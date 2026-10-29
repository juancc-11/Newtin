namespace Newtin.Diagnostics.Reports;

/// <summary>Guarda el último resultado de cada diagnóstico mientras Newtin está abierto.</summary>
public sealed class ReportSession
{
    private readonly object _gate = new();
    private readonly Dictionary<string, ModuleResult> _results = new();
    private readonly List<string> _order;

    public ReportSession(IReadOnlyList<string> order) => _order = order.ToList();

    /// <summary>Se dispara al registrar o limpiar resultados. Puede ejecutarse en cualquier hilo.</summary>
    public event Action? Changed;

    public void Record(ModuleResult result)
    {
        lock (_gate) _results[result.Key] = result;
        Changed?.Invoke();
    }

    public IReadOnlyList<ModuleResult> All()
    {
        lock (_gate)
            return _results.Values
                .OrderBy(r => _order.IndexOf(r.Key) is var index and >= 0 ? index : int.MaxValue)
                .ThenBy(r => r.Key, StringComparer.Ordinal)
                .ToList();
    }

    public void Clear()
    {
        lock (_gate) _results.Clear();
        Changed?.Invoke();
    }
}