using System.Globalization;
using DiskSpaceMon.Configuration;
using DiskSpaceMon.Monitoring;
using DiskSpaceMon.Notifications;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace DiskSpaceMon.Hosting;

/// <summary>
/// The one-shot commands, which exist so an operator can prove a settings file works before
/// leaving a service to run on it.
/// </summary>
public static class Commands
{
    /// <summary>
    /// Reads every configured volume once and prints a table of what was found.
    /// </summary>
    /// <param name="services">The built host's services.</param>
    /// <param name="output">Where the table is written.</param>
    /// <returns>Zero if every volume is healthy, 2 if any is low, 1 if any could not be read.</returns>
    public static int Check(IServiceProvider services, TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(output);

        var settings = services.GetRequiredService<IOptionsMonitor<DiskSpaceMonSettings>>().CurrentValue;
        var statuses = services.GetRequiredService<DiskScanner>().Scan(settings.Monitoring);

        if (statuses.Count == 0)
        {
            output.WriteLine("No volumes are configured. Check monitoring/disks in settings.xml.");
            return 1;
        }

        var rows = statuses.Select(Row).ToList();
        string[] headers = ["VOLUME", "PATH", "TOTAL", "FREE", "FREE %", "THRESHOLD", "STATE"];

        var widths = headers
            .Select((header, column) =>
                Math.Max(header.Length, rows.Max(row => row[column].Length)))
            .ToArray();

        output.WriteLine(Line(headers, widths));
        output.WriteLine(string.Join("  ", widths.Select(width => new string('-', width))));
        foreach (var row in rows) output.WriteLine(Line(row, widths));

        var unreadable = statuses.Count(status => !status.IsReadable);
        var breaching = statuses.Count(status => status.IsBreaching);

        output.WriteLine();
        output.WriteLine(
            $"{statuses.Count} volume(s): {breaching} below threshold, {unreadable} unreadable.");

        if (unreadable > 0) return 1;
        return breaching > 0 ? 2 : 0;
    }

    /// <summary>
    /// Sends one alert email describing every volume that can be read, whatever its state, so
    /// the operator can confirm the transport and the template before an incident needs them.
    /// </summary>
    /// <param name="services">The built host's services.</param>
    /// <param name="output">Where progress is written.</param>
    /// <param name="cancellationToken">Cancellation for the send.</param>
    /// <returns>Zero if the email went out, non-zero otherwise.</returns>
    public static async Task<int> TestEmailAsync(
        IServiceProvider services, TextWriter output, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(output);

        var settings = services.GetRequiredService<IOptionsMonitor<DiskSpaceMonSettings>>().CurrentValue;

        if (!settings.Email.Enabled)
        {
            output.WriteLine("Email is disabled. Set email/enabled to true in settings.xml.");
            return 1;
        }

        var nowUtc = services.GetRequiredService<TimeProvider>().GetUtcNow();
        var statuses = services.GetRequiredService<DiskScanner>().Scan(settings.Monitoring);

        var readable = statuses.Where(status => status.IsReadable).ToList();
        if (readable.Count == 0)
        {
            output.WriteLine("No volume could be read, so there is nothing to put in the email.");
            return 1;
        }

        // Every readable volume is reported as a new breach. The point is to exercise the
        // template and the transport, and a test that only works when a disk happens to be full
        // is a test nobody can run.
        var decisions = readable
            .Select(status => new AlertDecision(status, AlertAction.Alert, nowUtc))
            .ToList();

        output.WriteLine(
            $"Sending a test alert for {decisions.Count} volume(s) via {settings.Email.Provider} "
            + $"to {Recipients.Describe(settings.Email.To, settings.Email.Cc, settings.Email.Bcc)}...");

        var outcome = await services.GetRequiredService<AlertNotifier>()
            .NotifyAsync(AlertEmailKind.Alert, decisions, settings.Email, nowUtc, cancellationToken)
            .ConfigureAwait(false);

        if (outcome == NotifyOutcome.Sent)
        {
            output.WriteLine("Sent.");
            return 0;
        }

        output.WriteLine("Not sent. The reason is in the log above.");
        return 1;
    }

    /// <summary>
    /// Renders both emails to files instead of sending them, so a template edit can be checked
    /// in a browser rather than by filling a disk and waiting for the post.
    /// </summary>
    /// <param name="services">The built host's services.</param>
    /// <param name="output">Where the file paths are written.</param>
    /// <param name="cancellationToken">Cancellation for the render.</param>
    /// <returns>Zero if both files were written, non-zero otherwise.</returns>
    public static async Task<int> PreviewAsync(
        IServiceProvider services, TextWriter output, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(output);

        var settings = services.GetRequiredService<IOptionsMonitor<DiskSpaceMonSettings>>().CurrentValue;
        var nowUtc = services.GetRequiredService<TimeProvider>().GetUtcNow();
        var paths = services.GetRequiredService<AppPaths>();
        var composer = services.GetRequiredService<AlertEmailComposer>();

        var statuses = services.GetRequiredService<DiskScanner>()
            .Scan(settings.Monitoring)
            .Where(status => status.IsReadable)
            .ToList();

        if (statuses.Count == 0)
        {
            output.WriteLine("No volume could be read, so there is nothing to preview.");
            return 1;
        }

        try
        {
            await WriteAsync(AlertEmailKind.Alert, AlertAction.Alert, "preview-alert.html")
                .ConfigureAwait(false);

            await WriteAsync(AlertEmailKind.Recovery, AlertAction.Recovered, "preview-recovery.html")
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is FileNotFoundException or FormatException
                                      or InvalidOperationException or IOException)
        {
            output.WriteLine($"Could not render the templates: {ex.Message}");
            return 1;
        }

        return 0;

        async Task WriteAsync(AlertEmailKind kind, AlertAction action, string fileName)
        {
            var decisions = statuses
                .Select(status => new AlertDecision(status, action, nowUtc.AddHours(-3)))
                .ToList();

            var message = await composer
                .ComposeAsync(kind, decisions, settings.Email, nowUtc, cancellationToken)
                .ConfigureAwait(false);

            var file = paths.Resolve(fileName);
            await File.WriteAllTextAsync(file, message.HtmlBody, cancellationToken)
                .ConfigureAwait(false);

            output.WriteLine($"{message.Subject}");
            output.WriteLine($"  {file}");
        }
    }

    private static string[] Row(DiskStatus status)
    {
        if (!status.IsReadable)
        {
            return
            [
                status.DisplayName, status.Target.Path, "-", "-", "-",
                Threshold(status.Target), "UNREADABLE"
            ];
        }

        var reading = status.Reading!;

        return
        [
            status.DisplayName,
            status.Target.Path,
            ByteSize.Format(reading.TotalBytes),
            ByteSize.Format(reading.FreeBytes),
            ByteSize.FormatPercent(reading.FreePercent),
            Threshold(status.Target),
            status.IsBreaching ? "LOW" : "ok"
        ];
    }

    private static string Threshold(DiskTarget target)
    {
        var parts = new List<string>(2);

        if (target.MinFreePercent is > 0)
            parts.Add(ByteSize.FormatPercent(target.MinFreePercent.Value));

        if (target.MinFreeGb is > 0)
            parts.Add(string.Format(CultureInfo.InvariantCulture, "{0:0.#} GB", target.MinFreeGb.Value));

        return parts.Count == 0 ? "none" : string.Join(" or ", parts);
    }

    private static string Line(IReadOnlyList<string> cells, IReadOnlyList<int> widths)
        => string.Join("  ", cells.Select((cell, column) => cell.PadRight(widths[column]))).TrimEnd();
}
