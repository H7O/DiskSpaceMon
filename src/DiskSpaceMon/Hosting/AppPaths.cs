namespace DiskSpaceMon.Hosting;

/// <summary>
/// Turns the relative paths in the settings file into absolute ones.
/// </summary>
/// <remarks>
/// A Windows service starts with its working directory set to <c>C:\Windows\System32</c>, not to
/// the folder it was installed in. Every path from the settings file is therefore resolved
/// against the application folder explicitly, or the service would look for its templates
/// somewhere in Windows.
/// </remarks>
/// <param name="baseDirectory">The folder the application was installed in.</param>
public sealed class AppPaths(string baseDirectory)
{
    /// <summary>The folder the application was installed in.</summary>
    public string BaseDirectory { get; } = baseDirectory;

    /// <summary>
    /// Resolves a settings path.
    /// </summary>
    /// <param name="path">A path from the settings file, relative or absolute.</param>
    /// <returns>The absolute path it refers to.</returns>
    public string Resolve(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        return Path.IsPathRooted(path)
            ? Path.GetFullPath(path)
            : Path.GetFullPath(Path.Combine(BaseDirectory, path));
    }
}
