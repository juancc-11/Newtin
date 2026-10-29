using System.Globalization;
using System.Text;
using Newtin.Server.Models;
using Newtin.Server.Validation;

namespace Newtin.Server.Persistence;

/// <summary>
/// Notas y textos en tabla (tipo, texto, categoría, colores, autor, x, y). Se lee con coma, punto y coma o tabulador.
/// Al exportar, los textos que empiezan por = + - @ llevan un apóstrofo delante para que Excel no los ejecute como fórmula.
/// </summary>
public static class CsvFormat
{
    private const int MaxRows = 2000;
    private const string FormulaStarts = "=+-@\t\r";

    private static string N(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    private static string Guard(string? text) =>
        string.IsNullOrEmpty(text) ? "" : FormulaStarts.Contains(text[0]) ? "'" + text : text;

    private static string Unguard(string text) =>
        text.Length >= 2 && text[0] == '\'' && FormulaStarts.Contains(text[1]) ? text[1..] : text;

    private static string Field(string? value)
    {
        value ??= "";
        var mustQuote = value.IndexOfAny([',', ';', '"', '\n', '\r']) >= 0
                        || value.StartsWith(' ') || value.EndsWith(' ');
        return mustQuote ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
    }

    public static byte[] Write(WhiteboardSnapshot snapshot)
    {
        var categories = snapshot.Categories.ToDictionary(c => c.Id);
        var sb = new StringBuilder("tipo,texto,categoria,color_categoria,color,autor,x,y\r\n");

        foreach (var element in snapshot.Elements
                     .Where(e => e.Type is ElementType.Note or ElementType.Text)
                     .OrderBy(e => e.Order))
        {
            var category = element.CategoryId is not null && categories.TryGetValue(element.CategoryId, out var found) ? found : null;

            sb.Append(string.Join(",",
                Field(element.Type == ElementType.Note ? "nota" : "texto"),
                Field(Guard(element.Text)),
                Field(Guard(category?.Name)),
                Field(category?.Color),
                Field(element.Color),
                Field(Guard(element.Author)),
                Field(N(element.X)),
                Field(N(element.Y)))).Append("\r\n");
        }

        // El BOM hace que Excel reconozca las tildes.
        return [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(sb.ToString())];
    }

    public static PlanResult Read(byte[] data)
    {
        var rows = ParseRows(TextDecoder.Decode(data));
        if (rows.Count == 0) return PlanResult.Fail("El archivo CSV está vacío.");

        var header = rows[0].Select(NormalizeHeader).ToList();
        int Find(params string[] names) => header.FindIndex(names.Contains);

        var textColumn = Find("texto", "text", "nota", "note", "contenido", "content");
        if (textColumn < 0) return PlanResult.Fail("No se encontró la columna «texto» en la primera fila del CSV.");

        var typeColumn = Find("tipo", "type");
        var categoryColumn = Find("categoria", "category");
        var categoryColorColumn = Find("colorcategoria", "categorycolor");
        var colorColumn = Find("color");
        var xColumn = Find("x");
        var yColumn = Find("y");

        static string Cell(List<string> row, int column) => column >= 0 && column < row.Count ? row[column] : "";

        var warnings = new List<string>();
        var categories = new List<PlanCategory>();
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var elements = new List<PlanElement>();
        var empty = 0;
        var truncated = 0;

        foreach (var row in rows.Skip(1).Take(MaxRows))
        {
            var text = Unguard(Cell(row, textColumn)).Trim();
            if (text.Length == 0)
            {
                empty++;
                continue;
            }

            if (text.Length > WhiteboardLimits.MaxTextLength)
            {
                text = text[..WhiteboardLimits.MaxTextLength];
                truncated++;
            }

            var isText = NormalizeHeader(Cell(row, typeColumn)) is "texto" or "text";

            string? key = null;
            var rawCategory = Unguard(Cell(row, categoryColumn)).Trim();
            if (rawCategory.Length > 0)
            {
                var shortName = rawCategory.Length > WhiteboardLimits.MaxCategoryNameLength
                    ? rawCategory[..WhiteboardLimits.MaxCategoryNameLength]
                    : rawCategory;

                if (WhiteboardValidator.TryNormalizeCategoryName(shortName, out var name, out _))
                {
                    key = name.ToLowerInvariant();
                    if (keys.Add(key))
                        categories.Add(new PlanCategory(key, name,
                            ImportHelpers.ValidColor(Cell(row, categoryColorColumn)) ?? ImportHelpers.PaletteColor(categories.Count)));
                }
            }

            var hasPosition =
                double.TryParse(Cell(row, xColumn), NumberStyles.Float, CultureInfo.InvariantCulture, out var x)
                & double.TryParse(Cell(row, yColumn), NumberStyles.Float, CultureInfo.InvariantCulture, out var y)
                && double.IsFinite(x) && double.IsFinite(y);

            if (hasPosition)
            {
                x = Math.Clamp(x, 0, BoardLayoutCalculator.BoardWidth - BoardLayoutCalculator.NoteWidth);
                y = Math.Clamp(y, 0, BoardLayoutCalculator.BoardHeight - BoardLayoutCalculator.NoteHeight);
            }

            elements.Add(new PlanElement(
                new ElementInput(null, isText ? ElementType.Text : ElementType.Note,
                    hasPosition ? x : 0, hasPosition ? y : 0, text, null,
                    ImportHelpers.ValidColor(Cell(row, colorColumn)), null, null),
                key,
                AutoPlace: !hasPosition));
        }

        if (rows.Count - 1 > MaxRows) warnings.Add($"Solo se importan las primeras {MaxRows} filas.");
        if (empty > 0) warnings.Add($"{empty} fila(s) sin texto se omitieron.");
        if (truncated > 0) warnings.Add($"{truncated} texto(s) se recortaron a {WhiteboardLimits.MaxTextLength} caracteres.");

        return elements.Count == 0
            ? PlanResult.Fail("El CSV no contiene ninguna fila con texto.")
            : PlanResult.Ok(new ImportPlan(categories, elements, warnings));
    }

    private static string NormalizeHeader(string value)
    {
        var builder = new StringBuilder();
        foreach (var c in value.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            if (c is ' ' or '_' or '-') continue;
            builder.Append(c);
        }
        return builder.ToString();
    }

    private static char DetectDelimiter(string text)
    {
        int commas = 0, semicolons = 0, tabs = 0;
        var inQuotes = false;

        foreach (var c in text)
        {
            if (c == '"') inQuotes = !inQuotes;
            else if (!inQuotes && (c == '\n' || c == '\r')) break;
            else if (!inQuotes)
            {
                if (c == ',') commas++;
                else if (c == ';') semicolons++;
                else if (c == '\t') tabs++;
            }
        }

        return semicolons > commas && semicolons >= tabs ? ';'
            : tabs > commas && tabs > semicolons ? '\t'
            : ',';
    }

    // Comillas dobles, comillas escapadas ("") y saltos de línea dentro de una celda.
    private static List<List<string>> ParseRows(string text)
    {
        var delimiter = DetectDelimiter(text);
        var rows = new List<List<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;

        void EndField()
        {
            row.Add(field.ToString());
            field.Clear();
        }

        void EndRow()
        {
            EndField();
            if (row.Count > 1 || row[0].Length > 0) rows.Add(row);
            row = new List<string>();
        }

        for (var i = 0; i < text.Length && rows.Count <= MaxRows + 1; i++)
        {
            var c = text[i];

            if (inQuotes)
            {
                if (c != '"') field.Append(c);
                else if (i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                else inQuotes = false;
            }
            else if (c == '"' && field.Length == 0) inQuotes = true;
            else if (c == delimiter) EndField();
            else if (c == '\r')
            {
                if (i + 1 < text.Length && text[i + 1] == '\n') i++;
                EndRow();
            }
            else if (c == '\n') EndRow();
            else field.Append(c);
        }

        if (field.Length > 0 || row.Count > 0) EndRow();
        return rows;
    }
}