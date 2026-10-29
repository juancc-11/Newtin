using Newtin.Core.Models;

namespace Newtin.Diagnostics.Events;

public static class EventFormatter
{
    public static string PeriodLabel(TimeSpan window) =>
        window.TotalHours <= 24 ? "Últimas 24 horas" : $"Últimos {(int)window.TotalDays} días";

    public static string SeverityText(EventSeverity severity) => severity switch
    {
        EventSeverity.Critical => "Crítico",
        EventSeverity.Error => "Error",
        _ => "Advertencia"
    };

    public static InfoLevel LevelOf(EventSeverity severity) =>
        severity == EventSeverity.Warning ? InfoLevel.Warning : InfoLevel.Error;

    private static string LogName(string log) => log switch
    {
        "System" => "Sistema",
        "Application" => "Aplicación",
        _ => log
    };

    public static Interpretation Conclude(EventStats stats, bool incomplete)
    {
        var (text, level) =
            stats.Critical > 0
                ? ("Hay eventos críticos en el período. Revisa las fuentes con más eventos y los eventos más recientes.",
                    InfoLevel.Error)
            : stats.Errors > 0
                ? ("Se registraron errores en el período. Un error aislado no implica un problema actual: " +
                   "fíjate en los que se repiten (fuentes con más eventos).", InfoLevel.Warning)
            : stats.Warnings > 0
                ? ("No hay errores, pero sí advertencias. Suelen ser informativas; revisa si se repiten.",
                    InfoLevel.Normal)
                : ("No se encontraron errores en el período consultado.", InfoLevel.Success);

        if (incomplete)
        {
            if (level == InfoLevel.Success)
            {
                text = "La consulta fue incompleta: algún registro no se pudo leer, " +
                       "así que no se puede asegurar que no haya errores.";
                level = InfoLevel.Warning;
            }
            else
            {
                text += " La consulta fue incompleta: algún registro no se pudo leer.";
            }
        }

        return new Interpretation(text, level);
    }

    public static IReadOnlyList<InfoSection> ToSections(
        IReadOnlyList<EventSummary> events,
        bool truncated,
        IReadOnlyList<InfoItem> problems,
        EventQueryOptions options)
    {
        var ordered = events.OrderByDescending(e => e.Time).ToList();
        var stats = EventAnalyzer.Summarize(ordered);
        var conclusion = Conclude(stats, problems.Count > 0);

        var summary = new List<InfoItem>
        {
            new("Período", PeriodLabel(options.Window)),
            new("Críticos", stats.Critical.ToString(), null, null, stats.Critical > 0 ? InfoLevel.Error : InfoLevel.Success),
            new("Errores", stats.Errors.ToString(), null, null, stats.Errors > 0 ? InfoLevel.Error : InfoLevel.Success),
            options.IncludeWarnings
                ? new InfoItem("Advertencias", stats.Warnings.ToString(), null, null,
                    stats.Warnings > 0 ? InfoLevel.Warning : InfoLevel.Success)
                : new InfoItem("Advertencias", "No incluidas", "Activa «Incluir advertencias» para verlas."),
            new("Conclusión", conclusion.Text,
                truncated ? "Se analizaron solo los eventos más recientes de cada registro; puede haber más." : null,
                null, conclusion.Level)
        };
        summary.AddRange(problems);

        var sections = new List<InfoSection> { new("Eventos de Windows (Sistema y Aplicación)", summary) };

        if (stats.TopSources.Count > 0)
        {
            sections.Add(new InfoSection("Fuentes con más eventos",
                stats.TopSources.Select(source => new InfoItem(
                    source.Source,
                    source.Count == 1 ? "1 evento" : $"{source.Count} eventos",
                    $"Peor nivel: {SeverityText(source.Worst)} · último: {source.Last:dd/MM HH:mm} · " +
                    $"ID más frecuente: {source.MostFrequentId}",
                    null,
                    LevelOf(source.Worst))).ToList()));
        }

        var shown = ordered.Take(options.MaxShown).Select(e => new InfoItem(
            $"{e.Time:dd/MM HH:mm:ss} · {LogName(e.Log)}",
            $"{SeverityText(e.Severity)} — {e.Source} (ID {e.EventId})",
            EventAnalyzer.Shorten(e.Message) ?? "(descripción no disponible)",
            null,
            LevelOf(e.Severity))).ToList();

        if (shown.Count == 0)
            shown.Add(new InfoItem("Eventos", "Ninguno en el período", null, null, InfoLevel.Success));

        sections.Add(new InfoSection($"Eventos más recientes (hasta {options.MaxShown})", shown, IncludeInReport: false));
        return sections;
    }
}