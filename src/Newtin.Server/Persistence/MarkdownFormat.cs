using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Newtin.Server.Models;
using Newtin.Server.Validation;

namespace Newtin.Server.Persistence;

/// <summary>
/// Notas y textos como viñetas, agrupados por categoría (## Categoría). Sin dibujos ni posiciones.
/// Al importar, también sirve un texto plano: cada línea es una nota.
/// </summary>
public static partial class MarkdownFormat
{
    private const int MaxItems = 1000;
    private const string FreeTextsTitle = "Textos libres";
    private const string NoCategoryTitle = "Sin categoría";

    [GeneratedRegex(@"^(#{1,6})\s+(.+?)\s*#*\s*$")]
    private static partial Regex HeadingPattern();

    [GeneratedRegex(@"^(?:[-*+]|\d{1,9}[.)])\s+(.*)$")]
    private static partial Regex BulletPattern();

    [GeneratedRegex(@"^_Exportada el .*_$")]
    private static partial Regex ExportedPattern();

    private static string Escape(string text) => text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    private static string Unescape(string text) => text.Replace("&lt;", "<").Replace("&gt;", ">").Replace("&amp;", "&");

    public static string Write(WhiteboardSnapshot snapshot, DateTimeOffset now)
    {
        var sb = new StringBuilder();
        sb.Append("# Pizarra Newtin\n\n");
        sb.Append("_Exportada el ").Append(now.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture)).Append("_\n\n");

        var notes = snapshot.Elements.Where(e => e.Type == ElementType.Note).OrderBy(e => e.Order).ToList();
        var known = snapshot.Categories.Select(c => c.Id).ToHashSet();
        var loose = notes.Where(n => n.CategoryId is null || !known.Contains(n.CategoryId)).ToList();

        if (loose.Count > 0) Section(sb, NoCategoryTitle, loose);
        foreach (var category in snapshot.Categories)
            Section(sb, category.Name, notes.Where(n => n.CategoryId == category.Id).ToList());

        var texts = snapshot.Elements.Where(e => e.Type == ElementType.Text).OrderBy(e => e.Order).ToList();
        if (texts.Count > 0) Section(sb, FreeTextsTitle, texts);

        return sb.ToString();
    }

    private static void Section(StringBuilder sb, string title, IReadOnlyList<WhiteboardElement> items)
    {
        sb.Append("## ").Append(Escape(title)).Append("\n\n");

        foreach (var item in items)
        {
            var lines = (item.Text ?? "").Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
            if (lines.Count == 0) continue;

            sb.Append("- ").Append(Escape(lines[0])).Append('\n');
            foreach (var line in lines.Skip(1)) sb.Append("  ").Append(Escape(line)).Append('\n');
        }

        sb.Append('\n');
    }

        private sealed class Item(string? categoryKey, bool isText, string text)
    {
        public string? CategoryKey { get; } = categoryKey;
        public bool IsText { get; } = isText;
        public StringBuilder Text { get; } = new(text);
    }

    public static PlanResult Read(byte[] data)
    {
        var lines = TextDecoder.Decode(data).ReplaceLineEndings("\n").Split('\n');
        var items = new List<Item>();
        var categories = new List<PlanCategory>();
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string? categoryKey = null;
        var freeText = false;
        Item? current = null;

        void Flush()
        {
            if (current is not null) items.Add(current);
            current = null;
        }

        // Cada encabezado crea su categoría aunque no tenga notas debajo, para que las categorías vacías no se pierdan.
        string? Register(string rawName)
        {
            var shortName = rawName.Length > WhiteboardLimits.MaxCategoryNameLength
                ? rawName[..WhiteboardLimits.MaxCategoryNameLength]
                : rawName;

            if (!WhiteboardValidator.TryNormalizeCategoryName(shortName, out var name, out _)) return null;

            var key = name.ToLowerInvariant();
            if (keys.Add(key)) categories.Add(new PlanCategory(key, name, ImportHelpers.PaletteColor(categories.Count)));
            return key;
        }

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line) || ExportedPattern().IsMatch(line))
            {
                Flush();
                continue;
            }

            var heading = HeadingPattern().Match(line);
            if (heading.Success)
            {
                Flush();
                if (heading.Groups[1].Length >= 2)   // el título (#) del archivo no es una categoría
                {
                    var name = Unescape(heading.Groups[2].Value).Trim();
                    freeText = name.Equals(FreeTextsTitle, StringComparison.OrdinalIgnoreCase);
                    categoryKey = freeText || name.Equals(NoCategoryTitle, StringComparison.OrdinalIgnoreCase)
                        ? null
                        : Register(name);
                }
                continue;
            }

            var bullet = BulletPattern().Match(line);
            if (bullet.Success)
            {
                Flush();
                current = new Item(categoryKey, freeText, Unescape(bullet.Groups[1].Value).Trim());
                continue;
            }

            if (current is not null && char.IsWhiteSpace(line[0]))
            {
                current.Text.Append('\n').Append(Unescape(line.Trim()));
                continue;
            }

            Flush();
            current = new Item(categoryKey, freeText, Unescape(line.Trim()));
        }
        Flush();

        var warnings = new List<string>();
        if (items.Count > MaxItems)
        {
            warnings.Add($"Solo se importan las primeras {MaxItems} notas del archivo.");
            items = items.Take(MaxItems).ToList();
        }

        var elements = new List<PlanElement>();
        var truncated = 0;

        foreach (var item in items)
        {
            var text = item.Text.ToString().Trim();
            if (text.Length == 0) continue;

            if (text.Length > WhiteboardLimits.MaxTextLength)
            {
                text = text[..WhiteboardLimits.MaxTextLength];
                truncated++;
            }

            elements.Add(new PlanElement(
                new ElementInput(null, item.IsText ? ElementType.Text : ElementType.Note, 0, 0, text, null, null, null, null),
                item.CategoryKey,
                AutoPlace: true));
        }

        if (truncated > 0)
            warnings.Add($"{truncated} texto(s) superaban {WhiteboardLimits.MaxTextLength} caracteres y se recortaron.");

        return elements.Count == 0
            ? PlanResult.Fail("No se encontró ninguna nota en el archivo.")
            : PlanResult.Ok(new ImportPlan(categories, elements, warnings));
    }
}