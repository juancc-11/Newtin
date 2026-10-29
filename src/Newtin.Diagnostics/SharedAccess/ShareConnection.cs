using Newtin.Core.Interfaces;
using Newtin.Core.Models;

namespace Newtin.Diagnostics.SharedAccess;

/// <summary>Una conexión abierta para una prueba. Al liberarla se cierra solo la conexión temporal que Newtin creó.</summary>
public sealed class ShareConnection : IDisposable
{
    private readonly ISmbNative _native;
    private readonly bool _created;
    private readonly ILogService _log;
    private readonly string _disconnectPath;
    private bool _disposed;

    internal ShareConnection(
        ShareAccessResult access,
        ISmbNative native,
        bool created,
        ILogService log,
        string? disconnectPath = null)
    {
        Access = access;
        _native = native;
        _created = created;
        _log = log;
        _disconnectPath = disconnectPath ?? access.UncPath;
    }

    public ShareAccessResult Access { get; }
    public string UncPath => Access.UncPath;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (!_created) return;

        var code = _native.Disconnect(_disconnectPath);
        if (code != 0)
            _log.Info($"La conexión temporal con {_disconnectPath} no se cerró (código {code}); Windows la liberará cuando deje de estar en uso.");
    }
}