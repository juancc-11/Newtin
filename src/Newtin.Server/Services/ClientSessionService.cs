using System.Collections.Concurrent;
using Newtin.Server.Models;
using static Newtin.Server.Validation.WhiteboardLimits;

namespace Newtin.Server.Services;

public sealed class ClientSessionService
{
    private sealed class Session
    {
        public required string Name { get; set; }
        public long WindowStartMs;
        public int WindowCount;
    }

    private readonly ConcurrentDictionary<string, Session> _sessions = new();

    /// <summary>Se dispara cuando entra, sale o cambia de nombre un cliente. Puede ejecutarse en cualquier hilo.</summary>
    public event Action? Changed;

    public int Count => _sessions.Count;

    public IReadOnlyList<string> Names =>
        _sessions.Values.Select(s => s.Name).OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase).ToList();

    public ClientsInfo Info()
    {
        var names = Names;
        return new ClientsInfo(names.Count, names);
    }

    public void Join(string connectionId, string name)
    {
        _sessions.AddOrUpdate(
            connectionId,
            _ => new Session { Name = name },
            (_, existing) =>
            {
                existing.Name = name;
                return existing;
            });
        Changed?.Invoke();
    }

    public string? NameOf(string connectionId) =>
        _sessions.TryGetValue(connectionId, out var session) ? session.Name : null;

    public bool Remove(string connectionId)
    {
        var removed = _sessions.TryRemove(connectionId, out _);
        if (removed) Changed?.Invoke();
        return removed;
    }

    /// <summary>Limita cuántas operaciones por segundo puede enviar cada cliente.</summary>
    public bool TryConsume(string connectionId, long nowMs)
    {
        if (!_sessions.TryGetValue(connectionId, out var session)) return false;

        lock (session)
        {
            if (nowMs - session.WindowStartMs >= 1000)
            {
                session.WindowStartMs = nowMs;
                session.WindowCount = 0;
            }

            session.WindowCount++;
            return session.WindowCount <= MaxOperationsPerSecond;
        }
    }
}