using DiskMon.Configuration;

namespace DiskMon.Monitoring;

/// <summary>What a sweep decided to do about one volume.</summary>
public enum AlertAction
{
    /// <summary>Nothing to say about it this time.</summary>
    None,

    /// <summary>It has just gone below its threshold.</summary>
    Alert,

    /// <summary>It is still below, and the quiet period has elapsed.</summary>
    ReAlert,

    /// <summary>It has climbed back above its threshold.</summary>
    Recovered
}

/// <summary>One volume and what should happen about it.</summary>
/// <param name="Status">The volume's reading and reasons.</param>
/// <param name="Action">What to do.</param>
/// <param name="FirstBreachUtc">
/// When this volume first went below its threshold, where that is already known. A re-alert can
/// then say how long it has been low, which is the part that tells a reader whether anyone is
/// dealing with it.
/// </param>
public sealed record AlertDecision(
    DiskStatus Status, AlertAction Action, DateTimeOffset? FirstBreachUtc);

/// <summary>
/// Decides which volumes are worth an email, given what has already been sent.
/// </summary>
/// <remarks>
/// Deciding is kept separate from recording, because the record must only move once an email has
/// actually gone out. If the mail server is unreachable, marking the disk as alerted anyway would
/// buy silence for the whole cooldown -- exactly when someone needs to hear about it.
/// </remarks>
public static class AlertCoordinator
{
    /// <summary>
    /// Works out what to say about each volume.
    /// </summary>
    /// <param name="statuses">What the sweep found.</param>
    /// <param name="state">The history, which this method does not modify.</param>
    /// <param name="settings">The cooldown and recovery preferences.</param>
    /// <param name="nowUtc">The moment the sweep ran.</param>
    /// <returns>Only the volumes with something to report, in the order they were scanned.</returns>
    public static IReadOnlyList<AlertDecision> Decide(
        IReadOnlyList<DiskStatus> statuses,
        AlertStateFile state,
        AlertSettings settings,
        DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(statuses);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(settings);

        var decisions = new List<AlertDecision>();

        foreach (var status in statuses)
        {
            // A volume that could not be read is unknown, not recovered. Reporting recovery for
            // an unplugged drive would be a lie, and a comforting one.
            if (!status.IsReadable) continue;

            var previous = state.Disks.GetValueOrDefault(status.Target.Path);
            var action = Decide(status, previous, settings, nowUtc);

            if (action != AlertAction.None)
            {
                decisions.Add(new AlertDecision(
                    status, action, previous?.FirstBreachUtc ?? NowIfNew(action, nowUtc)));
            }
        }

        return decisions;
    }

    /// <summary>
    /// A volume going below its threshold for the first time has no recorded start, so the
    /// sweep that found it is the start.
    /// </summary>
    private static DateTimeOffset? NowIfNew(AlertAction action, DateTimeOffset nowUtc)
        => action == AlertAction.Alert ? nowUtc : null;

    private static AlertAction Decide(
        DiskStatus status,
        DiskAlertState? previous,
        AlertSettings settings,
        DateTimeOffset nowUtc)
    {
        var wasBreaching = previous?.IsBreaching == true;

        if (status.IsBreaching)
        {
            if (!wasBreaching) return AlertAction.Alert;
            if (settings.ReAlertAfterHours <= 0) return AlertAction.None;

            var last = previous?.LastAlertUtc;
            if (last is null) return AlertAction.ReAlert;

            return nowUtc - last.Value >= TimeSpan.FromHours(settings.ReAlertAfterHours)
                ? AlertAction.ReAlert
                : AlertAction.None;
        }

        return wasBreaching && settings.SendRecoveryEmail
            ? AlertAction.Recovered
            : AlertAction.None;
    }

    /// <summary>
    /// Records that a decision has been acted on.
    /// </summary>
    /// <param name="state">The history to update in place.</param>
    /// <param name="decision">The decision whose email has been sent.</param>
    /// <param name="nowUtc">The moment the sweep ran.</param>
    public static void Commit(
        AlertStateFile state, AlertDecision decision, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(decision);

        var path = decision.Status.Target.Path;

        if (decision.Action == AlertAction.Recovered)
        {
            state.Disks.Remove(path);
            return;
        }

        var entry = state.For(path);
        entry.IsBreaching = true;
        entry.FirstBreachUtc ??= nowUtc;
        entry.LastAlertUtc = nowUtc;
    }

    /// <summary>
    /// Records that a volume dropped below its threshold, whether or not an email went out.
    /// </summary>
    /// <remarks>
    /// A breach that could not be emailed still has to be remembered, otherwise the disk looks
    /// newly broken at every sweep and the recovery notice never fires. Leaving
    /// <see cref="DiskAlertState.LastAlertUtc"/> alone is what makes the next sweep try again
    /// instead of waiting out the cooldown.
    /// </remarks>
    /// <param name="state">The history to update in place.</param>
    /// <param name="decision">The decision whose email failed.</param>
    /// <param name="nowUtc">The moment the sweep ran.</param>
    public static void CommitUnsent(
        AlertStateFile state, AlertDecision decision, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(decision);

        if (decision.Action == AlertAction.Recovered) return;

        var entry = state.For(decision.Status.Target.Path);
        entry.IsBreaching = true;
        entry.FirstBreachUtc ??= nowUtc;
    }

    /// <summary>
    /// Drops history for volumes that are no longer being watched, so a path removed from the
    /// settings file does not sit in the state file forever.
    /// </summary>
    /// <param name="state">The history to prune in place.</param>
    /// <param name="watched">The volumes the current settings watch.</param>
    public static void Prune(AlertStateFile state, IEnumerable<DiskTarget> watched)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(watched);

        var keep = new HashSet<string>(
            watched.Select(target => target.Path), StringComparer.OrdinalIgnoreCase);

        foreach (var path in state.Disks.Keys.Where(path => !keep.Contains(path)).ToList())
            state.Disks.Remove(path);
    }
}
