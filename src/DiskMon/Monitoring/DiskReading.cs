using System.Globalization;

namespace DiskMon.Monitoring;

/// <summary>What one volume reported at one moment.</summary>
/// <param name="Path">The root or mount point that was read.</param>
/// <param name="VolumeLabel">The volume's own label, where the platform supplies one.</param>
/// <param name="TotalBytes">Capacity.</param>
/// <param name="FreeBytes">
/// Space available to the account the process runs under, which is what a quota-bound service
/// can actually use, and so what an alert should be measured against.
/// </param>
public sealed record DiskReading(
    string Path,
    string? VolumeLabel,
    long TotalBytes,
    long FreeBytes)
{
    /// <summary>Space in use.</summary>
    public long UsedBytes => Math.Max(0, TotalBytes - FreeBytes);

    /// <summary>Free space as a percentage of capacity. Zero for a zero-byte volume.</summary>
    public double FreePercent
        => TotalBytes <= 0 ? 0d : FreeBytes * 100d / TotalBytes;

    /// <summary>Free space in binary gigabytes.</summary>
    public double FreeGb => ByteSize.ToGb(FreeBytes);

    /// <summary>Capacity in binary gigabytes.</summary>
    public double TotalGb => ByteSize.ToGb(TotalBytes);
}

/// <summary>Byte arithmetic and the wording used in logs and email.</summary>
public static class ByteSize
{
    private const double BytesPerGb = 1024d * 1024d * 1024d;

    /// <summary>Converts bytes to binary gigabytes.</summary>
    /// <param name="bytes">The count to convert.</param>
    /// <returns>The same quantity in gigabytes.</returns>
    public static double ToGb(long bytes) => bytes / BytesPerGb;

    /// <summary>
    /// Renders a byte count the way Windows Explorer does, so a reader can compare the alert
    /// against what the machine shows them.
    /// </summary>
    /// <param name="bytes">The count to render.</param>
    /// <returns>A short string such as <c>466.1 GB</c>.</returns>
    public static string Format(long bytes)
    {
        string[] units = ["bytes", "KB", "MB", "GB", "TB", "PB"];
        double value = bytes;
        var unit = 0;

        while (value >= 1024d && unit < units.Length - 1)
        {
            value /= 1024d;
            unit++;
        }

        return unit == 0
            ? string.Format(CultureInfo.InvariantCulture, "{0:0} {1}", value, units[unit])
            : string.Format(CultureInfo.InvariantCulture, "{0:0.#} {1}", value, units[unit]);
    }

    /// <summary>Renders a percentage to one decimal place.</summary>
    /// <param name="percent">The value to render.</param>
    /// <returns>A string such as <c>7.4%</c>.</returns>
    public static string FormatPercent(double percent)
        => string.Format(CultureInfo.InvariantCulture, "{0:0.#}%", percent);
}
