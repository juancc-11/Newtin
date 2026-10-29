namespace Newtin.Server.Persistence;

public enum BoardFormat { Native, Png, Svg, Markdown, Csv, Text }

public static class BoardFormats
{
    public static BoardFormat? FromPath(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".newtin" or ".json" => BoardFormat.Native,
        ".png" => BoardFormat.Png,
        ".svg" => BoardFormat.Svg,
        ".md" or ".markdown" => BoardFormat.Markdown,
        ".csv" => BoardFormat.Csv,
        ".txt" => BoardFormat.Text,
        _ => null
    };
}