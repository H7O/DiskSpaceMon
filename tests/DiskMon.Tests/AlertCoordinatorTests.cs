using DiskMon.Configuration;
using DiskMon.Monitoring;

namespace DiskMon.Tests;

public class AlertCoordinatorTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 4, 8, 0, 0, TimeSpan.Zero);

    private static readonly AlertSettings SixHourCooldown =
        new() { ReAlertAfterHours = 6, SendRecoveryEmail = true };

    [Fact]
    public void TheFullLifeOfOneLowDisk()
    {
        // The behaviour the whole feature exists for, walked end to end: one email when it goes
        // low, silence while it stays low, one more after the cooldown, one when it recovers,
        // and nothing after that.
        var state = new AlertStateFile();
        var low = new[] { Status(freeGb: 5, totalGb: 100, minPercent: 10) };
        var healthy = new[] { Status(freeGb: 40, totalGb: 100, minPercent: 10) };

        var first = AlertCoordinator.Decide(low, state, SixHourCooldown, Start);
        Assert.Equal(AlertAction.Alert, Assert.Single(first).Action);
        AlertCoordinator.Commit(state, first[0], Start);

        var soon = AlertCoordinator.Decide(low, state, SixHourCooldown, Start.AddHours(5));
        Assert.Empty(soon);

        var later = AlertCoordinator.Decide(low, state, SixHourCooldown, Start.AddHours(6));
        Assert.Equal(AlertAction.ReAlert, Assert.Single(later).Action);
        AlertCoordinator.Commit(state, later[0], Start.AddHours(6));

        var recovered = AlertCoordinator.Decide(healthy, state, SixHourCooldown, Start.AddHours(7));
        Assert.Equal(AlertAction.Recovered, Assert.Single(recovered).Action);
        AlertCoordinator.Commit(state, recovered[0], Start.AddHours(7));

        Assert.Empty(state.Disks);
        Assert.Empty(AlertCoordinator.Decide(healthy, state, SixHourCooldown, Start.AddHours(8)));
    }

    [Fact]
    public void ARecoveringDiskCarriesHowLongItWasLow()
    {
        var state = new AlertStateFile();
        var low = new[] { Status(freeGb: 5, totalGb: 100, minPercent: 10) };
        var healthy = new[] { Status(freeGb: 40, totalGb: 100, minPercent: 10) };

        AlertCoordinator.Commit(state, AlertCoordinator.Decide(low, state, SixHourCooldown, Start)[0], Start);

        var recovered = AlertCoordinator.Decide(healthy, state, SixHourCooldown, Start.AddHours(9));

        Assert.Equal(Start, Assert.Single(recovered).FirstBreachUtc);
    }

    [Fact]
    public void ACooldownOfZeroMeansOneEmailPerCrossing()
    {
        var settings = new AlertSettings { ReAlertAfterHours = 0, SendRecoveryEmail = true };
        var state = new AlertStateFile();
        var low = new[] { Status(freeGb: 5, totalGb: 100, minPercent: 10) };

        AlertCoordinator.Commit(state, AlertCoordinator.Decide(low, state, settings, Start)[0], Start);

        Assert.Empty(AlertCoordinator.Decide(low, state, settings, Start.AddDays(30)));
    }

    [Fact]
    public void RecoveryCanBeTurnedOff()
    {
        var settings = new AlertSettings { ReAlertAfterHours = 6, SendRecoveryEmail = false };
        var state = new AlertStateFile();
        var low = new[] { Status(freeGb: 5, totalGb: 100, minPercent: 10) };
        var healthy = new[] { Status(freeGb: 40, totalGb: 100, minPercent: 10) };

        AlertCoordinator.Commit(state, AlertCoordinator.Decide(low, state, settings, Start)[0], Start);

        Assert.Empty(AlertCoordinator.Decide(healthy, state, settings, Start.AddHours(7)));
    }

    [Fact]
    public void AFailedEmailIsRetriedNextSweepRatherThanWaitingOutTheCooldown()
    {
        // If a send failure recorded the alert anyway, a mail outage would buy six hours of
        // silence starting exactly when someone needed to hear about the disk.
        var state = new AlertStateFile();
        var low = new[] { Status(freeGb: 5, totalGb: 100, minPercent: 10) };

        var first = AlertCoordinator.Decide(low, state, SixHourCooldown, Start);
        AlertCoordinator.CommitUnsent(state, first[0], Start);

        var next = AlertCoordinator.Decide(low, state, SixHourCooldown, Start.AddMinutes(5));

        Assert.Equal(AlertAction.ReAlert, Assert.Single(next).Action);
    }

    [Fact]
    public void AVolumeThatCannotBeReadIsNotTreatedAsRecovered()
    {
        // An unplugged drive is unknown, not healthy. Sending "recovered" for it would be a
        // comforting lie.
        var state = new AlertStateFile();
        var low = new[] { Status(freeGb: 5, totalGb: 100, minPercent: 10) };
        AlertCoordinator.Commit(state, AlertCoordinator.Decide(low, state, SixHourCooldown, Start)[0], Start);

        var unreadable = new[]
        {
            new DiskStatus(
                new DiskTarget(@"C:\", null, 10, null, false),
                Reading: null,
                Error: "gone",
                Reasons: [])
        };

        Assert.Empty(AlertCoordinator.Decide(unreadable, state, SixHourCooldown, Start.AddHours(9)));
        Assert.True(state.Disks[@"C:\"].IsBreaching);
    }

    [Fact]
    public void HistoryForAVolumeNoLongerWatchedIsDropped()
    {
        var state = new AlertStateFile();
        state.For(@"C:\").IsBreaching = true;
        state.For(@"D:\").IsBreaching = true;

        AlertCoordinator.Prune(state, [new DiskTarget(@"C:\", null, 10, null, false)]);

        Assert.Equal([@"C:\"], state.Disks.Keys);
    }

    private static DiskStatus Status(double freeGb, double totalGb, double minPercent)
    {
        var target = new DiskTarget(@"C:\", "System", minPercent, null, false);
        var reading = new DiskReading(@"C:\", null, Size.Gb(totalGb), Size.Gb(freeGb));

        return new DiskStatus(target, reading, Error: null, DiskScanner.Evaluate(target, reading));
    }
}
