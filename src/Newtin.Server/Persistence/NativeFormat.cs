using System.Text.Json;
using System.Text.Json.Serialization;
using Newtin.Server.Models;
using Newtin.Server.Validation;

namespace Newtin.Server.Persistence;

public sealed class NativeDocument
{
    public string? Format { get; set; }
    public int Version { get; set; }
    public DateTimeOffset? ExportedAt { get; set; }
    public string? Application { get; set; }
    public List<NativeCategory>? Categories { get; set; }
    public List<NativeElement>? Elements { get; set; }
}

public sealed class NativeCategory
{
    public string? Id { get; set; }
    public string? Name { get; set; }
    public string? Color { get; set; }
}

public sealed class NativeElement
{
    public string? Id { get; set; }
    public string? Type { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public string? Text { get; set; }
    public List<PointD>? Points { get; set; }
    public string? Color { get; set; }
    public double? Width { get; set; }
    public string? CategoryId { get; set; }
    public string? Author { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public DateTimeOffset? ModifiedAt { get; set; }
    public long? Order { get; set; }
}

/// <summary>Formato propio de Newtin (.newtin): JSON con versión. Al leerlo nada se da por bueno: pasa por las validaciones del servidor.</summary>
public static class NativeFormat
{
    public const string FormatName = "newtin-whiteboard";
    public const int CurrentVersion = 1;

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly JsonSerializerOptions ReadOptions = new() { PropertyNameCaseInsensitive = true };

    public static byte[] Write(WhiteboardSnapshot snapshot, DateTimeOffset now)
    {
        var document = new NativeDocument
        {
            Format = FormatName,
            Version = CurrentVersion,
            ExportedAt = now,
            Application = "Newtin",
            Categories = snapshot.Categories
                .Select(c => new NativeCategory { Id = c.Id, Name = c.Name, Color = c.Color })
                .ToList(),
            Elements = snapshot.Elements.Select(e => new NativeElement
            {
                Id = e.Id,
                Type = e.Type.ToString().ToLowerInvariant(),
                X = e.X,
                Y = e.Y,
                Text = e.Text,
                Points = e.Points?.ToList(),
                Color = e.Color,
                Width = e.Width,
                CategoryId = e.CategoryId,
                Author = e.Author,
                CreatedAt = e.CreatedAt,
                ModifiedAt = e.ModifiedAt,
                Order = e.Order
            }).ToList()
        };

        return JsonSerializer.SerializeToUtf8Bytes(document, WriteOptions);
    }

    public static PlanResult Read(byte[] data)
    {
        NativeDocument? document;
        try
        {
            document = JsonSerializer.Deserialize<NativeDocument>(TextDecoder.StripUtf8Bom(data), ReadOptions);
        }
        catch (JsonException)
        {
            return PlanResult.Fail("El archivo no es una pizarra de Newtin válida: no se pudo leer su contenido.");
        }

        if (document is null || document.Format != FormatName)
            return PlanResult.Fail("El archivo no es una pizarra de Newtin.");

        if (document.Version < 1 || document.Version > CurrentVersion)
            return PlanResult.Fail($"El archivo es de la versión {document.Version}, que esta versión de Newtin no puede abrir.");

        var warnings = new List<string>();
        var categories = new List<PlanCategory>();
        var known = new HashSet<string>(StringComparer.Ordinal);
        var invalidCategories = 0;

        foreach (var entry in document.Categories ?? [])
        {
            var key = entry.Id?.Trim();
            if (string.IsNullOrEmpty(key) || known.Contains(key)
                || !WhiteboardValidator.TryNormalizeCategoryName(entry.Name, out var name, out _))
            {
                invalidCategories++;
                continue;
            }

            known.Add(key);
            categories.Add(new PlanCategory(key, name,
                ImportHelpers.ValidColor(entry.Color) ?? ImportHelpers.PaletteColor(categories.Count)));
        }

        if (invalidCategories > 0)
            warnings.Add($"{invalidCategories} categoría(s) no válidas o repetidas se omitieron.");

        var ordered = (document.Elements ?? [])
            .Select((entry, index) => (entry, index))
            .OrderBy(item => item.entry.Order ?? item.index)
            .ThenBy(item => item.index)
            .Select(item => item.entry);

        var elements = new List<PlanElement>();
        var unknownTypes = 0;

        foreach (var entry in ordered)
        {
            if (!Enum.TryParse<ElementType>(entry.Type, ignoreCase: true, out var type) || !Enum.IsDefined(type))
            {
                unknownTypes++;
                continue;
            }

            var categoryKey = entry.CategoryId?.Trim();
            elements.Add(new PlanElement(
                new ElementInput(null, type, entry.X, entry.Y, entry.Text, entry.Points,
                    ImportHelpers.ValidColor(entry.Color), entry.Width, null),
                categoryKey is not null && known.Contains(categoryKey) ? categoryKey : null,
                AutoPlace: false));
        }

        if (unknownTypes > 0)
            warnings.Add($"{unknownTypes} elemento(s) de un tipo desconocido se omitieron.");

        return PlanResult.Ok(new ImportPlan(categories, elements, warnings));
    }
}