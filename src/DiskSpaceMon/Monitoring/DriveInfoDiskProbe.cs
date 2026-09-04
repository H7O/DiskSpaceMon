using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace DiskSpaceMon.Monitoring;

/// <summary>
/// Reads volumes through <see cref="DriveInfo"/>, falling back to the Win32 call for paths
/// <see cref="DriveInfo"/> will not accept.
/// </summary>
/// <remarks>
/// <see cref="DriveInfo"/> only understands a drive root, so a volume mounted into a folder --
/// the usual shape for a SQL Server data volume -- cannot be read through it at all. Asking
/// Windows directly covers those, and the two paths report the same numbers for a drive letter.
/// </remarks>
public sealed partial class DriveInfoDiskProbe : IDiskProbe
{
    /// <inheritdoc/>
    public IReadOnlyList<string> FixedDriveRootPaths()
    {
        try
        {
            return [.. DriveInfo.GetDrives()
                .Where(drive => drive.DriveType == DriveType.Fixed && drive.IsReady)
                .Select(drive => drive.Name)];
        }
        catch (IOException)
        {
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <inheritdoc/>
    public bool TryRead(
        string path,
        [NotNullWhen(true)] out DiskReading? reading,
        [NotNullWhen(false)] out string? error)
    {
        reading = null;
        error = null;

        if (string.IsNullOrWhiteSpace(path))
        {
            error = "No path was given.";
            return false;
        }

        var normalised = DiskPath.Normalise(path);

        try
        {
            var drive = new DriveInfo(normalised);
            if (!drive.IsReady)
            {
                error = $"'{normalised}' is not ready.";
                return false;
            }

            reading = new DiskReading(
                drive.Name, ReadLabel(drive), drive.TotalSize, drive.AvailableFreeSpace);
            return true;
        }
        catch (ArgumentException) when (OperatingSystem.IsWindows())
        {
            // Not a drive root: a mount point, or a folder on a mounted volume.
            return TryReadWindowsPath(normalised, out reading, out error);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException
                                      or UnauthorizedAccessException)
        {
            error = $"'{normalised}' could not be read: {ex.Message}";
            return false;
        }
    }

    private static string? ReadLabel(DriveInfo drive)
    {
        try
        {
            var label = drive.VolumeLabel;
            return string.IsNullOrWhiteSpace(label) ? null : label;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                      or NotSupportedException)
        {
            // A label is decoration. Losing it must not cost us the reading.
            return null;
        }
    }

    [SupportedOSPlatform("windows")]
    private static bool TryReadWindowsPath(
        string path,
        [NotNullWhen(true)] out DiskReading? reading,
        [NotNullWhen(false)] out string? error)
    {
        reading = null;

        // GetDiskFreeSpaceEx wants a directory, and is happier with a trailing separator.
        var directory = path.EndsWith(Path.DirectorySeparatorChar) ? path : path + Path.DirectorySeparatorChar;

        if (!GetDiskFreeSpaceEx(directory, out var availableToCaller, out var total, out _))
        {
            error = $"'{path}' could not be read: "
                + new Win32Exception(Marshal.GetLastWin32Error()).Message;
            return false;
        }

        reading = new DiskReading(
            path,
            VolumeLabel: null,
            TotalBytes: ToInt64(total),
            FreeBytes: ToInt64(availableToCaller));

        error = null;
        return true;
    }

    /// <summary>
    /// Windows reports these as unsigned. No volume is anywhere near <see cref="long.MaxValue"/>
    /// bytes, so clamping rather than checking keeps the caller free of a case that cannot happen.
    /// </summary>
    private static long ToInt64(ulong value)
        => value > long.MaxValue ? long.MaxValue : (long)value;

    [LibraryImport("kernel32.dll", EntryPoint = "GetDiskFreeSpaceExW",
        StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    [SupportedOSPlatform("windows")]
    private static partial bool GetDiskFreeSpaceEx(
        string directoryName,
        out ulong freeBytesAvailableToCaller,
        out ulong totalNumberOfBytes,
        out ulong totalNumberOfFreeBytes);
}
