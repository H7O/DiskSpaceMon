using Microsoft.Extensions.Configuration;

namespace DiskSpaceMon.Configuration;

/// <summary>Adds a settings XML file to a configuration builder.</summary>
public static class XmlSettingsConfigurationExtensions
{
    /// <summary>
    /// Adds <paramref name="path"/> as a configuration source.
    /// </summary>
    /// <param name="builder">The builder to add to.</param>
    /// <param name="path">Path to the file, relative to the builder's base path.</param>
    /// <param name="collections">
    /// Sections whose child elements form a list, by colon-delimited path — see
    /// <see cref="XmlSettingsConfigurationSource.Collections"/>.
    /// </param>
    /// <param name="optional">Whether a missing file is tolerated.</param>
    /// <param name="reloadOnChange">Whether edits to the file are picked up while running.</param>
    /// <returns><paramref name="builder"/>, for chaining.</returns>
    public static IConfigurationBuilder AddXmlSettingsFile(
        this IConfigurationBuilder builder,
        string path,
        IReadOnlyCollection<string> collections,
        bool optional = false,
        bool reloadOnChange = false)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(collections);

        return builder.Add<XmlSettingsConfigurationSource>(source =>
        {
            source.Path = path;
            source.Collections = collections;
            source.Optional = optional;
            source.ReloadOnChange = reloadOnChange;

            // The framework's own file sources use the same delay. An editor that writes a file
            // in two passes would otherwise be read halfway through the save.
            source.ReloadDelay = 250;
            source.ResolveFileProvider();
        });
    }
}
