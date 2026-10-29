using Newtin.Server.Models;
using Newtin.Server.Services;

namespace Newtin.Server.Persistence;

public static class BoardImporter
{
    private const string ImportAuthor = "Importado";
    private const int GridColumns = 10;
    private const double CellWidth = 230;
    private const double CellHeight = 170;

    private static string PrefixFor(ElementType? type) => type switch
    {
        ElementType.Note => "n",
        ElementType.Text => "t",
        _ => "s"
    };

    /// <summary>
    /// Aplica el plan a través de las mismas validaciones de siempre. En Merge, las categorías se reutilizan por nombre y
    /// el contenido nuevo sin posición se coloca debajo de lo que ya hay. Los Id siempre son nuevos.
    /// </summary>
    public static ImportOutcome Apply(WhiteboardStateService state, ImportPlan plan, ImportMode mode)
    {
        var warnings = new List<string>(plan.Warnings);
        if (mode == ImportMode.Replace) state.ClearAll();

        var before = state.Snapshot();
        var known = before.Categories.ToList();
        var map = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var newCategories = 0;

        foreach (var planned in plan.Categories)
        {
            var existing = known.FirstOrDefault(c => c.Name.Equals(planned.Name, StringComparison.OrdinalIgnoreCase));
            if (existing is not null)
            {
                map[planned.Key] = existing.Id;
                continue;
            }

            var result = state.CreateCategory(new CategoryInput(ImportHelpers.NewId("c"), planned.Name, planned.Color));
            if (result.Ok)
            {
                map[planned.Key] = result.Value!.Id;
                known.Add(result.Value);
                newCategories++;
            }
            else
            {
                map[planned.Key] = null;
                warnings.Add($"No se pudo crear la categoría «{planned.Name}»: {result.Error}");
            }
        }

        var startY = mode == ImportMode.Merge && before.Elements.Count > 0
            ? Math.Max(BoardLayoutCalculator.Margin, BoardLayoutCalculator.ContentBottom(before) + BoardLayoutCalculator.Margin)
            : BoardLayoutCalculator.Margin;

        int created = 0, noSpace = 0, rejected = 0, slot = 0;
        string? firstError = null;

        foreach (var planned in plan.Elements)
        {
            var input = planned.Input with
            {
                Id = ImportHelpers.NewId(PrefixFor(planned.Input.Type)),
                CategoryId = planned.CategoryKey is { } key && map.TryGetValue(key, out var mapped) ? mapped : null
            };

            if (planned.AutoPlace)
            {
                var x = BoardLayoutCalculator.Margin + slot % GridColumns * CellWidth;
                var y = startY + slot / GridColumns * CellHeight;

                if (y + BoardLayoutCalculator.NoteHeight > BoardLayoutCalculator.BoardHeight - BoardLayoutCalculator.Margin)
                {
                    noSpace++;
                    continue;
                }

                input = input with { X = x, Y = y };
                slot++;
            }

            var added = state.CreateElement(input, ImportAuthor);
            if (added.Ok) created++;
            else
            {
                rejected++;
                firstError ??= added.Error;
            }
        }

        if (noSpace > 0) warnings.Add($"{noSpace} nota(s) no cupieron en la pizarra y se omitieron.");
        if (rejected > 0) warnings.Add($"{rejected} elemento(s) no válidos se omitieron (por ejemplo: {firstError}).");

        state.MarkChanged();
        return new ImportOutcome(created, newCategories, noSpace + rejected, warnings.Take(10).ToList());
    }
}