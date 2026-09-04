namespace DiskMon.Configuration;

/// <summary>Everything <c>settings/settings.xml</c> configures.</summary>
public sealed class DiskMonSettings
{
    /// <summary>
    /// Sections whose child elements form a list, passed to the XML configuration source.
    /// </summary>
    public static IReadOnlyCollection<string> CollectionPaths { get; } = ["monitoring:disks"];

    /// <summary>What to watch, and how often.</summary>
    public MonitoringSettings Monitoring { get; set; } = new();

    /// <summary>How often a disk that stays low is allowed to email again.</summary>
    public AlertSettings Alerts { get; set; } = new();

    /// <summary>Who is emailed, through which provider, using which templates.</summary>
    public EmailSettings Email { get; set; } = new();
}

/// <summary>What to watch, and how often.</summary>
public sealed class MonitoringSettings
{
    /// <summary>Seconds between sweeps. The first sweep runs at startup.</summary>
    public int IntervalSeconds { get; set; } = 300;

    /// <summary>
    /// Whether every ready fixed drive is watched using <see cref="Defaults"/>, on top of the
    /// drives listed under <see cref="Disks"/>.
    /// </summary>
    public bool IncludeAllFixedDrives { get; set; }

    /// <summary>Thresholds for any drive that does not set its own.</summary>
    public DiskThresholds Defaults { get; set; } = new();

    /// <summary>Drives named explicitly. An entry here overrides the discovered one.</summary>
    public List<DiskSettings> Disks { get; set; } = [];
}

/// <summary>
/// How little free space is too little. Either limit alone is enough; setting both alerts on
/// whichever trips first, which is what a mixed estate of small and very large volumes needs.
/// Ten per cent of a 20 TB array is 2 TB, and ten per cent of a 120 GB system disk is 12 GB.
/// </summary>
public class DiskThresholds
{
    /// <summary>Alert below this percentage of free space. Null disables the percentage test.</summary>
    public double? MinFreePercent { get; set; }

    /// <summary>
    /// Alert below this many free gigabytes, counted the way Windows Explorer counts them
    /// (1 GB = 1024 * 1024 * 1024 bytes). Null disables the absolute test.
    /// </summary>
    public double? MinFreeGb { get; set; }

    /// <summary>Whether either limit is set.</summary>
    public bool IsConfigured => MinFreePercent is > 0 || MinFreeGb is > 0;
}

/// <summary>One drive to watch.</summary>
public sealed class DiskSettings : DiskThresholds
{
    /// <summary>
    /// The drive, as a root path (<c>C:\</c>), a drive letter (<c>C:</c> or <c>C</c>), or a
    /// mount point.
    /// </summary>
    public string Path { get; set; } = "";

    /// <summary>A name for the alert email. Falls back to the volume label, then the path.</summary>
    public string? Label { get; set; }

    /// <summary>
    /// Whether this entry is watched. Set it to false to exclude a drive that
    /// <see cref="MonitoringSettings.IncludeAllFixedDrives"/> would otherwise pick up.
    /// </summary>
    public bool Enabled { get; set; } = true;
}

/// <summary>How often a disk that stays low is allowed to email again.</summary>
public sealed class AlertSettings
{
    /// <summary>
    /// Hours of silence after an alert before the same disk, still below its threshold, emails
    /// again. Zero or less means never repeat: one email per crossing.
    /// </summary>
    public double ReAlertAfterHours { get; set; } = 6;

    /// <summary>Whether a disk climbing back above its threshold sends a recovery email.</summary>
    public bool SendRecoveryEmail { get; set; } = true;

    /// <summary>
    /// Where the per-disk alert history is kept, relative to the application folder. It lives
    /// outside <c>settings/</c> because the service writes it; without it, every restart would
    /// re-alert on a disk that was already reported.
    /// </summary>
    public string StateFile { get; set; } = "state/alert-state.json";
}

/// <summary>Which library sends the mail.</summary>
public enum EmailProvider
{
    /// <summary>SMTP, through <c>Com.H.Net.Mail</c>.</summary>
    Smtp,

    /// <summary>Microsoft Graph <c>sendMail</c>, through <c>Com.H.GraphAPI</c>.</summary>
    Graph
}

/// <summary>Who is emailed, through which provider, using which templates.</summary>
public sealed class EmailSettings
{
    /// <summary>Whether alerts are emailed at all. Sweeps still run and log when false.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Which sender to use.</summary>
    public EmailProvider Provider { get; set; } = EmailProvider.Smtp;

    /// <summary>The sending mailbox.</summary>
    public string From { get; set; } = "";

    /// <summary>Display name shown beside <see cref="From"/>.</summary>
    public string? FromDisplayName { get; set; }

    /// <summary>Recipients, separated by comma, semicolon or newline.</summary>
    public string To { get; set; } = "";

    /// <summary>Copied recipients, separated the same way.</summary>
    public string? Cc { get; set; }

    /// <summary>Blind-copied recipients, separated the same way.</summary>
    public string? Bcc { get; set; }

    /// <summary>Subject of a low-space alert. Rendered through the template engine.</summary>
    public string SubjectAlert { get; set; } = "[DiskMon] Low disk space on {{machineName}}";

    /// <summary>Subject of a recovery notice. Rendered through the template engine.</summary>
    public string SubjectRecovery { get; set; } = "[DiskMon] Disk space recovered on {{machineName}}";

    /// <summary>Where the two body templates live.</summary>
    public EmailTemplateSettings Templates { get; set; } = new();

    /// <summary>Used when <see cref="Provider"/> is <see cref="EmailProvider.Smtp"/>.</summary>
    public SmtpSettings Smtp { get; set; } = new();

    /// <summary>Used when <see cref="Provider"/> is <see cref="EmailProvider.Graph"/>.</summary>
    public GraphSettings Graph { get; set; } = new();
}

/// <summary>Template file paths, relative to the application folder.</summary>
public sealed class EmailTemplateSettings
{
    /// <summary>Body of a low-space alert.</summary>
    public string Alert { get; set; } = "settings/templates/alert.html";

    /// <summary>Body of a recovery notice.</summary>
    public string Recovery { get; set; } = "settings/templates/recovery.html";
}

/// <summary>SMTP connection details.</summary>
public sealed class SmtpSettings
{
    /// <summary>Server host name.</summary>
    public string Host { get; set; } = "";

    /// <summary>Server port. 587 for STARTTLS, 25 for an unauthenticated relay.</summary>
    public int Port { get; set; } = 587;

    /// <summary>Whether the connection is upgraded to TLS.</summary>
    public bool EnableSsl { get; set; } = true;

    /// <summary>Login name. Blank means the sending address is used.</summary>
    public string? Username { get; set; }

    /// <summary>
    /// Password. Blank means no authentication. Supply it through the environment rather than
    /// the file where you can, as described in the README.
    /// </summary>
    public string? Password { get; set; }
}

/// <summary>Azure app registration used for Graph <c>sendMail</c>.</summary>
public sealed class GraphSettings
{
    /// <summary>Directory (tenant) ID.</summary>
    public string TenantId { get; set; } = "";

    /// <summary>Application (client) ID.</summary>
    public string ClientId { get; set; } = "";

    /// <summary>
    /// Client secret. Supply it through the environment rather than the file where you can, as
    /// described in the README.
    /// </summary>
    public string ClientSecret { get; set; } = "";

    /// <summary>Whether Graph keeps a copy in the sending mailbox's Sent Items.</summary>
    public bool SaveToSentItems { get; set; }
}
