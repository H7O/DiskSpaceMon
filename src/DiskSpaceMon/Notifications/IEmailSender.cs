using DiskSpaceMon.Configuration;

namespace DiskSpaceMon.Notifications;

/// <summary>A rendered email, ready to go out however the settings say.</summary>
/// <param name="Subject">The subject line, already rendered.</param>
/// <param name="HtmlBody">The body, already rendered.</param>
public sealed record EmailMessage(string Subject, string HtmlBody);

/// <summary>Sends an email through one transport.</summary>
public interface IEmailSender
{
    /// <summary>Which settings value selects this sender.</summary>
    EmailProvider Provider { get; }

    /// <summary>
    /// Sends one message.
    /// </summary>
    /// <param name="message">The rendered subject and body.</param>
    /// <param name="settings">
    /// Addresses and transport credentials. Passed in rather than injected so a send always uses
    /// the same settings snapshot the sweep was decided from, even if the file changes mid-sweep.
    /// </param>
    /// <param name="cancellationToken">Cancellation for the send.</param>
    /// <returns>A task that completes when the transport has accepted the message.</returns>
    /// <exception cref="Exception">
    /// The transport rejected the message. Callers treat any failure as "not sent" and try again
    /// on the next sweep.
    /// </exception>
    Task SendAsync(
        EmailMessage message, EmailSettings settings, CancellationToken cancellationToken = default);
}

/// <summary>
/// Picks the sender named by the settings.
/// </summary>
/// <remarks>
/// Both senders are constructed at startup and chosen from per send, so switching
/// <c>email/provider</c> in the settings file takes effect on the next sweep without a restart.
/// </remarks>
public sealed class EmailSenderSelector(IEnumerable<IEmailSender> senders)
{
    private readonly Dictionary<EmailProvider, IEmailSender> _senders =
        senders.ToDictionary(sender => sender.Provider);

    /// <summary>
    /// Returns the sender for a provider.
    /// </summary>
    /// <param name="provider">The value of <c>email/provider</c>.</param>
    /// <returns>The matching sender.</returns>
    /// <exception cref="InvalidOperationException">No sender is registered for it.</exception>
    public IEmailSender For(EmailProvider provider)
        => _senders.TryGetValue(provider, out var sender)
            ? sender
            : throw new InvalidOperationException(
                $"No email sender is registered for provider '{provider}'.");
}
