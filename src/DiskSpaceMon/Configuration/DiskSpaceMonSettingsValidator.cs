using DiskSpaceMon.Monitoring;
using Microsoft.Extensions.Options;

namespace DiskSpaceMon.Configuration;

/// <summary>
/// Checks that a loaded settings file could actually do its job, so a typo is reported at
/// startup with the setting named rather than as a failure hours later when a disk fills.
/// </summary>
public sealed class DiskSpaceMonSettingsValidator : IValidateOptions<DiskSpaceMonSettings>
{
    /// <summary>The smallest sweep interval accepted.</summary>
    public const int MinimumIntervalSeconds = 5;

    /// <inheritdoc/>
    public ValidateOptionsResult Validate(string? name, DiskSpaceMonSettings options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();
        ValidateMonitoring(options.Monitoring, failures);
        ValidateAlerts(options.Alerts, failures);
        ValidateEmail(options.Email, failures);

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private static void ValidateMonitoring(MonitoringSettings monitoring, List<string> failures)
    {
        if (monitoring.IntervalSeconds < MinimumIntervalSeconds)
        {
            failures.Add(
                $"monitoring/intervalSeconds is {monitoring.IntervalSeconds}; it must be at "
                + $"least {MinimumIntervalSeconds}.");
        }

        var enabled = monitoring.Disks.Where(d => d.Enabled).ToList();

        if (enabled.Count == 0 && !monitoring.IncludeAllFixedDrives)
        {
            failures.Add(
                "Nothing is being watched: monitoring/disks has no enabled entry and "
                + "monitoring/includeAllFixedDrives is false.");
        }

        if (monitoring.IncludeAllFixedDrives && !monitoring.Defaults.IsConfigured)
        {
            failures.Add(
                "monitoring/includeAllFixedDrives is true, so monitoring/defaults must set "
                + "minFreePercent or minFreeGb for the drives that are discovered.");
        }

        for (var i = 0; i < monitoring.Disks.Count; i++)
        {
            var disk = monitoring.Disks[i];
            var where = $"monitoring/disks[{i}]";

            if (string.IsNullOrWhiteSpace(disk.Path))
            {
                failures.Add($"{where} has no path.");
                continue;
            }

            if (!disk.Enabled) continue;

            if (!disk.IsConfigured && !monitoring.Defaults.IsConfigured)
            {
                failures.Add(
                    $"{where} ('{disk.Path}') sets no threshold and monitoring/defaults sets "
                    + "none either, so it could never alert.");
            }

            if (disk.MinFreePercent is < 0 or > 100)
                failures.Add($"{where} minFreePercent must be between 0 and 100.");

            if (disk.MinFreeGb is < 0)
                failures.Add($"{where} minFreeGb cannot be negative.");
        }

        if (monitoring.Defaults.MinFreePercent is < 0 or > 100)
            failures.Add("monitoring/defaults/minFreePercent must be between 0 and 100.");

        if (monitoring.Defaults.MinFreeGb is < 0)
            failures.Add("monitoring/defaults/minFreeGb cannot be negative.");

        // Compared in canonical form, so 'C:' and 'C:\' are caught as the one drive they are.
        var duplicates = monitoring.Disks
            .Select(d => DiskPath.Normalise(d.Path))
            .Where(path => path.Length > 0)
            .GroupBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key);

        foreach (var duplicate in duplicates)
            failures.Add($"monitoring/disks lists '{duplicate}' more than once.");
    }

    private static void ValidateAlerts(AlertSettings alerts, List<string> failures)
    {
        if (string.IsNullOrWhiteSpace(alerts.StateFile))
            failures.Add("alerts/stateFile cannot be blank.");
    }

    private static void ValidateEmail(EmailSettings email, List<string> failures)
    {
        if (!email.Enabled) return;

        if (string.IsNullOrWhiteSpace(email.From))
            failures.Add("email/from is required when email is enabled.");

        if (Recipients.Split(email.To).Count == 0
            && Recipients.Split(email.Cc).Count == 0
            && Recipients.Split(email.Bcc).Count == 0)
        {
            failures.Add("email needs at least one recipient in to, cc or bcc.");
        }

        if (string.IsNullOrWhiteSpace(email.Templates.Alert))
            failures.Add("email/templates/alert cannot be blank.");

        if (string.IsNullOrWhiteSpace(email.Templates.Recovery))
            failures.Add("email/templates/recovery cannot be blank.");

        switch (email.Provider)
        {
            case EmailProvider.Smtp:
                if (string.IsNullOrWhiteSpace(email.Smtp.Host))
                    failures.Add("email/smtp/host is required when provider is Smtp.");
                if (email.Smtp.Port is < 1 or > 65535)
                    failures.Add("email/smtp/port must be between 1 and 65535.");
                break;

            case EmailProvider.Graph:
                if (string.IsNullOrWhiteSpace(email.Graph.TenantId))
                    failures.Add("email/graph/tenantId is required when provider is Graph.");
                if (string.IsNullOrWhiteSpace(email.Graph.ClientId))
                    failures.Add("email/graph/clientId is required when provider is Graph.");
                if (string.IsNullOrWhiteSpace(email.Graph.ClientSecret))
                    failures.Add("email/graph/clientSecret is required when provider is Graph.");
                break;

            default:
                failures.Add(
                    $"email/provider is '{email.Provider}'; expected Smtp or Graph.");
                break;
        }
    }
}

/// <summary>Splits the delimited recipient lists that the settings file uses.</summary>
public static class Recipients
{
    private static readonly char[] Separators = [',', ';', ' ', '\r', '\n', '\t'];

    /// <summary>
    /// Splits <paramref name="value"/> on commas, semicolons and whitespace, dropping blanks.
    /// </summary>
    /// <param name="value">The delimited list, possibly null.</param>
    /// <returns>The individual addresses, in order.</returns>
    public static IReadOnlyList<string> Split(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split(Separators, StringSplitOptions.RemoveEmptyEntries
                                      | StringSplitOptions.TrimEntries);

    /// <summary>
    /// Describes where a message went, for the log and for <c>test-email</c>.
    /// </summary>
    /// <remarks>
    /// Every field is named, because reporting only <c>to</c> would say a message went nowhere
    /// whenever the recipients are all on <c>bcc</c> -- which is a perfectly ordinary way to
    /// configure an alert, and which made the log read "Sent ... to ." until this existed.
    /// </remarks>
    /// <param name="to">The <c>to</c> list.</param>
    /// <param name="cc">The <c>cc</c> list.</param>
    /// <param name="bcc">The <c>bcc</c> list.</param>
    /// <returns>
    /// Something like <c>to a@b.com; bcc c@d.com</c>, or <c>no recipients</c> if all three are
    /// empty.
    /// </returns>
    public static string Describe(string? to, string? cc, string? bcc)
    {
        var parts = new List<string>(3);
        Add("to", to);
        Add("cc", cc);
        Add("bcc", bcc);

        return parts.Count == 0 ? "no recipients" : string.Join("; ", parts);

        void Add(string label, string? value)
        {
            var addresses = Split(value);
            if (addresses.Count > 0) parts.Add($"{label} {string.Join(", ", addresses)}");
        }
    }
}
