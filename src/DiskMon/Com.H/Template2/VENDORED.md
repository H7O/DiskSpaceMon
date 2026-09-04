# Vendored: Com.H.Text.Template2

These files are a **verbatim copy** of `src/*.cs` from
[H7O/Com.H.Text.Template2](https://github.com/H7O/Com.H.Text.Template2), taken while that library
was still being tested and had not been published to NuGet.

Nothing here has been edited. Keeping the copy byte-identical to upstream means the folder can be
deleted outright once the package ships, with no changes to merge back and nothing to diff:

```xml
<!-- in DiskMon.csproj, replacing the Com.H.Data.Common reference -->
<PackageReference Include="Com.H.Text.Template2" Version="..." />
```

Then delete this folder. Nothing else in DiskMon changes: the namespace stays
`Com.H.Text.Template2` either way.

## Why Com.H.Data.Common is still referenced

The engine's `DbTemplateDataProvider`, `TemplateDataRequest` and `TemplateExtensions` use
`DbQueryParams` from [Com.H.Data.Common](https://www.nuget.org/packages/Com.H.Data.Common), which
is the package's one dependency. DiskMon never touches a database, but the files are copied
unedited, so the reference comes with them. Stripping the database-facing files would have made
this a fork rather than a copy, and the next upstream change would have had to be merged by hand.

## What DiskMon actually uses

Only the value-substitution and repeat-per-row parts. The rows come from
`DiskMon.Notifications.ModelTemplateDataProvider`, an `ITemplateDataProvider` written against the
library's public extension point rather than a change to it.
