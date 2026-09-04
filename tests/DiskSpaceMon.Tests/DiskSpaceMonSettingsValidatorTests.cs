using DiskSpaceMon.Configuration;
using Microsoft.Extensions.Options;

namespace DiskSpaceMon.Tests;

public class DiskSpaceMonSettingsValidatorTests
{
    private static ValidateOptionsResult Validate(Action<DiskSpaceMonSettings> configure)
    {
        var settings = Valid();
        configure(settings);
        return new DiskSpaceMonSettingsValidator().Validate(null, settings);
    }

    private static DiskSpaceMonSettings Valid() => new()
    {
        Monitoring =
        {
            IntervalSeconds = 300,
            Disks = [new DiskSettings { Path = @"C:\", MinFreePercent = 10 }]
        },
        Email =
        {
            Enabled = true,
            From = "diskspacemon@example.com",
            To = "ops@example.com",
            Provider = EmailProvider.Smtp,
            Smtp = { Host = "smtp.example.com", Port = 587 }
        }
    };

    [Fact]
    public void AWorkableFilePasses()
        => Assert.True(Validate(_ => { }).Succeeded);

    [Fact]
    public void TooShortAnIntervalIsRejected()
    {
        var result = Validate(s => s.Monitoring.IntervalSeconds = 1);

        Assert.Contains(result.Failures!, f => f.Contains("intervalSeconds"));
    }

    [Fact]
    public void WatchingNothingIsRejected()
    {
        var result = Validate(s =>
        {
            s.Monitoring.Disks.Clear();
            s.Monitoring.IncludeAllFixedDrives = false;
        });

        Assert.Contains(result.Failures!, f => f.Contains("Nothing is being watched"));
    }

    [Fact]
    public void ADiskThatCouldNeverAlertIsRejected()
    {
        // Silently watching a disk with no threshold is the failure mode that looks like it is
        // working right up until the disk fills.
        var result = Validate(s => s.Monitoring.Disks = [new DiskSettings { Path = @"C:\" }]);

        Assert.Contains(result.Failures!, f => f.Contains("could never alert"));
    }

    [Fact]
    public void DiscoveringDrivesWithoutDefaultThresholdsIsRejected()
    {
        var result = Validate(s =>
        {
            s.Monitoring.IncludeAllFixedDrives = true;
            s.Monitoring.Defaults = new DiskThresholds();
        });

        Assert.Contains(result.Failures!, f => f.Contains("includeAllFixedDrives"));
    }

    [Fact]
    public void TheSameDriveWrittenTwoWaysIsRejected()
    {
        var result = Validate(s => s.Monitoring.Disks =
        [
            new DiskSettings { Path = @"C:\", MinFreePercent = 10 },
            new DiskSettings { Path = "C:", MinFreePercent = 20 }
        ]);

        Assert.Contains(result.Failures!, f => f.Contains("more than once"));
    }

    [Fact]
    public void SmtpNeedsAHost()
    {
        var result = Validate(s => s.Email.Smtp.Host = "");

        Assert.Contains(result.Failures!, f => f.Contains("email/smtp/host"));
    }

    [Fact]
    public void GraphNeedsItsThreeCredentials()
    {
        var result = Validate(s => s.Email.Provider = EmailProvider.Graph);

        Assert.Contains(result.Failures!, f => f.Contains("tenantId"));
        Assert.Contains(result.Failures!, f => f.Contains("clientId"));
        Assert.Contains(result.Failures!, f => f.Contains("clientSecret"));
    }

    [Fact]
    public void EmailNeedsSomewhereToGo()
    {
        var result = Validate(s =>
        {
            s.Email.To = "";
            s.Email.Cc = "";
            s.Email.Bcc = "";
        });

        Assert.Contains(result.Failures!, f => f.Contains("at least one recipient"));
    }

    [Fact]
    public void TurningEmailOffSkipsItsChecksEntirely()
    {
        var result = Validate(s =>
        {
            s.Email.Enabled = false;
            s.Email.From = "";
            s.Email.To = "";
            s.Email.Smtp.Host = "";
        });

        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData("a@b.com, c@d.com", 2)]
    [InlineData("a@b.com; c@d.com", 2)]
    [InlineData("a@b.com\n c@d.com ;", 2)]
    [InlineData("", 0)]
    [InlineData(null, 0)]
    public void RecipientsAreSplitOnCommasSemicolonsAndWhitespace(string? value, int expected)
        => Assert.Equal(expected, Recipients.Split(value).Count);
}
