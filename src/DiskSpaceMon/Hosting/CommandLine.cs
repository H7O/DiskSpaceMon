namespace DiskSpaceMon.Hosting;

/// <summary>What the executable was asked to do.</summary>
public enum Verb
{
    /// <summary>Monitor, either in the terminal or as a service. The default.</summary>
    Run,

    /// <summary>Read the volumes once, print what was found, and exit.</summary>
    Check,

    /// <summary>Send one alert email using the current readings, and exit.</summary>
    TestEmail,

    /// <summary>Render both emails to HTML files without sending anything, and exit.</summary>
    Preview,

    /// <summary>Register the Windows service.</summary>
    Install,

    /// <summary>Remove the Windows service.</summary>
    Uninstall,

    /// <summary>Start the installed service.</summary>
    Start,

    /// <summary>Stop the running service.</summary>
    Stop,

    /// <summary>Report the service's state.</summary>
    Status,

    /// <summary>Print usage.</summary>
    Help
}

/// <summary>Reads the verb off the command line.</summary>
public static class CommandLine
{
    /// <summary>
    /// Works out which verb was asked for.
    /// </summary>
    /// <param name="args">The process arguments.</param>
    /// <returns>
    /// The verb. Anything unrecognised is <see cref="Verb.Run"/>, because the host also reads
    /// the command line for configuration overrides and those must not look like a bad verb.
    /// </returns>
    public static Verb Parse(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        var first = args.FirstOrDefault()?.Trim();
        if (string.IsNullOrEmpty(first)) return Verb.Run;

        return first.ToLowerInvariant() switch
        {
            "run" => Verb.Run,
            "check" => Verb.Check,
            "test-email" or "testemail" => Verb.TestEmail,
            "preview" or "preview-email" => Verb.Preview,
            "install" => Verb.Install,
            "uninstall" or "remove" => Verb.Uninstall,
            "start" => Verb.Start,
            "stop" => Verb.Stop,
            "status" => Verb.Status,
            "help" or "--help" or "-h" or "-?" or "/?" => Verb.Help,
            _ => Verb.Run
        };
    }

    /// <summary>Whether a verb only talks to the service control manager.</summary>
    /// <param name="verb">The verb to test.</param>
    /// <returns>True when the verb needs no settings, no host and no logging.</returns>
    public static bool IsServiceControl(Verb verb)
        => verb is Verb.Install or Verb.Uninstall or Verb.Start or Verb.Stop or Verb.Status;

    /// <summary>Usage text.</summary>
    public const string Usage = """
        DiskSpaceMon - watches free disk space and emails when it runs low.

        Usage: DiskSpaceMon [command]

          (none), run    Monitor continuously. Detects whether it was started by the Windows
                         service control manager and hosts itself accordingly.
          check          Read every configured volume once, print the result, and exit.
          test-email     Send one alert email using the current readings, and exit.
          preview        Render both emails to HTML files beside the executable, without
                         sending anything. Open them in a browser while editing a template.

          install        Register the Windows service (needs an elevated prompt).
          uninstall      Stop and remove the Windows service (needs an elevated prompt).
          start          Start the installed service.
          stop           Stop the running service.
          status         Report what Windows thinks of the service.
          help           Print this.

        Settings live in settings/settings.xml beside the executable. Any value can be
        overridden with an environment variable, for example:

          DISKSPACEMON_email__smtp__password=...
          DISKSPACEMON_monitoring__disks__0__minFreePercent=15
        """;
}
