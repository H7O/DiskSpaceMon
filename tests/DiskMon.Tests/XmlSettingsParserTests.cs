using System.Text;
using DiskMon.Configuration;
using Microsoft.Extensions.Configuration;

namespace DiskMon.Tests;

public class XmlSettingsParserTests
{
    private static IDictionary<string, string?> Parse(string xml, params string[] collections)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));
        return XmlSettingsParser.Parse(stream, collections);
    }

    [Fact]
    public void RootElementNameIsNotAKey()
    {
        var data = Parse("<diskmon><monitoring><intervalSeconds>60</intervalSeconds></monitoring></diskmon>");

        Assert.Equal("60", data["monitoring:intervalSeconds"]);
        Assert.DoesNotContain(data.Keys, key => key.StartsWith("diskmon", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void KeysAreCaseInsensitive()
    {
        var data = Parse("<r><Email><From>a@b.com</From></Email></r>");

        Assert.Equal("a@b.com", data["email:from"]);
    }

    [Fact]
    public void CDataIsReadAsAValue()
    {
        var data = Parse("<r><subject><![CDATA[Low space & rising <alert>]]></subject></r>");

        Assert.Equal("Low space & rising <alert>", data["subject"]);
    }

    [Fact]
    public void AttributesBecomeChildKeys()
    {
        var data = Parse("""<r><smtp host="mail.example.com" port="25" /></r>""");

        Assert.Equal("mail.example.com", data["smtp:host"]);
        Assert.Equal("25", data["smtp:port"]);
    }

    [Fact]
    public void BlankElementMeansNotSet()
    {
        // The whole point: <minFreeGb></minFreeGb> must fall back to the default rather than
        // binding as an empty string, which a non-nullable target would reject outright.
        var data = Parse("<r><a>7</a><b></b><c>   </c></r>");

        Assert.Equal("7", data["a"]);
        Assert.False(data.ContainsKey("b"));
        Assert.False(data.ContainsKey("c"));
    }

    [Fact]
    public void DeclaredCollectionChildrenAreIndexedAndTheirNameDropped()
    {
        var data = Parse(
            """
            <r>
              <monitoring>
                <disks>
                  <disk><path>C:\</path></disk>
                  <disk><path>D:\</path></disk>
                </disks>
              </monitoring>
            </r>
            """,
            "monitoring:disks");

        Assert.Equal(@"C:\", data["monitoring:disks:0:path"]);
        Assert.Equal(@"D:\", data["monitoring:disks:1:path"]);
    }

    [Fact]
    public void ACollectionWithOneEntryStillIndexesIt()
    {
        // Deleting all but one disk is the moment a shape-guessing parser would quietly stop
        // binding the list, so this is the case that matters most.
        var data = Parse(
            @"<r><monitoring><disks><disk><path>C:\</path></disk></disks></monitoring></r>",
            "monitoring:disks");

        Assert.Equal(@"C:\", data["monitoring:disks:0:path"]);
    }

    [Fact]
    public void AnEmptyCollectionProducesNoKeys()
    {
        var data = Parse("<r><monitoring><disks /></monitoring></r>", "monitoring:disks");

        Assert.DoesNotContain(data.Keys, key => key.StartsWith("monitoring:disks:", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void RepeatingAnElementOutsideACollectionIsReportedWithItsLine()
    {
        var exception = Assert.Throws<FormatException>(() => Parse(
            """
            <r>
              <email>
                <from>a@b.com</from>
                <from>c@d.com</from>
              </email>
            </r>
            """));

        Assert.Contains("email:from", exception.Message);
        Assert.Contains("line 4", exception.Message);
    }

    [Fact]
    public void SettingTheSameValueTwiceIsReported()
    {
        var exception = Assert.Throws<FormatException>(
            () => Parse("""<r><smtp host="a"><host>b</host></smtp></r>"""));

        Assert.Contains("smtp:host", exception.Message);
    }

    [Fact]
    public void MalformedXmlIsReportedWithItsLine()
    {
        var exception = Assert.Throws<FormatException>(() => Parse("<r>\n  <a>1</b>\n</r>"));

        Assert.Contains("not well-formed", exception.Message);
        Assert.Contains("line 2", exception.Message);
    }

    [Fact]
    public void AnEmptyDocumentIsReported()
    {
        Assert.Throws<FormatException>(() => Parse(""));
    }

    [Fact]
    public void TheShippedSettingsFileBindsToTheSettingsModel()
    {
        // The file an operator actually edits, parsed and bound exactly as the application does.
        var settings = Bind(SettingsFile.FilePath);

        Assert.Equal(300, settings.Monitoring.IntervalSeconds);
        Assert.Equal(10, settings.Monitoring.Defaults.MinFreePercent);
        Assert.Null(settings.Monitoring.Defaults.MinFreeGb);

        Assert.Equal(2, settings.Monitoring.Disks.Count);
        Assert.Equal(@"C:\", settings.Monitoring.Disks[0].Path);
        Assert.Equal("System", settings.Monitoring.Disks[0].Label);
        Assert.Equal(15, settings.Monitoring.Disks[0].MinFreePercent);
        Assert.True(settings.Monitoring.Disks[0].Enabled);
        Assert.False(settings.Monitoring.Disks[1].Enabled);

        Assert.Equal(6, settings.Alerts.ReAlertAfterHours);
        Assert.True(settings.Alerts.SendRecoveryEmail);

        Assert.Equal(EmailProvider.Smtp, settings.Email.Provider);
        Assert.Equal(587, settings.Email.Smtp.Port);
        Assert.Contains("{{machineName}}", settings.Email.SubjectAlert);
    }

    [Fact]
    public void AnEnvironmentVariableOverridesTheFile()
    {
        // The layering the deployment story depends on: a secret set as an Azure app setting or a
        // container variable must beat whatever is written in the file.
        const string variable = "DISKMONTEST_email__smtp__password";
        Environment.SetEnvironmentVariable(variable, "from-the-environment");

        try
        {
            var configuration = new ConfigurationBuilder()
                .AddXmlSettingsFile(
                    SettingsFile.FilePath, DiskMonSettings.CollectionPaths, optional: false)
                .AddEnvironmentVariables("DISKMONTEST_")
                .Build();

            Assert.Equal("from-the-environment", configuration["email:smtp:password"]);
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, null);
        }
    }

    private static DiskMonSettings Bind(string path)
    {
        var configuration = new ConfigurationBuilder()
            .AddXmlSettingsFile(path, DiskMonSettings.CollectionPaths, optional: false)
            .Build();

        var settings = new DiskMonSettings();
        configuration.Bind(settings);
        return settings;
    }
}

/// <summary>Locates the shipped settings file inside the test output.</summary>
internal static class SettingsFile
{
    public static string Directory => Path.Combine(AppContext.BaseDirectory, "settings");

    public static string FilePath => Path.Combine(Directory, "settings.xml");
}
