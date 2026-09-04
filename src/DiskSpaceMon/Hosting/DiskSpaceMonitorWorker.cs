using DiskSpaceMon.Configuration;
using DiskSpaceMon.Monitoring;
using DiskSpaceMon.Notifications;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DiskSpaceMon.Hosting;

/// <summary>
/// Sweeps the configured volumes on an interval and emails what changed.
/// </summary>
public sealed class DiskSpaceMonitorWorker(
    IOptionsMonitor<DiskSpaceMonSettings> options,
    DiskScanner scanner,
    IAlertStateStore stateStore,
    AlertNotifier notifier,
    TimeProvider time,
    ILogger<DiskSpaceMonitorWorker> logger) : BackgroundService
{
    private DiskSpaceMonSettings? _lastGoodSettings;
    private AlertStateFile _state = new();

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = CurrentSettings();

        logger.LogInformation(
            "DiskSpaceMon started. Sweeping every {Interval}s; email {EmailState} via {Provider}.",
            settings.Monitoring.IntervalSeconds,
            settings.Email.Enabled ? "enabled" : "disabled",
            settings.Email.Provider);

        _state = await stateStore.LoadAsync(stoppingToken).ConfigureAwait(false);

        while (!stoppingToken.IsCancellationRequested)
        {
            settings = CurrentSettings();

            try
            {
                await SweepAsync(settings, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Whatever went wrong, the next sweep is the thing that matters. A monitoring
                // service that exits on a bad sweep stops being monitoring.
                logger.LogError(ex, "The sweep failed. Continuing.");
            }

            var interval = TimeSpan.FromSeconds(Math.Max(
                settings.Monitoring.IntervalSeconds, DiskSpaceMonSettingsValidator.MinimumIntervalSeconds));

            try
            {
                await Task.Delay(interval, time, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        logger.LogInformation("DiskSpaceMon stopped.");
    }

    private async Task SweepAsync(DiskSpaceMonSettings settings, CancellationToken cancellationToken)
    {
        var nowUtc = time.GetUtcNow();
        var statuses = scanner.Scan(settings.Monitoring);

        LogFindings(statuses);

        var changed = Prune(statuses);

        var decisions = AlertCoordinator.Decide(statuses, _state, settings.Alerts, nowUtc);

        var breaches = decisions
            .Where(d => d.Action is AlertAction.Alert or AlertAction.ReAlert)
            .ToList();

        var recoveries = decisions
            .Where(d => d.Action is AlertAction.Recovered)
            .ToList();

        changed |= await ReportAsync(
            AlertEmailKind.Alert, breaches, settings, nowUtc, cancellationToken)
            .ConfigureAwait(false);

        changed |= await ReportAsync(
            AlertEmailKind.Recovery, recoveries, settings, nowUtc, cancellationToken)
            .ConfigureAwait(false);

        if (changed) await stateStore.SaveAsync(_state, cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> ReportAsync(
        AlertEmailKind kind,
        IReadOnlyList<AlertDecision> decisions,
        DiskSpaceMonSettings settings,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        if (decisions.Count == 0) return false;

        var outcome = await notifier
            .NotifyAsync(kind, decisions, settings.Email, nowUtc, cancellationToken)
            .ConfigureAwait(false);

        foreach (var decision in decisions)
        {
            if (outcome == NotifyOutcome.Failed)
                AlertCoordinator.CommitUnsent(_state, decision, nowUtc);
            else
                AlertCoordinator.Commit(_state, decision, nowUtc);
        }

        return true;
    }

    private bool Prune(IReadOnlyList<DiskStatus> statuses)
    {
        var before = _state.Disks.Count;
        AlertCoordinator.Prune(_state, statuses.Select(status => status.Target));
        return _state.Disks.Count != before;
    }

    private void LogFindings(IReadOnlyList<DiskStatus> statuses)
    {
        foreach (var status in statuses.Where(s => s.IsReadable))
        {
            if (status.IsBreaching)
            {
                logger.LogWarning(
                    "{Name} ({Path}): {Reasons}.",
                    status.DisplayName, status.Target.Path, string.Join("; ", status.Reasons));
            }
            else
            {
                logger.LogDebug(
                    "{Name} ({Path}): {Free} free of {Total} ({Percent}).",
                    status.DisplayName,
                    status.Target.Path,
                    ByteSize.Format(status.Reading!.FreeBytes),
                    ByteSize.Format(status.Reading.TotalBytes),
                    ByteSize.FormatPercent(status.Reading.FreePercent));
            }
        }

        var readable = statuses.Count(s => s.IsReadable);
        var breaching = statuses.Count(s => s.IsBreaching);

        logger.LogInformation(
            "Swept {Readable} of {Total} volume(s); {Breaching} below threshold.",
            readable, statuses.Count, breaching);
    }

    /// <summary>
    /// Reads the current settings, falling back to the last set that loaded and validated.
    /// </summary>
    /// <remarks>
    /// The settings file is watched, so a typo saved at 2am would otherwise take the monitoring
    /// down with it. Keeping the last good copy means a bad edit is loud in the log and harmless
    /// in effect.
    /// </remarks>
    private DiskSpaceMonSettings CurrentSettings()
    {
        try
        {
            _lastGoodSettings = options.CurrentValue;
        }
        catch (Exception ex)
        {
            if (_lastGoodSettings is null) throw;

            logger.LogError(
                ex, "settings.xml could not be loaded. Continuing with the last settings that "
                    + "worked; fix the file and it will be picked up automatically.");
        }

        return _lastGoodSettings!;
    }
}
