namespace DiskMon.Monitoring;

/// <summary>
/// Puts a volume path into one canonical form.
/// </summary>
/// <remarks>
/// An operator writes <c>C</c>, <c>C:</c> or <c>C:\</c> and means the same volume, while
/// enumerating drives always reports <c>C:\</c>. Without a single normalisation, an explicit
/// entry written one way would fail to suppress the discovered entry for the same disk, and the
/// operator would be emailed twice about it.
/// </remarks>
public static class DiskPath
{
    /// <summary>
    /// Canonicalises <paramref name="path"/>: a bare drive letter gains its colon and separator,
    /// and surrounding whitespace goes. Anything else -- a mount point, a POSIX path -- is
    /// returned trimmed, since only the caller knows what it means.
    /// </summary>
    /// <param name="path">The path as written in the settings file, or as enumerated.</param>
    /// <returns>The canonical form, or an empty string for a blank input.</returns>
    public static string Normalise(string? path)
    {
        var trimmed = path?.Trim() ?? "";
        if (trimmed.Length == 0 || !OperatingSystem.IsWindows()) return trimmed;

        if (trimmed.Length == 1 && char.IsLetter(trimmed[0]))
            return $"{trimmed}:\\";

        if (trimmed.Length == 2 && char.IsLetter(trimmed[0]) && trimmed[1] == ':')
            return trimmed + '\\';

        return trimmed;
    }
}
