using DiskMon.Configuration;
using Microsoft.Extensions.Logging;

namespace DiskMon.Monitoring;

/// <summary>One volume to watch, with the thresholds that apply to it.</summary>
/// <param name="Path">The root or mount point to read.</param>
/// <param name="Label">The name to use in an email, where the settings supplied one.</param>
/// <param name="MinFreePercent">Percentage limit, or null if only the absolute one applies.</param>
/// <param name="MinFreeGb">Absolute limit in gigabytes, or null.</param>
/// <param name="Discovered">
/// Whether the volume came from enumerating fixed drives rather than from an explicit entry.
/// </param>
public sealed record DiskTarget(
    string Path,
    string? Label,
    double? MinFreePercent,
    double? MinFreeGb,
    bool Discovered);

/// <summary>What a sweep found for one volume.</summary>
/// <param name="Target">The volume and its thresholds.</param>
/// <param name="Reading">The figures read, or null if the volume could not be read.</param>
/// <param name="Error">Why the read failed, or null.</param>
/// <param name="Reasons">
/// The thresholds that were breached, worded for a reader. Empty when the volume is healthy.
/// </param>
public sealed record DiskStatus(
    DiskTarget Target,
    DiskReading? Reading,
    string? Error,
    IReadOnlyList<string> Reasons)
{
    /// <summary>Whether at least one threshold was breached.</summary>
    public bool IsBreaching => Reasons.Count > 0;

    /// <summary>Whether the volume could be read at all.</summary>
    public bool IsReadable => Reading is not null;

    /// <summary>
    /// The name to show a reader: the configured label, else the volume label, else the path.
    /// </summary>
    public string DisplayName
        => Target.Label
           ?? (string.IsNullOrWhiteSpace(Reading?.VolumeLabel) ? null : Reading!.VolumeLabel)
           ?? Target.Path;
}

/// <summary>
/// Turns the monitoring settings into a list of volumes, reads each one, and says which are
/// below their thresholds.
/// </summary>
public sealed class DiskScanner(IDiskProbe probe, ILogger<DiskScanner> logger)
{
    /// <summary>
    /// Reads every configured volume once.
    /// </summary>
    /// <param name="monitoring">The section of the settings that says what to watch.</param>
    /// <returns>One status per volume, in the order the targets were resolved.</returns>
    public IReadOnlyList<DiskStatus> Scan(MonitoringSettings monitoring)
    {
        ArgumentNullException.ThrowIfNull(monitoring);

        var statuses = new List<DiskStatus>();

        foreach (var target in ResolveTargets(monitoring))
        {
            if (!probe.TryRead(target.Path, out var reading, out var error))
            {
                // A drive that has been removed or a mount point that has not come back yet is
                // an operational fact, not a crash. Log it and carry on with the others.
                logger.LogWarning("Could not read '{Path}': {Error}", target.Path, error);
                statuses.Add(new DiskStatus(target, Reading: null, error, Reasons: []));
                continue;
            }

            statuses.Add(new DiskStatus(target, reading, Error: null, Evaluate(target, reading)));
        }

        return statuses;
    }

    /// <summary>
    /// Works out which volumes to watch. Explicit entries win over discovered drives, so a
    /// tighter threshold on the system disk survives turning on
    /// <see cref="MonitoringSettings.IncludeAllFixedDrives"/>.
    /// </summary>
    /// <param name="monitoring">The section of the settings that says what to watch.</param>
    /// <returns>The volumes to read, explicit entries first.</returns>
    internal IReadOnlyList<DiskTarget> ResolveTargets(MonitoringSettings monitoring)
    {
        var targets = new List<DiskTarget>();
        var claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var disk in monitoring.Disks)
        {
            var path = DiskPath.Normalise(disk.Path);
            if (path.Length == 0) continue;

            // A disabled entry still claims its path, which is how it excludes that drive from
            // the discovered ones.
            if (!claimed.Add(path) || !disk.Enabled) continue;

            targets.Add(new DiskTarget(
                path,
                string.IsNullOrWhiteSpace(disk.Label) ? null : disk.Label.Trim(),
                disk.MinFreePercent ?? monitoring.Defaults.MinFreePercent,
                disk.MinFreeGb ?? monitoring.Defaults.MinFreeGb,
                Discovered: false));
        }

        if (!monitoring.IncludeAllFixedDrives) return targets;

        foreach (var root in probe.FixedDriveRootPaths())
        {
            var path = DiskPath.Normalise(root);
            if (path.Length == 0 || !claimed.Add(path)) continue;

            targets.Add(new DiskTarget(
                path,
                Label: null,
                monitoring.Defaults.MinFreePercent,
                monitoring.Defaults.MinFreeGb,
                Discovered: true));
        }

        return targets;
    }

    /// <summary>
    /// Compares a reading against its thresholds.
    /// </summary>
    /// <param name="target">The volume and its limits.</param>
    /// <param name="reading">What the volume reported.</param>
    /// <returns>
    /// One sentence per breached limit, or an empty list when the volume is healthy. Both limits
    /// are tested, so an email says every reason at once rather than one at a time.
    /// </returns>
    internal static IReadOnlyList<string> Evaluate(DiskTarget target, DiskReading reading)
    {
        var reasons = new List<string>(2);

        if (target.MinFreePercent is > 0 && reading.FreePercent < target.MinFreePercent)
        {
            reasons.Add(
                $"{ByteSize.FormatPercent(reading.FreePercent)} free, below the "
                + $"{ByteSize.FormatPercent(target.MinFreePercent.Value)} minimum");
        }

        if (target.MinFreeGb is > 0 && reading.FreeGb < target.MinFreeGb)
        {
            reasons.Add(
                $"{ByteSize.Format(reading.FreeBytes)} free, below the "
                + $"{target.MinFreeGb.Value:0.#} GB minimum");
        }

        return reasons;
    }
}
