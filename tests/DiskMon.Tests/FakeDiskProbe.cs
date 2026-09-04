using System.Diagnostics.CodeAnalysis;
using DiskMon.Monitoring;

namespace DiskMon.Tests;

/// <summary>A disk probe whose volumes the test invents.</summary>
internal sealed class FakeDiskProbe : IDiskProbe
{
    private readonly Dictionary<string, DiskReading> _readings = new(StringComparer.OrdinalIgnoreCase);

    public List<string> FixedDrives { get; } = [];

    public FakeDiskProbe With(string path, long totalBytes, long freeBytes, string? label = null)
    {
        _readings[path] = new DiskReading(path, label, totalBytes, freeBytes);
        return this;
    }

    public IReadOnlyList<string> FixedDriveRootPaths() => FixedDrives;

    public bool TryRead(
        string path,
        [NotNullWhen(true)] out DiskReading? reading,
        [NotNullWhen(false)] out string? error)
    {
        if (_readings.TryGetValue(path, out reading))
        {
            error = null;
            return true;
        }

        error = $"'{path}' is not present in this test.";
        return false;
    }
}

/// <summary>Byte counts written the way the tests read them.</summary>
internal static class Size
{
    public static long Gb(double gigabytes) => (long)(gigabytes * 1024 * 1024 * 1024);
}
