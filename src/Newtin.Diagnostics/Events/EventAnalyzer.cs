using Newtin.Core.Models;

namespace Newtin.Diagnostics.Events;

public sealed record SourceStat(string Source, int Count, EventSeverity Worst, DateTimeOffset Last, int MostFrequentId);

public sealed record EventStats(int Critical, int Errors, int Warnings, IReadOnlyList<SourceStat> TopSources);

public static class EventAnalyzer
{
    public static EventStats Summarize(IReadOnlyList<EventSummary> events, int topSources = 5)
    {
        var top = events
            .GroupBy(e => e.Source, StringComparer.OrdinalIgnoreCase)
            .Select(group => new SourceStat(
                group.First().Source,
                group.Count(),
                group.Min(e => e.Severity),
                group.Max(e => e.Time),
                group.GroupBy(e => e.EventId).OrderByDescending(g => g.Count()).ThenBy(g => g.Key).First().Key))
            .OrderByDescending(stat => stat.Count)
            .ThenBy(stat => stat.Worst)
            .ThenBy(stat => stat.Source, StringComparer.OrdinalIgnoreCase)
            .Take(topSources)
            .ToList();

        return new EventStats(
            events.Count(e => e.Severity == EventSeverity.Critical),
            events.Count(e => e.Severity == EventSeverity.Error),
            events.Count(e => e.Severity == EventSeverity.Warning),
            top);
    }

    /// <summary>Primera línea de la descripción, sin caracteres de control y con un máximo de caracteres.</summary>
    public static string? Shorten(string? message, int maxLength = 300)
    {
        if (string.IsNullOrWhiteSpace(message)) return null;

        var line = message
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();
        if (line is null) return null;

        var clean = new string(line.Where(c => !char.IsControl(c)).ToArray());
        return clean.Length <= maxLength ? clean : clean[..(maxLength - 1)] + "…";
    }
}