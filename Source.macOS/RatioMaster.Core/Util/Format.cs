namespace RatioMaster.Core.Util;

using System.Globalization;

public static class Format
{
    public static string FileSize(long bytes)
    {
        if (bytes < 0)
        {
            return "0 bytes";
        }

        return bytes switch
        {
            >= 0x40000000 => string.Format(CultureInfo.InvariantCulture, "{0:########0.00} GB", bytes / 1073741824.0),
            >= 0x100000 => string.Format(CultureInfo.InvariantCulture, "{0:####0.00} MB", bytes / 1048576.0),
            >= 0x400 => string.Format(CultureInfo.InvariantCulture, "{0:####0.00} KB", bytes / 1024.0),
            _ => string.Format(CultureInfo.InvariantCulture, "{0} bytes", bytes),
        };
    }

    public static string Duration(int seconds)
    {
        if (seconds < 0)
        {
            seconds = 0;
        }

        return seconds < 3600
            ? $"{seconds / 60:00}:{seconds % 60:00}"
            : $"{seconds / 3600:00}:{seconds % 3600 / 60:00}:{seconds % 60:00}";
    }

    public static string Ratio(double? ratio) =>
        ratio is null ? "NaN" : ratio.Value.ToString("0.00", CultureInfo.InvariantCulture);

    public static string Percent(double value) =>
        value.ToString("0.##", CultureInfo.InvariantCulture);
}
