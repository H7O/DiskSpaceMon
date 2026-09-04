using DiskSpaceMon.Configuration;
using DiskSpaceMon.Monitoring;
using Microsoft.Extensions.Logging.Abstractions;

namespace DiskSpaceMon.Tests;

public class DiskScannerTests
{
    private static DiskScanner Scanner(IDiskProbe probe)
        => new(probe, NullLogger<DiskScanner>.Instance);

    [Fact]
    public void APercentageThresholdTripsBelowItAndNotOnIt()
    {
        var target = new DiskTarget(@"C:\", null, MinFreePercent: 10, MinFreeGb: null, false);

        Assert.Empty(DiskScanner.Evaluate(target, Reading(free: 10, total: 100)));
        Assert.Single(DiskScanner.Evaluate(target, Reading(free: 9.9, total: 100)));
    }

    [Fact]
    public void AnAbsoluteThresholdCatchesTheCaseAPercentageMisses()
    {
        // 1.5 TB free is 7.3% of a 20 TB array, so the percentage limit is happy. On a volume
        // that size, 1.5 TB is still not much runway, which is what the absolute limit is for.
        var target = new DiskTarget(@"E:\", null, MinFreePercent: 5, MinFreeGb: 2000, false);
        var reading = Reading(free: 1500, total: 20480);

        var reasons = DiskScanner.Evaluate(target, reading);

        Assert.Single(reasons);
        Assert.Contains("2000 GB minimum", reasons[0]);
    }

    [Fact]
    public void BothLimitsAreReportedWhenBothTrip()
    {
        var target = new DiskTarget(@"C:\", null, MinFreePercent: 20, MinFreeGb: 50, false);

        var reasons = DiskScanner.Evaluate(target, Reading(free: 10, total: 100));

        Assert.Equal(2, reasons.Count);
    }

    [Fact]
    public void NoThresholdNeverTrips()
    {
        var target = new DiskTarget(@"C:\", null, MinFreePercent: null, MinFreeGb: null, false);

        Assert.Empty(DiskScanner.Evaluate(target, Reading(free: 0.001, total: 100)));
    }

    [Fact]
    public void ADiskWithoutItsOwnThresholdInheritsTheDefaults()
    {
        var monitoring = new MonitoringSettings
        {
            Defaults = { MinFreePercent = 12, MinFreeGb = 30 },
            Disks = [new DiskSettings { Path = @"C:\" }]
        };

        var target = Scanner(new FakeDiskProbe()).ResolveTargets(monitoring).Single();

        Assert.Equal(12, target.MinFreePercent);
        Assert.Equal(30, target.MinFreeGb);
    }

    [Fact]
    public void ADiskOverridesOnlyTheLimitItSets()
    {
        var monitoring = new MonitoringSettings
        {
            Defaults = { MinFreePercent = 12, MinFreeGb = 30 },
            Disks = [new DiskSettings { Path = @"C:\", MinFreePercent = 25 }]
        };

        var target = Scanner(new FakeDiskProbe()).ResolveTargets(monitoring).Single();

        Assert.Equal(25, target.MinFreePercent);
        Assert.Equal(30, target.MinFreeGb);
    }

    [Fact]
    public void AnExplicitEntrySuppressesTheDiscoveredOneEvenWrittenDifferently()
    {
        // 'C:' in the file and 'C:\' from enumerating drives are the same volume. Reporting it
        // twice would mean two emails about one disk.
        var probe = new FakeDiskProbe();
        probe.FixedDrives.AddRange([@"C:\", @"D:\"]);

        var monitoring = new MonitoringSettings
        {
            IncludeAllFixedDrives = true,
            Defaults = { MinFreePercent = 10 },
            Disks = [new DiskSettings { Path = "C:", Label = "System", MinFreePercent = 25 }]
        };

        var targets = Scanner(probe).ResolveTargets(monitoring);

        Assert.Equal(2, targets.Count);
        Assert.Equal(@"C:\", targets[0].Path);
        Assert.Equal(25, targets[0].MinFreePercent);
        Assert.False(targets[0].Discovered);
        Assert.Equal(@"D:\", targets[1].Path);
        Assert.True(targets[1].Discovered);
    }

    [Fact]
    public void ADisabledEntryExcludesThatDriveFromDiscovery()
    {
        var probe = new FakeDiskProbe();
        probe.FixedDrives.AddRange([@"C:\", @"D:\"]);

        var monitoring = new MonitoringSettings
        {
            IncludeAllFixedDrives = true,
            Defaults = { MinFreePercent = 10 },
            Disks = [new DiskSettings { Path = @"D:\", Enabled = false }]
        };

        var targets = Scanner(probe).ResolveTargets(monitoring);

        Assert.Equal(@"C:\", Assert.Single(targets).Path);
    }

    [Fact]
    public void AVolumeThatCannotBeReadIsReportedWithoutStoppingTheSweep()
    {
        var probe = new FakeDiskProbe().With(@"C:\", Size.Gb(500), Size.Gb(20));

        var monitoring = new MonitoringSettings
        {
            Defaults = { MinFreePercent = 10 },
            Disks =
            [
                new DiskSettings { Path = @"C:\" },
                new DiskSettings { Path = @"Z:\" }
            ]
        };

        var statuses = Scanner(probe).Scan(monitoring);

        Assert.Equal(2, statuses.Count);
        Assert.True(statuses[0].IsReadable);
        Assert.True(statuses[0].IsBreaching);
        Assert.False(statuses[1].IsReadable);
        Assert.False(statuses[1].IsBreaching);
        Assert.NotNull(statuses[1].Error);
    }

    [Fact]
    public void TheDisplayNameFallsBackFromSettingsLabelToVolumeLabelToPath()
    {
        var probe = new FakeDiskProbe()
            .With(@"C:\", Size.Gb(500), Size.Gb(100), label: "Windows")
            .With(@"D:\", Size.Gb(500), Size.Gb(100));

        var monitoring = new MonitoringSettings
        {
            Defaults = { MinFreePercent = 1 },
            Disks =
            [
                new DiskSettings { Path = @"C:\", Label = "System" },
                new DiskSettings { Path = @"D:\" }
            ]
        };

        var statuses = Scanner(probe).Scan(monitoring);

        Assert.Equal("System", statuses[0].DisplayName);
        Assert.Equal(@"D:\", statuses[1].DisplayName);
    }

    [Fact]
    public void AVolumeLabelIsUsedWhenTheSettingsFileNamesNone()
    {
        var probe = new FakeDiskProbe().With(@"C:\", Size.Gb(500), Size.Gb(100), label: "Windows");

        var monitoring = new MonitoringSettings
        {
            Defaults = { MinFreePercent = 1 },
            Disks = [new DiskSettings { Path = @"C:\" }]
        };

        Assert.Equal("Windows", Scanner(probe).Scan(monitoring).Single().DisplayName);
    }

    private static DiskReading Reading(double free, double total)
        => new(@"C:\", null, Size.Gb(total), Size.Gb(free));
}
