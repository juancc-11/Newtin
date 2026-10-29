namespace Newtin.Core.Models;

/// <summary>Nivel de un evento de Windows. El valor más bajo es el más grave.</summary>
public enum EventSeverity { Critical = 0, Error = 1, Warning = 2 }

public sealed record EventSummary(
    DateTimeOffset Time,
    string Log,
    EventSeverity Severity,
    string Source,
    int EventId,
    string? Message);

public sealed record EventReadResult(IReadOnlyList<EventSummary> Events, bool Truncated, string? Error);

public sealed record EventQueryOptions(TimeSpan Window, bool IncludeWarnings)
{
    public int MaxEventsPerLog { get; init; } = 2000;
    public int MaxShown { get; init; } = 50;
}