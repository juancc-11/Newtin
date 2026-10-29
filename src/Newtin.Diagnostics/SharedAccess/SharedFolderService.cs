using Newtin.Core.Interfaces;
using Newtin.Core.Models;
using Newtin.Core.Validation;
using static Newtin.Core.Models.ShareAccessStatus;

namespace Newtin.Diagnostics.SharedAccess;

public sealed class SharedFolderService
{
    private const int SmbPort = 445;

    private readonly ITcpPortProbe _tcp;
    private readonly ISmbNative _native;
    private readonly ILogService _log;
    private readonly Func<string, CancellationToken, Task> _listDirectory;

    public SharedFolderService(
        ITcpPortProbe tcp,
        ISmbNative native,
        ILogService log,
        Func<string, CancellationToken, Task>? listDirectory = null)
    {
        _tcp = tcp;
        _native = native;
        _log = log;
        _listDirectory = listDirectory ?? ListRealDirectoryAsync;
    }

    public async Task<ShareAccessResult> CheckAsync(ShareRequest request, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var connection = await ConnectAsync(request, timeout, cancellationToken);
        return connection.Access;
    }

    /// <summary>Abre (si hace falta) la conexión y comprueba que se pueda listar la carpeta. Liberar el resultado cierra la conexión temporal.</summary>
    public async Task<ShareConnection> ConnectAsync(ShareRequest request, TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (!UncPathBuilder.TryBuild(request.Host, request.Share, out var unc, out var error))
            return Finish(request, "", InvalidInput, error, created: false);

        if (request.Mode == AuthMode.Credentials
            && (string.IsNullOrWhiteSpace(request.User) || request.Password is null || request.Password.Length == 0))
            return Finish(request, unc, InvalidInput,
                "Escribe el usuario y la contraseña para usar credenciales proporcionadas.", created: false);

        HostValidator.TryNormalize(request.Host, out var host, out _);

        // Primero se comprueba el puerto 445: así un equipo apagado falla rápido, sin esperar el tiempo de Windows.
        var probe = await _tcp.ProbeAsync(host, SmbPort, timeout, cancellationToken);
        if (probe.Status == TcpProbeStatus.Cancelled)
            return Finish(request, unc, Cancelled, "La prueba fue cancelada.", created: false);

        if (probe.Status != TcpProbeStatus.Connected)
            return Finish(request, unc, HostUnreachable,
                $"No se pudo llegar al servicio de carpetas compartidas (TCP 445) en {host}. {probe.Message} " +
                "Usa el diagnóstico de conectividad para revisar la red y el firewall.",
                created: false);

        var created = false;
        var disconnectPath = unc;
        if (request.Mode == AuthMode.Credentials)
        {
            var authenticationPath = string.IsNullOrWhiteSpace(request.Share) ? $@"\\{host}\IPC$" : unc;
            // No se cancela a mitad de la conexión: si se creara después, quedaría una sesión sin cerrar.
            var code = await Task.Run(() => _native.Connect(authenticationPath, request.User!, request.Password!));
            if (code != 0)
            {
                var (failedStatus, failedMessage) = SmbErrorMapper.FromWin32(code, host);
                return Finish(request, unc, failedStatus, failedMessage, created: false);
            }

            created = true;
            disconnectPath = authenticationPath;
            if (cancellationToken.IsCancellationRequested)
                return Finish(request, unc, Cancelled, "La prueba fue cancelada.", created, disconnectPath);
        }

        var (status, message) = await ProbeFolderAsync(unc, host, request.Mode, cancellationToken);
        return Finish(request, unc, status, message, created, disconnectPath);
    }

    private async Task<(ShareAccessStatus Status, string Message)> ProbeFolderAsync(
        string unc, string host, AuthMode mode, CancellationToken cancellationToken)
    {
        try
        {
            await _listDirectory(unc, cancellationToken);
            return (Accessible, "Se pudo conectar y listar el contenido de la carpeta compartida.");
        }
        catch (OperationCanceledException)
        {
            return (Cancelled, "La prueba fue cancelada.");
        }
        catch (UnauthorizedAccessException)
        {
            return mode == AuthMode.Credentials
                ? (InsufficientPermissions,
                    "Las credenciales fueron aceptadas, pero este usuario no tiene permiso para ver el contenido de la carpeta compartida. " +
                    "El administrador del otro equipo debe concederle acceso en los permisos del recurso compartido y en la pestaña de seguridad.")
                : (AccessDenied,
                    "Windows rechazó el acceso con tu sesión actual. Prueba con la modalidad «Credenciales proporcionadas».");
        }
        catch (DirectoryNotFoundException)
        {
            return (ShareNotFound,
                "El equipo respondió, pero no se encontró esa carpeta compartida. Revisa el nombre exacto de la carpeta.");
        }
        catch (IOException ex)
        {
            var code = SmbErrorMapper.Win32CodeFrom(ex);
            if (code is null)
                return (Error, $"No se pudo acceder a la carpeta: {ex.Message}");

            var (status, message) = SmbErrorMapper.FromWin32(code.Value, host);
            if (mode == AuthMode.CurrentSession && (status is BadCredentials or AccessDenied))
                message += " Prueba con la modalidad «Credenciales proporcionadas».";
            return (status, message);
        }
    }

    private ShareConnection Finish(
        ShareRequest request,
        string unc,
        ShareAccessStatus status,
        string message,
        bool created,
        string? disconnectPath = null)
    {
        _log.Info($"Acceso a {(unc.Length > 0 ? unc : "(ruta no válida)")} (modo {request.Mode}): {status}.");
        return new ShareConnection(new ShareAccessResult(unc, status, message), _native, created, _log, disconnectPath);
    }

    private static Task ListRealDirectoryAsync(string path, CancellationToken cancellationToken) =>
        IsServerRoot(path)
            ? Task.Run(() => { _ = WindowsRemoteShareEnumerator.ListShares(path); }, cancellationToken)
                .WaitAsync(cancellationToken)
            : Task.Run(() => { _ = Directory.EnumerateFileSystemEntries(path).FirstOrDefault(); }, cancellationToken)
                .WaitAsync(cancellationToken);

    private static bool IsServerRoot(string path)
    {
        var trimmedPath = path.TrimEnd('\\');
        return trimmedPath.StartsWith(@"\\", StringComparison.Ordinal) &&
               trimmedPath.IndexOf('\\', 2) < 0;
    }
}