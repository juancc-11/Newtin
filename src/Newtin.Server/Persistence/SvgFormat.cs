using System.Globalization;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using Newtin.Server.Models;

namespace Newtin.Server.Persistence;

/// <summary>Imagen vectorial de la pizarra. Solo contiene formas y texto: ni scripts ni referencias externas.</summary>
public static partial class SvgFormat
{
    [GeneratedRegex(@"\A#[0-9A-Fa-f]{6}\z")]
    private static partial Regex ColorPattern();

    private static string N(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    private static string SafeColor(string? color, string fallback) =>
        color is not null && ColorPattern().IsMatch(color) ? color : fallback;

    private static string Escape(string? text)
    {
        var clean = new string((text ?? "").Where(c => XmlConvert.IsXmlChar(c) || char.IsSurrogate(c)).ToArray());
        return SecurityElement.Escape(clean) ?? "";
    }

    private static string TextColorFor(string hex)
    {
        var r = Convert.ToInt32(hex.Substring(1, 2), 16);
        var g = Convert.ToInt32(hex.Substring(3, 2), 16);
        var b = Convert.ToInt32(hex.Substring(5, 2), 16);
        return 0.299 * r + 0.587 * g + 0.114 * b > 150 ? "#17131F" : "#FFFFFF";
    }

    public static string Write(WhiteboardSnapshot snapshot)
    {
        var layout = BoardLayoutCalculator.Compute(snapshot);
        var categories = snapshot.Categories.ToDictionary(c => c.Id);
        var sb = new StringBuilder();

        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n");
        sb.Append($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{N(layout.Width)}\" height=\"{N(layout.Height)}\" " +
                  $"viewBox=\"{N(layout.X)} {N(layout.Y)} {N(layout.Width)} {N(layout.Height)}\">\n");
        sb.Append("  <title>Pizarra Newtin</title>\n");
        sb.Append($"  <rect x=\"{N(layout.X)}\" y=\"{N(layout.Y)}\" width=\"{N(layout.Width)}\" height=\"{N(layout.Height)}\" fill=\"#FAF8FF\"/>\n");

        var noteIndex = 0;
        foreach (var element in snapshot.Elements.OrderBy(e => e.Order))
        {
            switch (element.Type)
            {
                case ElementType.Stroke: WriteStroke(sb, element); break;
                case ElementType.Text: WriteText(sb, element); break;
                case ElementType.Note: WriteNote(sb, element, categories, noteIndex++); break;
            }
        }

        sb.Append("</svg>\n");
        return sb.ToString();
    }

    private static void WriteStroke(StringBuilder sb, WhiteboardElement element)
    {
        var points = element.Points ?? [];
        if (points.Count == 0) return;

        var color = SafeColor(element.Color, "#17131F");
        var width = element.Width ?? 3;
        var transform = element.X != 0 || element.Y != 0 ? $" transform=\"translate({N(element.X)} {N(element.Y)})\"" : "";

        if (points.Count == 1)
        {
            sb.Append($"  <circle cx=\"{N(points[0].X)}\" cy=\"{N(points[0].Y)}\" r=\"{N(width / 2)}\" fill=\"{color}\"{transform}/>\n");
            return;
        }

        var list = string.Join(" ", points.Select(p => $"{N(p.X)},{N(p.Y)}"));
        sb.Append($"  <polyline points=\"{list}\" fill=\"none\" stroke=\"{color}\" stroke-width=\"{N(width)}\" " +
                  $"stroke-linecap=\"round\" stroke-linejoin=\"round\"{transform}/>\n");
    }

    private static void WriteText(StringBuilder sb, WhiteboardElement element)
    {
        var size = element.Width ?? 24;
        var color = SafeColor(element.Color, "#17131F");
        var lines = (element.Text ?? "").Split('\n');

        sb.Append($"  <text x=\"{N(element.X)}\" y=\"{N(element.Y)}\" font-family=\"Segoe UI, Arial, sans-serif\" " +
                  $"font-size=\"{N(size)}\" fill=\"{color}\">\n");
        for (var i = 0; i < lines.Length; i++)
            sb.Append($"    <tspan x=\"{N(element.X)}\" dy=\"{N(i == 0 ? size : size * 1.25)}\">{Escape(lines[i])}</tspan>\n");
        sb.Append("  </text>\n");
    }

    private static void WriteNote(StringBuilder sb, WhiteboardElement element,
        Dictionary<string, WhiteboardCategory> categories, int index)
    {
        var (x, y) = BoardLayoutCalculator.NotePosition(element);
        var width = BoardLayoutCalculator.NoteWidth;
        var height = BoardLayoutCalculator.NoteHeight;
        var fill = SafeColor(element.Color, "#FFF1A8");
        var bodyColor = TextColorFor(fill);

        WhiteboardCategory? category = null;
        if (element.CategoryId is not null) categories.TryGetValue(element.CategoryId, out category);

        var headerFill = category is null ? "#000000" : SafeColor(category.Color, "#A00082");
        var headerOpacity = category is null ? "0.12" : "1";
        var headerText = category is null ? bodyColor : TextColorFor(headerFill);

        sb.Append($"  <clipPath id=\"note-clip-{index}\"><rect x=\"{N(x)}\" y=\"{N(y)}\" width=\"{N(width)}\" height=\"{N(height)}\" rx=\"10\"/></clipPath>\n");
        sb.Append($"  <rect x=\"{N(x)}\" y=\"{N(y)}\" width=\"{N(width)}\" height=\"{N(height)}\" rx=\"10\" fill=\"{fill}\"/>\n");
        sb.Append($"  <g clip-path=\"url(#note-clip-{index})\" font-family=\"Segoe UI, Arial, sans-serif\">\n");
        sb.Append($"    <rect x=\"{N(x)}\" y=\"{N(y)}\" width=\"{N(width)}\" height=\"24\" fill=\"{headerFill}\" fill-opacity=\"{headerOpacity}\"/>\n");
        sb.Append($"    <text x=\"{N(x + 8)}\" y=\"{N(y + 16)}\" font-size=\"12\" font-weight=\"600\" fill=\"{headerText}\">{Escape(category?.Name ?? "Sin categoría")}</text>\n");

        var lines = Wrap(element.Text ?? "", 24, 6);
        for (var i = 0; i < lines.Count; i++)
            sb.Append($"    <text x=\"{N(x + 8)}\" y=\"{N(y + 44 + i * 18)}\" font-size=\"14\" fill=\"{bodyColor}\">{Escape(lines[i])}</text>\n");

        sb.Append("  </g>\n");
    }

    /// <summary>Ajuste de línea aproximado: la medida exacta depende de la tipografía del visor.</summary>
    private static List<string> Wrap(string text, int maxChars, int maxLines)
    {
        var lines = new List<string>();

        foreach (var paragraph in text.Split('\n'))
        {
            var current = "";
            foreach (var word in paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var rest = word;
                while (rest.Length > maxChars)
                {
                    if (current.Length > 0) { lines.Add(current); current = ""; }
                    lines.Add(rest[..maxChars]);
                    rest = rest[maxChars..];
                }

                if (current.Length == 0) current = rest;
                else if (current.Length + 1 + rest.Length <= maxChars) current += " " + rest;
                else { lines.Add(current); current = rest; }
            }
            lines.Add(current);
        }

        if (lines.Count > maxLines)
        {
            lines = lines.Take(maxLines).ToList();
            var last = lines[^1];
            lines[^1] = (last.Length >= maxChars ? last[..(maxChars - 1)] : last) + "…";
        }

        return lines;
    }
}