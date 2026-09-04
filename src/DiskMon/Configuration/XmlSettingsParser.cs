using System.Xml;
using System.Xml.Linq;

namespace DiskMon.Configuration;

/// <summary>
/// Turns a settings XML document into the flat <c>key:sub-key</c> dictionary that
/// <see cref="Microsoft.Extensions.Configuration.IConfiguration"/> is built on.
/// </summary>
/// <remarks>
/// <para>
/// The XML provider that ships with Microsoft.Extensions.Configuration rejects two sibling
/// elements with the same name unless each carries a distinguishing <c>Name</c> attribute, and
/// that attribute then becomes a key segment. Expressing a list of disks through it means
/// hand-maintaining <c>Name="0"</c>, <c>Name="1"</c> indices in the file — delete the middle
/// entry and the gap silently truncates the bound list. For a file a DevOps engineer edits by
/// hand that is the wrong trade, so this parser indexes repeated elements itself.
/// </para>
/// <para>
/// Which elements are lists is declared in code, by path, rather than guessed from the document.
/// A single-entry list and a section with one child element are indistinguishable otherwise, so
/// any heuristic would break the moment an operator deleted the second disk.
/// </para>
/// </remarks>
internal static class XmlSettingsParser
{
    /// <summary>
    /// Parses <paramref name="stream"/> into configuration keys.
    /// </summary>
    /// <param name="stream">The settings document.</param>
    /// <param name="collectionPaths">
    /// Colon-delimited paths of the elements whose children are a list, e.g.
    /// <c>monitoring:disks</c>. Numeric segments are ignored when matching, so a list nested
    /// inside a list is declared once, without indices.
    /// </param>
    /// <returns>The flattened keys, compared case-insensitively.</returns>
    /// <exception cref="FormatException">
    /// The document is not well formed, has no root, or repeats a sibling element outside a
    /// declared list. The message carries the line number.
    /// </exception>
    public static IDictionary<string, string?> Parse(
        Stream stream, IReadOnlyCollection<string> collectionPaths)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(collectionPaths);

        XDocument document;
        try
        {
            document = XDocument.Load(stream, LoadOptions.SetLineInfo);
        }
        catch (XmlException ex)
        {
            throw new FormatException(
                $"The settings file is not well-formed XML (line {ex.LineNumber}, position "
                + $"{ex.LinePosition}): {ex.Message}", ex);
        }

        var root = document.Root
            ?? throw new FormatException("The settings file has no root element.");

        var collections = new HashSet<string>(collectionPaths, StringComparer.OrdinalIgnoreCase);
        var data = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        // The root element is the document's container, not a key, so its name is dropped —
        // the same convention the framework's own XML provider follows.
        VisitChildren(root, prefix: null, data, collections);
        return data;
    }

    private static void VisitChildren(
        XElement parent,
        string? prefix,
        Dictionary<string, string?> data,
        HashSet<string> collections)
    {
        var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var child in parent.Elements())
        {
            var name = child.Name.LocalName;
            if (seen.TryGetValue(name, out var firstLine))
            {
                throw new FormatException(
                    $"'{Describe(prefix, name)}' appears more than once (line {Line(child)}; "
                    + $"first seen on line {firstLine}). Repeating an element is only allowed "
                    + "inside a section registered as a list in Program.cs.");
            }
            seen[name] = Line(child);

            var path = prefix is null ? name : $"{prefix}:{name}";

            if (collections.Contains(Normalise(path)))
                VisitCollection(child, path, data, collections);
            else
                VisitElement(child, path, data, collections);
        }
    }

    /// <summary>
    /// Writes a declared list. Each child becomes an index, and the child's own element name is
    /// dropped, so <c>&lt;disks&gt;&lt;disk&gt;…</c> binds to a <c>Disks</c> property whatever
    /// the item element is called.
    /// </summary>
    private static void VisitCollection(
        XElement container,
        string path,
        Dictionary<string, string?> data,
        HashSet<string> collections)
    {
        foreach (var attribute in container.Attributes().Where(a => !a.IsNamespaceDeclaration))
            Add(data, $"{path}:{attribute.Name.LocalName}", attribute.Value, container);

        var index = 0;
        foreach (var item in container.Elements())
            VisitElement(item, $"{path}:{index++}", data, collections);
    }

    private static void VisitElement(
        XElement element,
        string path,
        Dictionary<string, string?> data,
        HashSet<string> collections)
    {
        foreach (var attribute in element.Attributes().Where(a => !a.IsNamespaceDeclaration))
            Add(data, $"{path}:{attribute.Name.LocalName}", attribute.Value, element);

        if (element.HasElements)
        {
            VisitChildren(element, path, data, collections);
            return;
        }

        // An element left blank means "not set", not "set to empty". Writing the empty string
        // would make <intervalSeconds></intervalSeconds> a binding failure rather than a
        // fallback to the default, and would have a blank <password/> override one supplied
        // through the environment.
        var value = element.Value.Trim();
        if (value.Length > 0) Add(data, path, value, element);
    }

    private static void Add(
        Dictionary<string, string?> data, string key, string value, XElement source)
    {
        if (data.ContainsKey(key))
        {
            throw new FormatException(
                $"'{key}' is set twice (line {Line(source)}). A setting can come from an "
                + "element or from an attribute, but not from both.");
        }
        data[key] = value;
    }

    /// <summary>Drops index segments so a list is declared by shape, not by position.</summary>
    private static string Normalise(string path)
        => string.Join(
            ':',
            path.Split(':').Where(segment => segment.Length == 0 || !segment.All(char.IsAsciiDigit)));

    private static string Describe(string? prefix, string name)
        => prefix is null ? name : $"{prefix}:{name}";

    private static int Line(XObject node)
        => node is IXmlLineInfo info && info.HasLineInfo() ? info.LineNumber : 0;
}
