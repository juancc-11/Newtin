using System.Diagnostics.Eventing.Reader;
using System.Runtime.Versioning;
using Newtin.Core.Interfaces;
using Newtin.Core.Models;
using System.Globalization;

namespace Newtin.Diagnostics.Events;

/// <summary>Lee el registro de eventos con el usuario actual. Solo lectura; los nombres de registro los fija Newtin.</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsEventLogReader : IEventLogReader
{
    public EventReadResult Read(
        string logName, TimeSpan window, bool includeWarnings, int maxEvents, int messagesFor,
        CancellationToken cancellationToken)
    {
        var xpath = BuildXPath(DateTimeOffset.UtcNow - window, includeWarnings);
        var query = new EventLogQuery(logName, PathType.LogName, xpath) { ReverseDirection = true };

        var events = new List<EventSummary>();
        try
        {
            using var reader = new EventLogReader(query);
            while (reader.ReadEvent() is { } record)
            {
                using (record)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    // Si ya hay el máximo y todavía queda otro evento, la lista se recorta.
                    if (events.Count >= maxEvents)
                        return new EventReadResult(events, true, null);

                    EventSeverity? severity = record.Level switch
                    {
                        1 => EventSeverity.Critical,
                        2 => EventSeverity.Error,
                        3 => EventSeverity.Warning,
                        _ => null
                    };

                    if (severity is null || record.TimeCreated is not { } created) continue;

                    events.Add(new EventSummary(
                        new DateTimeOffset(created),
                        logName,
                        severity.Value,
                        record.ProviderName ?? "(desconocido)",
                        record.Id,
                        events.Count < messagesFor ? TryDescribe(record) : null));
                }
            }
        }
        catch (UnauthorizedAccessException)
        {
            return new EventReadResult(events, false, "Windows no permite a tu usuario leer este registro.");
        }
        catch (EventLogNotFoundException)
        {
            return new EventReadResult(events, false, "Este registro no existe en el equipo.");
        }
        catch (EventLogException ex)
        {
            return new EventReadResult(events, false, $"No se pudo leer el registro: {ex.Message}");
        }

        return new EventReadResult(events, false, null);
    }

    private static string? TryDescribe(EventRecord record)
    {
        try
        {
            return record.FormatDescription();
        }
        catch (Exception ex) when (ex is EventLogException or InvalidOperationException or UnauthorizedAccessException)
        {
            return null;   // el programa que generó el evento no tiene su texto instalado
        }
    }

        /// <summary>
    /// Filtra por una fecha absoluta (UTC, ISO 8601). Se evita timeDiff porque solo admite hasta
    /// 2.147.483.647 ms (unos 24,8 días): con 30 días Windows rechazaba la consulta como inválida.
    /// </summary>
    public static string BuildXPath(DateTimeOffset since, bool includeWarnings)
    {
        var levels = includeWarnings ? "Level=1 or Level=2 or Level=3" : "Level=1 or Level=2";
        var timestamp = since.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
        return $"*[System[({levels}) and TimeCreated[@SystemTime >= '{timestamp}']]]";
    }
}