namespace DiskMon.Hosting;

/// <summary>
/// The names Windows knows the service by.
/// </summary>
/// <remarks>
/// Kept apart from <see cref="ServiceControl"/>, which is Windows-only, because these strings are
/// not: the Event Log source name and the service name are read on every platform when the
/// process starts, and only acted on where they mean something.
/// </remarks>
public static class ServiceMetadata
{
    /// <summary>The service's key name, as used by <c>sc.exe</c> and <c>net start</c>.</summary>
    public const string Name = "DiskMon";

    /// <summary>The name shown in the Services console.</summary>
    public const string DisplayName = "DiskMon Disk Space Monitor";

    /// <summary>The description shown in the Services console.</summary>
    public const string Description =
        "Watches free space on the volumes listed in settings/settings.xml and emails when one "
        + "drops below its threshold.";
}
