using System.Globalization;
using Com.H.Text.Template2;
using DiskSpaceMon.Configuration;
using DiskSpaceMon.Hosting;
using DiskSpaceMon.Monitoring;

namespace DiskSpaceMon.Notifications;

/// <summary>Which of the two emails is being built.</summary>
public enum AlertEmailKind
{
    /// <summary>Volumes are below their thresholds.</summary>
    Alert,

    /// <summary>Volumes have climbed back above them.</summary>
    Recovery
}

/// <summary>
/// Renders a sweep's findings into a subject and an HTML body, through
/// <a href="https://github.com/H7O/Com.H.Text.Template2">Com.H.Text.Template2</a>.
/// </summary>
/// <remarks>
/// Both the subject and the body are templates, so the wording of an alert is something an
/// operator changes in <c>settings/</c> rather than something that needs a rebuild.
/// </remarks>
public sealed class AlertEmailComposer(AppPaths paths)
{
    /// <summary>The name the templates use for the repeating set of volumes.</summary>
    public const string DiskRowSet = "disks";

    /// <summary>
    /// Builds one email covering every volume in <paramref name="decisions"/>.
    /// </summary>
    /// <param name="kind">Which template and subject to use.</param>
    /// <param name="decisions">The volumes to report. Must not be empty.</param>
    /// <param name="settings">Subjects, template paths and addresses.</param>
    /// <param name="nowUtc">The moment the sweep ran.</param>
    /// <param name="cancellationToken">Cancellation for the render.</param>
    /// <returns>The rendered message.</returns>
    /// <exception cref="FileNotFoundException">The configured template does not exist.</exception>
    public async Task<EmailMessage> ComposeAsync(
        AlertEmailKind kind,
        IReadOnlyList<AlertDecision> decisions,
        EmailSettings settings,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(decisions);
        ArgumentNullException.ThrowIfNull(settings);

        var templatePath = paths.Resolve(
            kind == AlertEmailKind.Alert ? settings.Templates.Alert : settings.Templates.Recovery);

        if (!File.Exists(templatePath))
        {
            throw new FileNotFoundException(
                $"The {kind.ToString().ToLowerInvariant()} email template was not found. "
                + $"Check email/templates in settings.xml.", templatePath);
        }

        var rows = decisions.Select(d => BuildRow(d, nowUtc)).ToList();
        var model = BuildModel(kind, decisions, nowUtc);

        ITemplateDataProvider provider = new ModelTemplateDataProvider(
            new Dictionary<string, IReadOnlyList<object>>(StringComparer.OrdinalIgnoreCase)
            {
                [DiskRowSet] = rows
            });

        var subjectTemplate = kind == AlertEmailKind.Alert
            ? settings.SubjectAlert
            : settings.SubjectRecovery;

        // The subject is rendered without the provider: it has no repeating block, and handing it
        // one would let a stray tag in the subject line multiply the subject itself.
        var subject = await subjectTemplate
            .RenderContentAsync(provider: null, model, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var body = await new Uri(templatePath)
            .RenderContentAsync(provider, model, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return new EmailMessage(
            Collapse(subject) ?? subjectTemplate,
            body ?? "");
    }

    private static Dictionary<string, object> BuildModel(
        AlertEmailKind kind, IReadOnlyList<AlertDecision> decisions, DateTimeOffset nowUtc)
    {
        var count = decisions.Count;
        var noun = count == 1 ? "volume" : "volumes";

        return new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
        {
            ["appName"] = "DiskSpaceMon",
            ["machineName"] = Environment.MachineName,
            ["diskCount"] = count.ToString(CultureInfo.InvariantCulture),
            ["generatedAt"] = nowUtc.ToLocalTime()
                .ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture),
            ["generatedAtUtc"] = nowUtc.UtcDateTime
                .ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture),
            ["summary"] = kind == AlertEmailKind.Alert
                ? $"{count} {noun} below threshold"
                : $"{count} {noun} back above threshold"
        };
    }

