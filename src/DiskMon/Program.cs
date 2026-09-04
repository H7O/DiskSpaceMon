using Com.H.Extensions.Logging.File;
using DiskMon.Configuration;
using DiskMon.Hosting;
using DiskMon.Monitoring;
using DiskMon.Notifications;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.WindowsServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

var verb = CommandLine.Parse(args);

if (verb == Verb.Help)
{
    Console.WriteLine(CommandLine.Usage);
    return 0;
}

// The service-control verbs talk only to Windows. They need no settings, so they run before any
// of the host is built -- 'DiskMon uninstall' has to work even when settings.xml is the reason
// the service will not start.
if (CommandLine.IsServiceControl(verb))
{
    if (!OperatingSystem.IsWindows())
    {
        Console.Error.WriteLine("Service commands are only available on Windows.");
        return 1;
    }

    return verb switch
    {
        Verb.Install => ServiceControl.Install(ExecutablePath(), Console.Out),
        Verb.Uninstall => ServiceControl.Uninstall(Console.Out),
        Verb.Start => ServiceControl.Start(Console.Out),
        Verb.Stop => ServiceControl.Stop(Console.Out),
        _ => ServiceControl.Status(Console.Out)
    };
}

// Asking Windows whether it started us, rather than taking a --service flag, means the same
// command works from a terminal and from the service control manager, and nobody can install the
// service with the flag missing.
var isWindowsService = OperatingSystem.IsWindows() && WindowsServiceHelpers.IsWindowsService();

// A service starts with its working directory set to C:\Windows\System32, so every relative path
// -- settings, templates, logs, state -- is anchored to the executable's own folder instead.
var appDirectory = AppContext.BaseDirectory;

try
{

var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    ApplicationName = "DiskMon",
    ContentRootPath = appDirectory
});

// Out goes appsettings.json. Settings live in settings/settings.xml, where an operator can find
// them, and the environment is layered on top so a secret can be supplied by the platform --
// an Azure app setting, a container variable -- without ever being written to the file.
builder.Configuration.Sources.Clear();
builder.Configuration
    .SetBasePath(appDirectory)
    .AddXmlSettingsFile(
        "settings/settings.xml",
        DiskMonSettings.CollectionPaths,
        optional: false,
        reloadOnChange: true)
    .AddEnvironmentVariables("DISKMON_");

ConfigureLogging(builder, isWindowsService, appDirectory);

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(new AppPaths(appDirectory));
builder.Services.AddSingleton<IDiskProbe, DriveInfoDiskProbe>();
builder.Services.AddSingleton<DiskScanner>();
builder.Services.AddSingleton<AlertEmailComposer>();
builder.Services.AddSingleton<AlertNotifier>();

// Both senders are built, and the settings choose between them at send time, so switching
// email/provider is a file edit rather than a restart.
builder.Services.AddSingleton<IEmailSender, SmtpEmailSender>();
builder.Services.AddSingleton<IEmailSender, GraphEmailSender>();
builder.Services.AddSingleton<EmailSenderSelector>();

builder.Services.AddSingleton<IAlertStateStore>(provider => new JsonAlertStateStore(
    provider.GetRequiredService<AppPaths>().Resolve(
        provider.GetRequiredService<IOptionsMonitor<DiskMonSettings>>()
            .CurrentValue.Alerts.StateFile),
    provider.GetRequiredService<ILogger<JsonAlertStateStore>>()));

builder.Services.AddSingleton<IValidateOptions<DiskMonSettings>, DiskMonSettingsValidator>();
builder.Services
    .AddOptions<DiskMonSettings>()
    .Bind(builder.Configuration)
    .ValidateOnStart();

if (verb == Verb.Run)
{
    builder.Services.AddHostedService<DiskMonitorWorker>();

    if (OperatingSystem.IsWindows() && isWindowsService)
        WindowsHostingSetup.UseWindowsServiceLifetime(builder);
}

var host = builder.Build();

switch (verb)
{
    case Verb.Check:
        return Commands.Check(host.Services, Console.Out);

    case Verb.TestEmail:
        return await Commands.TestEmailAsync(host.Services, Console.Out);

    case Verb.Preview:
        return await Commands.PreviewAsync(host.Services, Console.Out);

    default:
        host.Services.GetRequiredService<ILoggerFactory>()
            .CreateLogger("DiskMon")
            .LogInformation(
                "Hosting as {Host}. Settings: {Settings}",
                isWindowsService ? "a Windows service" : "a terminal application",
                Path.Combine(appDirectory, "settings", "settings.xml"));

        await host.RunAsync();
        return 0;
}

}
catch (Exception ex) when (StartupProblem.IsSettingsProblem(ex))
{
    // A stack trace tells the reader nothing they can act on. The line number in their settings
    // file, or the name of the setting that was rejected, tells them everything.
    Console.Error.WriteLine("DiskMon cannot start.");
    Console.Error.WriteLine();
    Console.Error.WriteLine(StartupProblem.Describe(ex));
    Console.Error.WriteLine();
    Console.Error.WriteLine($"  File: {Path.Combine(appDirectory, "settings", "settings.xml")}");
    Console.Error.WriteLine("  Fix it, then run 'DiskMon check' to confirm before starting the service.");
    return 2;
}

static string ExecutablePath()
    => Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "DiskMon.exe");

static void ConfigureLogging(
    HostApplicationBuilder builder, bool isWindowsService, string appDirectory)
{
    builder.Logging.ClearProviders();
    builder.Logging.AddConfiguration(builder.Configuration.GetSection("logging"));

    // A service has no console to write to, and a console application has no business writing to
    // the Event Log, so the two hosts get different sinks.
    if (isWindowsService)
    {
        if (OperatingSystem.IsWindows()) WindowsHostingSetup.AddEventLog(builder);
    }
    else
    {
        builder.Logging.AddSimpleConsole(options =>
        {
            options.SingleLine = true;
            options.TimestampFormat = "HH:mm:ss ";
        });
    }

    if (!builder.Configuration.GetValue("logging:fileLog:enabled", true)) return;

    var directory = builder.Configuration.GetValue("logging:fileLog:directory", "logs")!;
    var retainDays = builder.Configuration.GetValue<int?>("logging:fileLog:retainDays");

    var resolved = Path.IsPathRooted(directory)
        ? directory
        : Path.Combine(appDirectory, directory);

    // The one sink both hosts share. Without it, a service that failed before the Event Log
    // source existed would leave nothing behind to read.
    builder.Logging.AddProvider(new SimpleFileLoggerProvider(resolved, retainDays));
}
