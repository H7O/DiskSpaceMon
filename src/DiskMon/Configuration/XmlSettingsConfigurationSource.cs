using Microsoft.Extensions.Configuration;

namespace DiskMon.Configuration;

/// <summary>
/// A configuration source backed by a settings XML file.
/// </summary>
public sealed class XmlSettingsConfigurationSource : FileConfigurationSource
{
    /// <summary>
    /// Colon-delimited paths of the sections whose child elements form a list, e.g.
    /// <c>monitoring:disks</c>.
    /// </summary>
    public IReadOnlyCollection<string> Collections { get; set; } = [];

    /// <inheritdoc/>
    public override IConfigurationProvider Build(IConfigurationBuilder builder)
    {
        EnsureDefaults(builder);
        return new XmlSettingsConfigurationProvider(this);
    }
}

/// <summary>
/// Reads a settings XML file into configuration keys, reloading when the file changes if the
/// source asks for it.
/// </summary>
public sealed class XmlSettingsConfigurationProvider(XmlSettingsConfigurationSource source)
    : FileConfigurationProvider(source)
{
    /// <inheritdoc/>
    /// <exception cref="FormatException">
    /// The document is malformed. On a reload the framework captures this and rethrows it when a
    /// key is next read, which is why callers hold on to the last settings that loaded cleanly.
    /// </exception>
    public override void Load(Stream stream)
        => Data = XmlSettingsParser.Parse(stream, source.Collections);
}
