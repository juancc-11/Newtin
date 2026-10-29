using Newtin.Core.Models;

namespace Newtin.Core.Interfaces;

public interface IEventLogReader
{
    /// <summary>
    /// Devuelve los eventos más recientes primero. Solo carga la descripción de los primeros
    /// <paramref name="messagesFor"/>. Un fallo de lectura se informa en Error, sin lanzar excepción.
    /// </summary>
    EventReadResult Read(
        string logName,
        TimeSpan window,
        bool includeWarnings,
        int maxEvents,
        int messagesFor,
        CancellationToken cancellationToken);
}