# DiskMon

Watches free space on the volumes you name and emails when one drops below its threshold. It runs
as a Windows service or as a terminal application from the same executable, and it works out which
one it is on its own.

Everything an operator needs to change lives in one commented file, `settings/settings.xml`, next
to the binaries rather than among them.

```
> DiskMon check

VOLUME  PATH  TOTAL     FREE      FREE %  THRESHOLD     STATE
------  ----  --------  --------  ------  ------------  -----
System  C:\   475.9 GB  38.2 GB   8.0%    15% or 20 GB  LOW
Data    D:\   1.8 TB    412.6 GB  22.4%   50 GB         ok

2 volume(s): 1 below threshold, 0 unreadable.
```

- **.NET 10**, no runtime dependency beyond the framework.
- **Email through either** [Com.H](https://www.nuget.org/packages/Com.H) (SMTP) or
  [Com.H.GraphAPI](https://www.nuget.org/packages/Com.H.GraphAPI) (Microsoft 365), switched by one
  setting.
- **Templated emails** through
  [Com.H.Text.Template2](https://github.com/H7O/Com.H.Text.Template2), so the wording and layout
  of an alert are an HTML file, not a rebuild.
- **Quiet by design**: one email when a volume goes low, silence while it stays low, one more after
  a cooldown you set, and one when it recovers.

---

## Quick start

```powershell
git clone <this repo>
cd DiskMon

dotnet publish src/DiskMon/DiskMon.csproj -c Release -o C:\Apps\DiskMon
cd C:\Apps\DiskMon

notepad settings\settings.xml     # drives, thresholds, addresses, mail server
.\DiskMon.exe check               # confirm it reads what you meant
.\DiskMon.exe test-email          # confirm the mail route works

.\DiskMon.exe install             # elevated prompt
.\DiskMon.exe start
```

`check` exits 0 when every volume is healthy, 2 when one is low, and 1 when one could not be read,
so it also works as a monitoring probe.

---

## Commands

| Command | What it does |
|---|---|
| `DiskMon` or `DiskMon run` | Monitor continuously. Hosts itself as a Windows service or as a terminal application, whichever it was started as. |
| `DiskMon check` | Read every configured volume once, print the table above, exit. |
| `DiskMon test-email` | Send one alert email using the current readings. Every readable volume is reported as a breach, so the test works whether or not a disk happens to be full. |
| `DiskMon preview` | Render both emails to `preview-alert.html` and `preview-recovery.html` beside the executable, without sending. Open them in a browser while editing a template. |
| `DiskMon install` | Register the Windows service, set it to start automatically, give it restart-on-failure, and create its Event Log source. Needs an elevated prompt. |
| `DiskMon uninstall` | Stop and remove the service. Needs an elevated prompt. |
| `DiskMon start` / `stop` / `status` | Control the installed service. |
| `DiskMon help` | Print the above. |

The service verbs shell out to `sc.exe`. If you would rather script it yourself, `install` is
equivalent to:

```powershell
sc.exe create DiskMon binPath= "C:\Apps\DiskMon\DiskMon.exe" start= auto DisplayName= "DiskMon Disk Space Monitor"
sc.exe description DiskMon "Watches free space on the volumes listed in settings/settings.xml..."
sc.exe failure DiskMon reset= 86400 actions= restart/60000/restart/60000/restart/60000
```

`uninstall` is `sc.exe stop DiskMon` followed by `sc.exe delete DiskMon`.

### Which account should it run as

`LocalSystem` (the default) can read every local volume and write the log and state folders. If you
run it as a named account instead, give that account write access to the application folder, and
run `DiskMon install` once as an administrator first so the Event Log source exists — creating one
needs rights the service itself should not have.

---

## Settings

`settings/settings.xml`, beside the executable. Edits take effect within a few seconds; there is no
need to restart. If an edit leaves the file unreadable, DiskMon logs the problem and keeps running
on the last settings that loaded cleanly, so a typo does not take the monitoring down with it.

Two conventions worth knowing before you edit:

**A blank element means "not set", not "set to empty."** `<minFreeGb></minFreeGb>` falls back to the
default rather than becoming zero, and a blank `<password></password>` does not override one supplied
through the environment.

**`<disks>` is a list.** Add and remove `<disk>` elements freely, including down to one. There are
no indices to maintain.

### monitoring

| Setting | Meaning |
|---|---|
| `intervalSeconds` | Seconds between sweeps. The first sweep runs at startup. Minimum 5. |
| `includeAllFixedDrives` | Watch every ready fixed drive using `defaults`, on top of the drives listed under `disks`. |
| `defaults/minFreePercent` | Percentage limit for any drive that sets none of its own. |
| `defaults/minFreeGb` | Absolute limit, in gigabytes counted the way Explorer counts them. |
| `disks/disk/path` | `C`, `C:`, `C:\`, or a mount point such as `D:\sql\data`. |
| `disks/disk/label` | What the email calls it. Falls back to the volume's own label, then the path. |
| `disks/disk/enabled` | `false` excludes the drive, including from `includeAllFixedDrives`. |
| `disks/disk/minFreePercent` | Overrides the default for this drive only. |
| `disks/disk/minFreeGb` | Overrides the default for this drive only. |

Set either limit, or both. A drive alerts on whichever trips first, and an email names every limit
that tripped. Both exist because neither works alone across a mixed estate: ten per cent of a 20 TB
array is 2 TB, and a fixed 50 GB floor is meaningless on a 120 GB system disk.

A drive listed under `disks` always wins over the same drive discovered by `includeAllFixedDrives`,
whether you wrote it as `C`, `C:` or `C:\`.

### alerts

| Setting | Meaning |
|---|---|
| `reAlertAfterHours` | Hours of silence after an alert before the same drive, still low, emails again. `0` means one email per crossing and no repeats. |
| `sendRecoveryEmail` | Email once when a drive climbs back above its threshold. |
| `stateFile` | Where the alert history is kept. Changing this takes effect on the next restart. |

The history file is what stops a reboot or a patch window from re-alerting every drive that was
already reported. The service account needs write access to its folder.

### email

| Setting | Meaning |
|---|---|
| `enabled` | `false` logs findings without sending anything. Sweeps still run. |
| `provider` | `Smtp` or `Graph`. Changing it takes effect on the next sweep. |
| `from` | The sending mailbox. |
| `fromDisplayName` | Display name beside it. |
| `to`, `cc`, `bcc` | Addresses, separated by commas or semicolons. |
| `subjectAlert`, `subjectRecovery` | Subject lines. Templates, not plain strings. |
| `templates/alert`, `templates/recovery` | Paths to the body templates. |
| `smtp/host`, `smtp/port`, `smtp/enableSsl` | The mail server. 587 with SSL for STARTTLS, 25 for an unauthenticated relay. |
| `smtp/username`, `smtp/password` | Leave the password blank for an unauthenticated relay. A blank username falls back to `from`. |
| `graph/tenantId`, `graph/clientId`, `graph/clientSecret` | The Azure app registration. |
| `graph/saveToSentItems` | Whether Graph keeps a copy in the sending mailbox. |

### logging

| Setting | Meaning |
|---|---|
| `logLevel/default` | `Trace`, `Debug`, `Information`, `Warning`, `Error`, `Critical` or `None`. |
| `logLevel/<Category>` | Per-category override, e.g. `<Microsoft.Hosting.Lifetime>`. |
| `fileLog/enabled` | Write log files. |
| `fileLog/directory` | Where, relative to the application folder. |
| `fileLog/retainDays` | Delete anything older. Blank keeps everything. |

Log files go to `<directory>/YYYY/MM/DD/log.txt`, via
[Com.H.Extensions.Logging.File](https://www.nuget.org/packages/Com.H.Extensions.Logging.File). They
are the one sink both hosts share: a terminal run also writes to the console, and a service also
writes to the Application event log, but a service that fails before its Event Log source exists
leaves nothing behind except the file.

---

## Secrets, and overriding anything from the environment

Every setting can be overridden by an environment variable, which is where credentials belong. The
name is `DISKMON_` followed by the path to the value with `__` (two underscores) between the levels:

```powershell
DISKMON_email__smtp__password = "..."
DISKMON_email__graph__clientSecret = "..."
DISKMON_email__to = "oncall@example.com"
DISKMON_monitoring__disks__0__minFreePercent = "15"
```

The environment is layered on top of the file and wins, so a value set there does not need removing
from `settings.xml`. In Azure, an app setting of that name arrives as exactly this variable; in a
container, so does `-e DISKMON_email__smtp__password=...`.

There is no encryption. Either supply secrets through the environment, or restrict the file's ACL
to the service account.

---

## Email templates

Bodies are HTML files under `settings/templates/`, rendered by
[Com.H.Text.Template2](https://github.com/H7O/Com.H.Text.Template2). Edit them in place; there is
nothing to rebuild. `DiskMon preview` renders both to files you can open in a browser.

Each email is two files: the page (`alert.html`) and the row that repeats once per volume
(`alert-rows.html`), pulled in with `<h-embedded-template>`. The split is what keeps the table
header out of the repetition.

**Values available anywhere:**

| Marker | Example |
|---|---|
| `{{appName}}` | `DiskMon` |
| `{{machineName}}` | `SQL-PROD-02` |
| `{{summary}}` | `2 volumes below threshold` |
| `{{diskCount}}` | `2` |
| `{{generatedAt}}` | `2026-09-04 08:30:00 +03:00` |
| `{{generatedAtUtc}}` | `2026-09-04 05:30:00 UTC` |

**Values available on each row** (in `alert-rows.html` and `recovery-rows.html`):

| Marker | Example |
|---|---|
| `{{name}}` | `System` — the label from settings.xml, else the volume label, else the path |
| `{{path}}` | `C:\` |
| `{{free}}` | `38.2 GB` |
| `{{freePercent}}` | `8%` |
| `{{used}}` | `437.7 GB` |
| `{{usedPercent}}` | `92%` |
| `{{total}}` | `475.9 GB` |
| `{{usedBarPercent}}` | `92` — a whole number, for a bar width |
| `{{threshold}}` | `15% or 20 GB` |
| `{{reason}}` | `8% free, below the 15% minimum` |
| `{{state}}` | `New`, `Still low` or `Recovered` |
| `{{since}}` | `2026-09-04 05:15` |
| `{{lowFor}}` | `3 hours` |

The page's values stay in scope inside the row file, so a row can use `{{machineName}}` too.

Three things to know when editing a template:

**Wrap values in `{html{...}}`, not `{{...}}`.** A volume label is not something DiskMon chose, and
`{html{name}}` escapes it. `{{name}}` writes it verbatim, which a label containing `&` or `<` would
break.

**A marker in an HTML comment is still a marker.** The engine is a text templating engine and does
not know what a comment is, so `{{name}}` written in one is substituted and ends up in the email.
That is why the shipped templates document their values here rather than in themselves.

**Do not empty the `<h-embedded-data>` block.** `<![CDATA[disks]]>` names the set of rows DiskMon
supplies. An empty block has no data source at all, and the file would then render once, with no
volume in it.

---

## Sending through Microsoft 365 (Graph)

Set `<provider>Graph</provider>` and fill in `<graph>`. You need an Azure app registration with the
**`Mail.Send` application permission** — the application one, not the delegated one — and admin
consent granted.

> That permission lets the app send as any mailbox in the tenant. Scope it down with an
> [application access policy](https://learn.microsoft.com/en-us/graph/auth-limit-mailbox-access) if
> that is broader than you want.

`<from>` must be a real mailbox in the tenant. No token handling is needed here: MSAL, underneath
`Com.H.GraphAPI`, caches and refreshes on its own.

---

## How it decides to email

One sweep reads every configured volume, and each volume falls into one of four cases.

A volume that has just gone below its threshold **alerts**. A volume that is still below it and was
already reported stays **silent** until `reAlertAfterHours` has passed, then alerts again. A volume
that has climbed back above **recovers**, once. Everything else says nothing.

Volumes are batched: one email per sweep covering every volume in it, not one email each. A machine
whose disks fill together should produce a message you can act on, not four arriving at once.

Two details worth stating, because both are the difference between a monitor you trust and one you
mute:

**A volume that could not be read is unknown, not healthy.** An unplugged drive or a mount point
that has not come back is logged as a warning and left alone. It never sends a recovery notice.

**A failed send is not recorded as a send.** If the mail server is unreachable, the sweep records
that the disk is low but not that anyone was told, so the next sweep tries again rather than
starting a six-hour silence exactly when someone needed to hear about it.

---

## How it is put together

```
src/DiskMon/
  Program.cs               startup: service-or-terminal detection, configuration, DI
  settings/                settings.xml and the email templates - copied to the output
  Configuration/           the XML configuration provider, the settings model, the validator
  Monitoring/              reading volumes, comparing thresholds, alert history
  Notifications/           rendering the emails and sending them
  Hosting/                 the worker, the commands, the sc.exe wrapper
  Com.H/Template2/         a verbatim copy of Com.H.Text.Template2 (see its VENDORED.md)
tests/DiskMon.Tests/
docs/TODO.md               follow-ups worth doing, and the reasoning behind each
```

### Detecting how it was started

`Program.cs` asks Windows with `WindowsServiceHelpers.IsWindowsService()` rather than taking a
`--service` flag, so the same command line works from a terminal and from the service control
manager, and nobody can install the service with the flag missing. The answer decides two things:
whether the process lifetime is handed to the service control manager, and whether the log goes to
the console or to the Application event log.

Every relative path is resolved against `AppContext.BaseDirectory`, because a Windows service starts
with its working directory set to `C:\Windows\System32` — a service anchored to the working
directory would look for its templates somewhere in Windows.

### Why the XML configuration provider is DiskMon's own

`Microsoft.Extensions.Configuration.Xml` rejects two sibling elements with the same name unless each
carries a `Name` attribute, which then becomes part of the key. Expressing a list of disks through it
means hand-maintaining `Name="0"`, `Name="1"` indices in the file — and deleting the middle entry
leaves a gap that silently truncates the bound list. For a file a DevOps engineer edits by hand that
is the wrong trade, so `Configuration/XmlSettingsParser.cs` indexes repeated elements itself.

Which sections are lists is declared in code (`DiskMonSettings.CollectionPaths`), by path, rather
than guessed from the document. A single-entry list and a section with one child element look
identical, so any heuristic would break the moment someone deleted the second disk.

Everything else is stock: it is a `FileConfigurationProvider`, so reload-on-change, `IOptionsMonitor`
and the environment-variable layer all come for free.

---

## Building and testing

```powershell
dotnet build DiskMon.slnx
dotnet test  DiskMon.slnx
```

The tests run the shipped `settings.xml` and the shipped email templates as they are, not copies of
them, so a change to either has to keep the tests passing. They cover the configuration parser and
its error messages, the environment-variable layering, threshold evaluation, the alert state machine
including the failed-send and unreadable-volume cases, and the templates rendered end to end through
the real engine.

Publishing for deployment:

```powershell
dotnet publish src/DiskMon/DiskMon.csproj -c Release -o C:\Apps\DiskMon
```

`settings/` is copied with `PreserveNewest`, so republishing over a deployed folder leaves an edited
`settings.xml` alone — unless the shipped one has itself changed since, in which case the newer file
wins and your edits are lost. Back the file up before an upgrade, or keep the values you cannot
afford to lose in environment variables, where a publish cannot reach them.

---

## Not built yet

[docs/TODO.md](docs/TODO.md) carries the follow-ups worth doing, with the reasoning behind each
so they can be picked up cold. The largest is an append-only **alert history** in SQLite through
`Com.H.Data.Common`, with its statements in `settings/sql.xml` — which would answer "how often did
this volume go low last quarter, and for how long" without reading a year of log files.

That is deliberately separate from the state store this already has. Restart-safety works today
through `state/alert-state.json`; history is a different feature, and the one that would justify
the dependency.

## License

MIT. See [LICENSE](LICENSE).
