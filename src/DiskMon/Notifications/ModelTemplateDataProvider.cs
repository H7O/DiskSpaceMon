using Com.H.Text.Template2;

namespace DiskMon.Notifications;

/// <summary>
/// Serves a template's repeating rows from data the application already has in memory.
/// </summary>
/// <remarks>
/// <para>
/// The template engine repeats a file once per row returned for its
/// <c>&lt;h-embedded-data&gt;</c> block, and normally those rows come from a database. DiskMon
/// has no database, so it supplies the rows itself through the engine's own extension point --
/// one provider, no fork -- and a template block reads:
/// </para>
/// <code>
/// &lt;h-embedded-data content-type="model"&gt;&lt;![CDATA[disks]]&gt;&lt;/h-embedded-data&gt;
/// </code>
/// <para>
/// The CDATA names which set of rows to use. It cannot be left empty: the engine skips the
/// provider entirely for a block with no content, and would then render the row template once
/// with no data rather than once per disk.
/// </para>
/// </remarks>
public sealed class ModelTemplateDataProvider(
    IReadOnlyDictionary<string, IReadOnlyList<object>> rowSets) : ITemplateDataProvider
{
    /// <summary>The <c>content-type</c> this provider answers for.</summary>
    public const string ContentType = "model";

    /// <inheritdoc/>
    /// <exception cref="InvalidOperationException">
    /// The block names a set of rows that was not supplied, which means the template and the code
    /// have drifted apart. Failing loudly beats sending an email with an empty table in it.
    /// </exception>
    public ValueTask<IReadOnlyList<dynamic>?> GetDataAsync(
        TemplateDataRequest request, CancellationToken cancellationToken = default)
    {
        if (request is null
            || !string.Equals(request.ContentType, ContentType, StringComparison.OrdinalIgnoreCase))
        {
            return new ValueTask<IReadOnlyList<dynamic>?>((IReadOnlyList<dynamic>?)null);
        }

        var name = request.Query?.Trim() ?? "";

        if (!rowSets.TryGetValue(name, out var rows))
        {
            throw new InvalidOperationException(
                $"The template asked for the row set '{name}', which this email does not supply. "
                + $"Available: {(rowSets.Count == 0 ? "none" : string.Join(", ", rowSets.Keys))}.");
        }

        return new ValueTask<IReadOnlyList<dynamic>?>(rows);
    }
}
