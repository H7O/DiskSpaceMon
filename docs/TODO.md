# TODO

Things worth building, and why they are not built yet. Each entry carries enough reasoning to be
picked up cold — the point of writing them down is that the next person does not have to
re-derive the decision.

---

## 1. Alert history, in SQLite via Com.H.Data.Common

**What.** An append-only record of every threshold crossing, so questions like *"how often did
`D:\` go low last quarter, and for how long each time?"* can be answered by a query rather than by
reading a year of log files.

**Why it is not the current state store.** DiskMon already survives a restart:
`JsonAlertStateStore` writes `state/alert-state.json` through a temp file and an atomic move, and
the worker loads it at startup, so a disk that was already reported is not re-alerted. Swapping
that for SQLite would add `Microsoft.Data.Sqlite`, the SQLitePCLRaw native binaries in every
publish folder, and a `DbProviderFactories.RegisterFactory` call at startup — to hold one row per
watched disk, typically three of them, and to lose a state file an operator can open in Notepad
and reset by deleting a few lines.

So SQLite only earns its place once the table holds something JSON genuinely cannot: history.
Current state is one row per disk, overwritten each sweep. History is a row per *event*, kept.
Those are different features, and the second is the one worth the dependency.

**Shape.** Add it *alongside* the JSON state store, not in place of it. `AlertCoordinator` already
produces exactly the events to record — each `AlertDecision` carries the volume, the action
(`Alert`, `ReAlert`, `Recovered`), the reasons and `FirstBreachUtc`. Write one row per decision
the worker commits, in `DiskMonitorWorker.ReportAsync`, next to the existing `Commit` call.

```sql
create table if not exists alert_history (
  id                integer primary key autoincrement,
  path              text    not null collate nocase,
  display_name      text    null,
  action            text    not null,       -- Alert | ReAlert | Recovered
  occurred_utc      text    not null,
  first_breach_utc  text    null,
  free_bytes        integer not null,
  total_bytes       integer not null,
  free_percent      real    not null,
  reasons           text    null,
  emailed           integer not null        -- whether it actually went out
);
```

Recording `emailed` matters: a row saying the disk was low but nobody was told is the row an
incident review needs.

**Mechanism.** `Com.H.Data.Common` is already referenced (the vendored Template2 sources need it),
so this needs no new package beyond the ADO.NET provider itself. Put every statement in
`settings/sql.xml` so a query can be tuned without a rebuild, loaded through the existing
`AddXmlSettingsFile` provider — it gives reload-on-change and environment overrides for free:

```csharp
builder.Configuration.AddXmlSettingsFile(
    "settings/sql.xml", collections: [], optional: true, reloadOnChange: true);
```

`<sql><alertHistory><insert>…</insert></alertHistory></sql>` flattens to `alertHistory:insert`,
which does not collide with anything `settings.xml` produces. Bind it with
`services.Configure<AlertHistorySql>(config.GetSection("alertHistory"))`.

Execute with `connection.ExecuteCommandAsync(sql, new { path, action, … })`. The `{{marker}}`
syntax in the SQL becomes real `DbParameter`s, so values from a volume label are bound, never
concatenated.

**Keep it provider-agnostic.** Take the provider name from settings rather than hard-coding
SQLite, and create the connection with `connectionString.CreateDbConnection(providerName)`. An
operator who already runs SQL Server then points DiskMon at it by changing three settings and the
statements in `sql.xml`, with no code change. That is the whole reason to route through
`Com.H.Data.Common` rather than talking to SQLite directly.

**Two traps.**

Register the factory at startup — `CreateDbConnection` resolves through `DbProviderFactories`, and
without `DbProviderFactories.RegisterFactory("Microsoft.Data.Sqlite", SqliteFactory.Instance)` it
throws a message about a missing provider.

Resolve a relative SQLite `Data Source` against `AppContext.BaseDirectory` before opening the
connection. A Windows service starts in `C:\Windows\System32`, so `Data Source=state/diskmon.db`
would quietly create the database there. Only do this for SQLite: for SQL Server, `Data Source`
is a server name, and rewriting it as a path would be wrong.

**Failure policy.** Writing history must never take the monitoring down. Log and carry on, the way
`JsonAlertStateStore.SaveAsync` already does — an unwritable history file is a nuisance, an
unmonitored disk is an incident.

---

## 2. Stop a publish from overwriting an edited settings.xml

`settings/` is copied with `PreserveNewest`, so republishing usually leaves a deployed
`settings.xml` alone — but not if the shipped one has itself changed since, in which case the
newer file wins and the operator's thresholds and addresses are gone. The README says so, which is
honest but not a fix.

The fix is to ship `settings/settings.default.xml` with `CopyToOutputDirectory=Always`, and have
startup create `settings.xml` from it only when it is missing. The operator's file is then never
touched by a publish, and the refreshed default doubles as the reference to diff against after an
upgrade to see what settings are new.

Not done yet because it trades one small surprise for another: a first-time user would not find
`settings.xml` until they ran DiskMon once, and being able to find it immediately was the point of
the `settings/` folder.

---

## 3. Validate recipient addresses at startup

`DiskMonSettingsValidator` checks that *at least one* recipient exists, but not that any of them
parses. Both senders drop malformed addresses silently — `Com.H.Net.Mail.Message` filters through
`IsEmail()`, and `Com.H.GraphAPI` leaves them out of the payload. So a typo in `<to>` means alerts
go nowhere and nothing says so.

`Com.H.Net.Mail.MailExtensions.IsEmail()` is already available. Reject an unparseable address in
the validator, naming it, so the mistake is caught by `DiskMon check` rather than during an
incident.

---

## 4. Test the worker loop itself

Every piece the worker orchestrates is tested — threshold evaluation, the alert state machine, the
templates, the configuration parser — but `DiskMonitorWorker` is not. The behaviour worth pinning
is the part that is easy to break and impossible to notice: that a malformed `settings.xml` saved
mid-run falls back to the last good copy and keeps sweeping, and that a sweep which throws does
not end the loop.

`TimeProvider` is already injected, so `Microsoft.Extensions.Time.Testing.FakeTimeProvider` can
drive the interval without real delays. It needs a fake `IOptionsMonitor` that can be made to
throw on `CurrentValue`, which is the whole point of the test.

---

## 5. Let alerts/stateFile change without a restart

The state file path is resolved once, when the DI container builds the store. Every other setting
is picked up within seconds of the file being saved; this one is not, and the README says so
rather than fixing it.

Low value on its own — nobody moves the state file often — but it disappears for free if the store
is ever refactored to take its path per call, which is roughly what adding the history store above
would prompt anyway.
