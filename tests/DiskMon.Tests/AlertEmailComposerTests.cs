using DiskMon.Configuration;
using DiskMon.Hosting;
using DiskMon.Monitoring;
using DiskMon.Notifications;

namespace DiskMon.Tests;

/// <summary>
/// Exercises the shipped email templates through the real template engine, so a broken template
/// or a renamed row value fails here rather than in an operator's inbox during an incident.
/// </summary>
public class AlertEmailComposerTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 4, 8, 30, 0, TimeSpan.Zero);

    private static AlertEmailComposer Composer()
        => new(new AppPaths(AppContext.BaseDirectory));

    private static EmailSettings Settings() => new()
    {
        From = "diskmon@example.com",
        To = "ops@example.com",
        SubjectAlert = "[DiskMon] Low disk space on {{machineName}} ({{diskCount}})",
        SubjectRecovery = "[DiskMon] Disk space recovered on {{machineName}}",
        Templates =
        {
            Alert = "settings/templates/alert.html",
            Recovery = "settings/templates/recovery.html"
        }
    };

    [Fact]
    public async Task TheAlertBodyRepeatsOncePerVolume()
    {
        var decisions = new[]
        {
            Decision(@"C:\", "System", freeGb: 5, totalGb: 100, AlertAction.Alert),
            Decision(@"D:\", "Data", freeGb: 2, totalGb: 500, AlertAction.ReAlert)
        };

        var message = await Composer()
            .ComposeAsync(AlertEmailKind.Alert, decisions, Settings(), Now);

        Assert.Contains("System", message.HtmlBody);
        Assert.Contains("Data", message.HtmlBody);
        Assert.Contains(@"C:\", message.HtmlBody);
        Assert.Contains(@"D:\", message.HtmlBody);

        // The row template stamps each volume with data-volume, so this counts the repeats and
        // not the layout rows the surrounding table also uses.
        Assert.Equal(2, Occurrences(message.HtmlBody, "data-volume="));

        // Nothing may survive un-substituted, or the reader sees the marker instead of the value.
        Assert.DoesNotContain("{{", message.HtmlBody);
        Assert.DoesNotContain("h-embedded", message.HtmlBody);
    }

    [Fact]
    public async Task TheSubjectIsRenderedFromItsOwnTemplate()
    {
        var decisions = new[] { Decision(@"C:\", "System", 5, 100, AlertAction.Alert) };

        var message = await Composer()
            .ComposeAsync(AlertEmailKind.Alert, decisions, Settings(), Now);

        Assert.Equal($"[DiskMon] Low disk space on {Environment.MachineName} (1)", message.Subject);
    }

    [Fact]
    public async Task TheRecoveryTemplateRendersTheSameWay()
    {
        var decisions = new[] { Decision(@"C:\", "System", 60, 100, AlertAction.Recovered) };

        var message = await Composer()
            .ComposeAsync(AlertEmailKind.Recovery, decisions, Settings(), Now);

        Assert.Equal($"[DiskMon] Disk space recovered on {Environment.MachineName}", message.Subject);
        Assert.Contains("recovered", message.HtmlBody, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, Occurrences(message.HtmlBody, "data-volume="));
        Assert.DoesNotContain("{{", message.HtmlBody);
    }

    [Fact]
    public async Task ReadingsAreRenderedInTheUnitsTheMachineShows()
    {
        var decisions = new[] { Decision(@"C:\", "System", freeGb: 7.5, totalGb: 100, AlertAction.Alert) };

        var message = await Composer()
            .ComposeAsync(AlertEmailKind.Alert, decisions, Settings(), Now);

        Assert.Contains("7.5 GB", message.HtmlBody);
        Assert.Contains("7.5%", message.HtmlBody);
        Assert.Contains("100 GB", message.HtmlBody);
    }

    [Fact]
    public async Task AVolumeLabelIsEscapedRatherThanTreatedAsMarkup()
    {
        // A volume label is not something this application chose. If it contained markup and the
        // template wrote it verbatim, the email would break, or worse.
        var decisions = new[]
        {
            Decision(@"C:\", "Smith & Sons <Holdings>", 5, 100, AlertAction.Alert)
        };

        var message = await Composer()
            .ComposeAsync(AlertEmailKind.Alert, decisions, Settings(), Now);

        Assert.Contains("Smith &amp; Sons &lt;Holdings&gt;", message.HtmlBody);
        Assert.DoesNotContain("<Holdings>", message.HtmlBody);
    }

    [Fact]
    public async Task AMissingTemplateSaysWhichSettingToFix()
    {
        var settings = Settings();
        settings.Templates.Alert = "settings/templates/does-not-exist.html";

        var decisions = new[] { Decision(@"C:\", "System", 5, 100, AlertAction.Alert) };

        var exception = await Assert.ThrowsAsync<FileNotFoundException>(
            () => Composer().ComposeAsync(AlertEmailKind.Alert, decisions, settings, Now));

        Assert.Contains("email/templates", exception.Message);
    }

    [Fact]
    public async Task ASubjectSpanningLinesIsFlattened()
    {
        var settings = Settings();
        settings.SubjectAlert = "[DiskMon]\n  Low space on {{machineName}}";

        var decisions = new[] { Decision(@"C:\", "System", 5, 100, AlertAction.Alert) };

        var message = await Composer()
            .ComposeAsync(AlertEmailKind.Alert, decisions, settings, Now);

        Assert.Equal($"[DiskMon] Low space on {Environment.MachineName}", message.Subject);
    }

    private static AlertDecision Decision(
        string path, string label, double freeGb, double totalGb, AlertAction action)
    {
        var target = new DiskTarget(path, label, MinFreePercent: 10, MinFreeGb: 20, false);
        var reading = new DiskReading(path, null, Size.Gb(totalGb), Size.Gb(freeGb));
        var status = new DiskStatus(
            target, reading, Error: null, DiskScanner.Evaluate(target, reading));

        return new AlertDecision(status, action, Now.AddHours(-3));
    }

    private static int Occurrences(string text, string value)
    {
        var count = 0;
        var index = 0;

        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }
}
