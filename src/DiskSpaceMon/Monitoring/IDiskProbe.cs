using System.Diagnostics.CodeAnalysis;

namespace DiskSpaceMon.Monitoring;

/// <summary>
/// Reads volumes. Behind an interface so the threshold logic can be tested against invented
/// disks rather than whatever the build agent happens to have mounted.
/// </summary>
public interface IDiskProbe
{
    /// <summary>
    /// Lists the ready fixed drives, by root path.
    /// </summary>
    /// <returns>Root paths such as <c>C:\</c>, or an empty list if none can be enumerated.</returns>
    IReadOnlyList<string> FixedDriveRootPaths();

    /// <summary>
    /// Reads one volume.
    /// </summary>
    /// <param name="path">A root path, a drive letter, or a mount point.</param>
    /// <param name="reading">The volume's figures, when the read succeeded.</param>
    /// <param name="error">Why the read failed, otherwise.</param>
    /// <returns>Whether the volume could be read.</returns>
    bool TryRead(
        string path,
        [NotNullWhen(true)] out DiskReading? reading,
        [NotNullWhen(false)] out string? error);
}
