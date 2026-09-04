using System.Runtime.Versioning;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DiskSpaceMon.Hosting;

/// <summary>
/// The parts of startup that only mean anything on Windows.
/// </summary>
/// <remarks>
/// Gathered into one Windows-only class so the rest of <c>Program.cs</c> stays platform-neutral
/// and DiskSpaceMon still builds and runs as a terminal application on Linux and macOS.
/// </remarks>
[SupportedOSPlatform("windows")]
public static class WindowsHostingSetup
{
    /// <summary>
    /// Hands the process lifetime to the service control manager, so Windows can stop and
    /// restart the service properly instead of killing it.
    /// </summary>
    /// <param name="builder">The host builder being configured.</param>
    public static void UseWindowsServiceLifetime(HostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddWindowsService(options => options.ServiceName = ServiceMetadata.Name);
    }

    /// <summary>
    /// Sends the log to the Application event log, which is where an administrator looks first
    /// when a service will not start.
    /// </summary>
    /// <param name="builder">The host builder being configured.</param>
    public static void AddEventLog(HostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Logging.AddEventLog(settings =>
        {
            // The source is registered by 'DiskSpaceMon install', which runs elevated. Registering it
            // here instead would mean the service account needed rights it should not have.
            settings.SourceName = ServiceMetadata.Name;
            settings.LogName = "Application";
        });
    }
}
