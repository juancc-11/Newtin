using System.Globalization;

namespace Newtin.Core.Utilities;

public static class ByteFormatter
{
    private static readonly string[] Units = ["B", "KB", "MB", "GB", "TB", "PB"];

    public static string Format(long bytes, CultureInfo? culture = null)
    {
        if (bytes < 0) return "No disponible";

        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < Units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        var text = value.ToString(unit == 0 ? "0" : "0.#", culture ?? CultureInfo.CurrentCulture);
        return $"{text} {Units[unit]}";
    }
}