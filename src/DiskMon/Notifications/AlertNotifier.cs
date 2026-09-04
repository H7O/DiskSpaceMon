using DiskMon.Configuration;
using DiskMon.Monitoring;
using Microsoft.Extensions.Logging;

namespace DiskMon.Notifications;

/// <summary>What happened when a sweep tried to report its findings.</summary>
public enum NotifyOutcome
{
    /// <summary>The email went out.</summary>
    Sent,

    /// <summary>Email is switched off, so the findings were logged instead.</summary>
    Suppressed,

    /// <summary>The email could not be sent and should be tried again next sweep.</summary>
    Failed
}

/// <summary>Renders a sweep's findings and sends them as one email.</summary>
/// <remarks>
/// One email per sweep, not one per volume: a machine whose disks fill together should produce a
/// message a reader can act on, not four of them arriving at once.
/// </remarks>
public sealed class AlertNotifier(
    AlertEmailComposer composer,
    EmailSenderSelector senders,
    ILogger<AlertNotifier> logger)
{
    /// <summary>
    /// Reports one group of findings.
    /// </summary>
    /// <param name="kind">Whether these are breaches or recoveries.</param>
    /// <param name="decisions">The volumes to report. An empty list is a no-op.</param>
    /// <param name="settings">Addresses, templates and transport.</param>
    /// <param name="nowUtc">The moment the sweep ran.</param>
    /// <param name="cancellationToken">Cancellation for the render and the send.</param>
    /// <returns>Whether the caller should record these findings as reported.</returns>
    public async Task<NotifyOutcome> NotifyAsync(
        AlertEmailKind kind,
        IReadOnlyList<AlertDecision> decisions,
        EmailSettings settings,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(decisions);
        ArgumentNullException.ThrowIfNull(settings);

        if (decisions.Count == 0) return NotifyOutcome.Suppressed;

        if (!settings.Enabled)
        {
            // Treated as reported rather than as a failure. Retrying forever would repeat the
            // same log line every sweep for as long as the disk stays low, which is the noise
            // the cooldown exists to prevent.
            logger.LogInformation(
                "Email is disabled; {Count} {Kind} finding(s) logged only: {Disks}",
                decisions.Count,
                kind,
                string.Join(", ", decisions.Select(d => d.Status.DisplayName)));

            return NotifyOutcome.Suppressed;
        }

        try
        {
            var message = await composer
                .ComposeAsync(kind, decisions, settings, nowUtc, cancellationToken)
                .ConfigureAwait(false);

            var sender = senders.For(settings.Provider);

            await sender.SendAsync(message, settings, cancellationToken).ConfigureAwait(false);

            logger.LogInformation(
                "Sent '{Subject}' via {Provider} to {Recipients}.",
                message.Subject,
                settings.Provider,
                string.Join(", ", Recipients.Split(settings.To)));

            return NotifyOutcome.Sent;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // A mail server outage must not stop the sweeps, and must not be recorded as though
            // the operator had been told.
            logger.LogError(
                ex, "Could not send the {Kind} email via {Provider}; will try again next sweep.",
                kind, settings.Provider);

            return NotifyOutcome.Failed;
        }
    }
}
