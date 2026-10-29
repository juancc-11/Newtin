using System.Security.Cryptography;
using System.Text;

namespace Newtin.Server.Services;

public enum AccessDecision { Allowed, Denied, Blocked }

public sealed record AccessCheck(AccessDecision Decision, bool JustBlocked = false);

/// <summary>Comprueba el código de acceso en tiempo constante y bloquea temporalmente a quien falla demasiadas veces.</summary>
public sealed class AccessGuard
{
    private const int MaxFailures = 10;
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(5);

    private readonly byte[] _code;
    private readonly object _gate = new();
    private readonly Dictionary<string, (int Count, DateTimeOffset Start)> _failures = new();

    public AccessGuard(string code) => _code = Encoding.UTF8.GetBytes(code.Trim().ToUpperInvariant());

    public AccessCheck Check(string? presented, string clientKey, DateTimeOffset now)
    {
        lock (_gate)
        {
            foreach (var expired in _failures.Where(f => now - f.Value.Start > Window).Select(f => f.Key).ToList())
                _failures.Remove(expired);

            var failures = _failures.TryGetValue(clientKey, out var current) ? current : (Count: 0, Start: now);
            if (failures.Count >= MaxFailures)
                return new AccessCheck(AccessDecision.Blocked);

            var candidate = Encoding.UTF8.GetBytes((presented ?? "").Trim().ToUpperInvariant());
            if (CryptographicOperations.FixedTimeEquals(candidate, _code))
            {
                _failures.Remove(clientKey);
                return new AccessCheck(AccessDecision.Allowed);
            }

            failures = (failures.Count + 1, failures.Count == 0 ? now : failures.Start);
            _failures[clientKey] = failures;

            return failures.Count >= MaxFailures
                ? new AccessCheck(AccessDecision.Blocked, JustBlocked: true)
                : new AccessCheck(AccessDecision.Denied);
        }
    }
}