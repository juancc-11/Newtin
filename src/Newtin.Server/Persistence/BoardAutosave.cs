using Newtin.Core.Interfaces;
using Newtin.Server.Hosting;

namespace Newtin.Server.Persistence;

public sealed record RestoreResult(bool Restored, string Message);

/// <summary>Guarda la pizarra poco después de cada cambio y la restaura al arrancar. Un archivo dañado se aparta, nunca se pisa.</summary>
public sealed class BoardAutosave : IDisposable
{
    public const long MaxFileBytes = 20L * 1024 * 1024;

    private readonly WhiteboardServerHost _host;
    private readonly string _path;
    private readonly ILogService _log;
    private readonly TimeSpan _delay;
    private readonly Timer _timer;
    private readonly object _writeGate = new();
    private int _scheduled;

    public BoardAutosave(WhiteboardServerHost host, string path, ILogService log, TimeSpan? delay = null)
    {
        _host = host;
        _path = path;
        _log = log;
        _delay = delay ?? TimeSpan.FromSeconds(3);
        _timer = new Timer(_ => Save(), null, Timeout.Infinite, Timeout.Infinite);
        _host.BoardChanged += OnBoardChanged;
    }

    public string Path => _path;

    // El primer cambio programa un guardado; los que lleguen en esa ventana se guardan juntos.
    private void OnBoardChanged(object? sender, EventArgs e)
    {
        if (Interlocked.Exchange(ref _scheduled, 1) == 0)
            _timer.Change(_delay, Timeout.InfiniteTimeSpan);
    }

    public void Flush() => Save();

    private void Save()
    {
        Interlocked.Exchange(ref _scheduled, 0);

        lock (_writeGate)
        {
            var data = NativeFormat.Write(_host.Snapshot(), DateTimeOffset.UtcNow);
            var result = AtomicFile.Write(_path, data, createDirectory: true);
            if (!result.Ok) _log.Warning($"No se pudo guardar la pizarra automáticamente: {result.Error}");
        }
    }

    public RestoreResult Restore()
    {
        if (!File.Exists(_path)) return new RestoreResult(false, "No hay una pizarra guardada.");

        var read = AtomicFile.Read(_path, MaxFileBytes);
        var plan = read.Data is null ? PlanResult.Fail(read.Error!) : NativeFormat.Read(read.Data);

        if (plan.Plan is null)
        {
            MoveAside();
            _log.Warning($"No se pudo restaurar la pizarra guardada: {plan.Error}");
            return new RestoreResult(false, plan.Error!);
        }

        var outcome = _host.ApplyImport(plan.Plan, ImportMode.Replace);
        _log.Info($"Pizarra restaurada: {outcome.Elements} elementos y {outcome.Categories} categorías.");
        return new RestoreResult(true, $"Se restauró la última pizarra ({outcome.Elements} elementos).");
    }

    private void MoveAside()
    {
        try
        {
            var aside = System.IO.Path.ChangeExtension(_path, null) + $".corrupt-{DateTime.Now:yyyyMMdd-HHmmss}.newtin";
            File.Move(_path, aside);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Warning("No se pudo apartar el archivo de autoguardado dañado.");
        }
    }

    public void Dispose()
    {
        _host.BoardChanged -= OnBoardChanged;
        _timer.Dispose();
    }
}