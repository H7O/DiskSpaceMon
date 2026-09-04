using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security.Principal;

namespace DiskMon.Hosting;

/// <summary>
/// Registers and controls the Windows service, so the same executable that runs the monitoring
/// also installs it.
/// </summary>
/// <remarks>
/// Everything here shells out to <c>sc.exe</c>, which is the tool a Windows administrator would
/// reach for anyway. Nothing is hidden: the README lists the equivalent commands for anyone who
/// would rather run them directly or put them in a deployment script.
/// </remarks>
[SupportedOSPlatform("windows")]
public static class ServiceControl
{
    /// <summary>
    /// Registers the service to start with Windows and to restart itself if it dies.
    /// </summary>
    /// <param name="executablePath">Full path to DiskMon.exe.</param>
    /// <param name="output">Where progress and errors are written.</param>
    /// <returns>Zero on success, non-zero otherwise.</returns>
    public static int Install(string executablePath, TextWriter output)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        ArgumentNullException.ThrowIfNull(output);

        if (!RequireElevation(output)) return 1;

        var created = Run(output, "create", ServiceMetadata.Name,
            "binPath=", executablePath,
            "start=", "auto",
            "DisplayName=", ServiceMetadata.DisplayName);

        if (created != 0)
        {
            output.WriteLine(
                $"Could not create the service. If '{ServiceMetadata.Name}' already exists, run "
                + "'DiskMon uninstall' first.");
            return created;
        }

        Run(output, "description", ServiceMetadata.Name, ServiceMetadata.Description);

        // Three restarts a minute apart, with the count resetting after a quiet day. A disk
        // monitor that stays down after one bad night is worse than useless.
        Run(output, "failure", ServiceMetadata.Name,
            "reset=", "86400",
            "actions=", "restart/60000/restart/60000/restart/60000");

        EnsureEventLogSource(output);

        output.WriteLine($"Installed '{ServiceMetadata.Name}'. Start it with: DiskMon start");
        return 0;
    }

    /// <summary>
    /// Stops the service if it is running and removes its registration.
    /// </summary>
    /// <param name="output">Where progress and errors are written.</param>
    /// <returns>Zero on success, non-zero otherwise.</returns>
    public static int Uninstall(TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(output);
        if (!RequireElevation(output)) return 1;

        // A stop failure is expected when the service is already stopped, so its exit code is
        // deliberately not checked.
        Run(output, "stop", ServiceMetadata.Name);

        var deleted = Run(output, "delete", ServiceMetadata.Name);
        if (deleted == 0) output.WriteLine($"Removed '{ServiceMetadata.Name}'.");
        return deleted;
    }

    /// <summary>Starts the installed service.</summary>
    /// <param name="output">Where progress and errors are written.</param>
    /// <returns>Zero on success, non-zero otherwise.</returns>
    public static int Start(TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(output);
        return RequireElevation(output) ? Run(output, "start", ServiceMetadata.Name) : 1;
    }

    /// <summary>Stops the running service.</summary>
    /// <param name="output">Where progress and errors are written.</param>
    /// <returns>Zero on success, non-zero otherwise.</returns>
    public static int Stop(TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(output);
        return RequireElevation(output) ? Run(output, "stop", ServiceMetadata.Name) : 1;
    }

    /// <summary>Prints what Windows currently thinks of the service.</summary>
    /// <param name="output">Where the report is written.</param>
    /// <returns>Zero on success, non-zero otherwise.</returns>
    public static int Status(TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(output);
        return Run(output, "query", ServiceMetadata.Name);
    }

    private static bool RequireElevation(TextWriter output)
    {
        using var identity = WindowsIdentity.GetCurrent();
        if (new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator)) return true;

        output.WriteLine(
            "This needs an elevated prompt. Re-run it from a terminal started with "
            + "'Run as administrator'.");

        return false;
    }

    private static void EnsureEventLogSource(TextWriter output)
    {
        try
        {
            // Creating the source needs administrator rights, which install already has. Doing it
            // here means the service itself never has to, so it can run as a low-privilege
            // account and still write to the Application log.
            if (!EventLog.SourceExists(ServiceMetadata.Name))
                EventLog.CreateEventSource(new EventSourceCreationData(ServiceMetadata.Name, "Application"));
        }
        catch (Exception ex) when (ex is System.Security.SecurityException
                                      or InvalidOperationException or ArgumentException)
        {
            output.WriteLine(
                $"Warning: could not register the '{ServiceMetadata.Name}' Event Log source "
                + $"({ex.Message}). The service will still run and still write its log files.");
        }
    }

    private static int Run(TextWriter output, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("sc.exe")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        // Passed as separate arguments on purpose: sc.exe expects 'binPath=' and its value as two
        // tokens, and building the command line by hand is how quoting bugs get in.
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo);
        if (process is null)
        {
            output.WriteLine("Could not start sc.exe.");
            return 1;
        }

        var standardOutput = process.StandardOutput.ReadToEnd();
        var standardError = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (!string.IsNullOrWhiteSpace(standardOutput)) output.WriteLine(standardOutput.Trim());
        if (!string.IsNullOrWhiteSpace(standardError)) output.WriteLine(standardError.Trim());

        return process.ExitCode;
    }
}
