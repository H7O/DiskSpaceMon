using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace DiskSpaceMon.Monitoring;

/// <summary>What has already been reported about one volume.</summary>
public sealed class DiskAlertState
{
    /// <summary>Whether the volume was below its threshold at the last sweep.</summary>
    public bool IsBreaching { get; set; }

    /// <summary>When it first went below, in UTC.</summary>
    public DateTimeOffset? FirstBreachUtc { get; set; }

    /// <summary>When an email about it last went out, in UTC.</summary>
    public DateTimeOffset? LastAlertUtc { get; set; }
}

/// <summary>The whole alert history, as written to disk.</summary>
public sealed class AlertStateFile
{
    /// <summary>Format version, so a later change can migrate rather than guess.</summary>
    public int Version { get; set; } = 1;

    /// <summary>State per volume path.</summary>
    public Dictionary<string, DiskAlertState> Disks { get; set; }
        = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Returns the entry for a path, creating an empty one if it is new.</summary>
    /// <param name="path">The volume's canonical path.</param>
    /// <returns>The volume's state.</returns>
    public DiskAlertState For(string path)
    {
        if (Disks.TryGetValue(path, out var existing)) return existing;
        return Disks[path] = new DiskAlertState();
    }
}

/// <summary>Reads and writes the alert history.</summary>
public interface IAlertStateStore
{
    /// <summary>
    /// Loads the history.
    /// </summary>
    /// <param name="cancellationToken">Cancellation for the read.</param>
    /// <returns>
    /// The stored history, or an empty one if there is no file yet or it cannot be understood.
    /// </returns>
    Task<AlertStateFile> LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes the history back.
    /// </summary>
    /// <param name="state">The history to persist.</param>
    /// <param name="cancellationToken">Cancellation for the write.</param>
    /// <returns>A task that completes once the file is in place.</returns>
    Task SaveAsync(AlertStateFile state, CancellationToken cancellationToken = default);
}

/// <summary>
/// Keeps the alert history in a JSON file beside the application.
/// </summary>
/// <remarks>
/// Without it, restarting the service -- a reboot, a patch window, a settings change -- would
/// look like a fresh breach on every disk that was already low, and re-send every alert.
/// </remarks>
public sealed class JsonAlertStateStore(string filePath, ILogger<JsonAlertStateStore> logger)
    : IAlertStateStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <inheritdoc/>
    public async Task<AlertStateFile> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath)) return new AlertStateFile();

        try
        {
            await using var stream = File.OpenRead(filePath);
            var state = await JsonSerializer
                .DeserializeAsync<AlertStateFile>(stream, SerializerOptions, cancellationToken)
                .ConfigureAwait(false);

            if (state is null) return new AlertStateFile();

            // The file is written camel-cased but read back into a case-insensitive dictionary,
            // so a path that changed case between runs still matches.
            state.Disks = new Dictionary<string, DiskAlertState>(
                state.Disks, StringComparer.OrdinalIgnoreCase);

            return state;
        }
        catch (Exception ex) when (ex is JsonException or IOException
                                      or UnauthorizedAccessException)
        {
            // Starting from an empty history costs one duplicate alert. Refusing to start costs
            // the monitoring itself, so this is not worth failing over.
            logger.LogWarning(
                ex, "Could not read the alert history at {Path}; starting from empty.", filePath);
            return new AlertStateFile();
        }
    }

    /// <inheritdoc/>
    public async Task SaveAsync(
        AlertStateFile state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);

        try
        {
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            // Written to a temporary file and moved into place, so a crash mid-write leaves the
            // previous history intact rather than a half-written file that will not parse.
            var temporary = filePath + ".tmp";
            await using (var stream = File.Create(temporary))
            {
                await JsonSerializer
                    .SerializeAsync(stream, state, SerializerOptions, cancellationToken)
                    .ConfigureAwait(false);
            }

            File.Move(temporary, filePath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogError(
                ex, "Could not write the alert history to {Path}; alerts may repeat after a "
                    + "restart.", filePath);
        }
    }
}