    private static object BuildRow(AlertDecision decision, DateTimeOffset nowUtc)
    {
        var status = decision.Status;
        var reading = status.Reading!;
        var usedPercent = 100d - reading.FreePercent;

        return new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
        {
            ["name"] = status.DisplayName,
            ["path"] = status.Target.Path,
            ["free"] = ByteSize.Format(reading.FreeBytes),
            ["freePercent"] = ByteSize.FormatPercent(reading.FreePercent),
            ["used"] = ByteSize.Format(reading.UsedBytes),
            ["usedPercent"] = ByteSize.FormatPercent(usedPercent),
            ["total"] = ByteSize.Format(reading.TotalBytes),

            // Rounded to a whole number because it is only ever a bar width in the email.
            ["usedBarPercent"] = Math.Clamp(Math.Round(usedPercent), 0, 100)
                .ToString(CultureInfo.InvariantCulture),

            ["threshold"] = DescribeThreshold(status.Target),
            ["reason"] = Reason(decision),
            ["state"] = decision.Action switch
            {
                AlertAction.Alert => "New",
                AlertAction.ReAlert => "Still low",
                AlertAction.Recovered => "Recovered",
                _ => ""
            },
            ["since"] = decision.FirstBreachUtc is { } since
                ? since.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)
                : "",
            ["lowFor"] = decision.FirstBreachUtc is { } start
                ? Humanise(nowUtc - start)
                : ""
        };
    }

    /// <summary>
    /// Says why the volume is in the email.
    /// </summary>
    /// <remarks>
    /// A volume with no breached limit reaches here two ways. A recovery is the ordinary one. The
    /// other is <c>DiskSpaceMon preview</c> or <c>DiskSpaceMon test-email</c>, which report healthy volumes
    /// on purpose so the layout and the mail route can be checked before an incident needs them --
    /// and those must not claim the disk just recovered.
    /// </remarks>
    private static string Reason(AlertDecision decision)
    {
        if (decision.Status.Reasons.Count > 0)
            return string.Join("; ", decision.Status.Reasons);

        return decision.Action == AlertAction.Recovered
            ? "back above its threshold"
            : "within its threshold (sample row)";
    }

    /// <summary>States the limits in the same words the settings file uses for them.</summary>
    private static string DescribeThreshold(DiskTarget target)
    {
        var parts = new List<string>(2);

        if (target.MinFreePercent is > 0)
            parts.Add(ByteSize.FormatPercent(target.MinFreePercent.Value));

        if (target.MinFreeGb is > 0)
            parts.Add(string.Format(CultureInfo.InvariantCulture, "{0:0.#} GB", target.MinFreeGb.Value));

        return parts.Count switch
        {
            0 => "none set",
            1 => parts[0],
            _ => string.Join(" or ", parts)
        };
    }

    /// <summary>
    /// Says how long something has been true in the roughest useful unit, because "3 days" is
    /// what a reader acts on and "3.04 days" is not.
    /// </summary>
    private static string Humanise(TimeSpan span)
    {
        if (span < TimeSpan.Zero) span = TimeSpan.Zero;

        if (span.TotalMinutes < 1) return "less than a minute";
        if (span.TotalHours < 1) return Plural((int)span.TotalMinutes, "minute");
        if (span.TotalDays < 1) return Plural((int)span.TotalHours, "hour");
        return Plural((int)span.TotalDays, "day");

        static string Plural(int count, string unit)
            => count == 1 ? $"1 {unit}" : $"{count} {unit}s";
    }

    /// <summary>
    /// Flattens a rendered subject onto one line. A template that spans lines is easy to write
    /// by accident, and a subject header cannot contain a line break.
    /// </summary>
    private static string? Collapse(string? subject)
    {
        if (string.IsNullOrWhiteSpace(subject)) return null;

        var collapsed = string.Join(
            ' ',
            subject.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries
                                        | StringSplitOptions.TrimEntries));

        return collapsed.Length == 0 ? null : collapsed;
    }
}
