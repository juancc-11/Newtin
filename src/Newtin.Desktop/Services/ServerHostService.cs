using System.Net;
using System;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using Newtin.Core.Interfaces;
using Newtin.Core.Validation;
using Newtin.Diagnostics.Network;
using Newtin.Server.Hosting;

namespace Newtin.Desktop.Services;

/// <summary>Une la configuración de Newtin con el servidor y avisa a la interfaz en su propio hilo.</summary>
public sealed class ServerHostService
{
    private readonly ISettingsService _settings;
    private readonly ILogService _log;
    private readonly WhiteboardServerHost _host;
    private readonly Dispatcher _dispatcher;

    public ServerHostService(ISettingsService settings, ILogService log)
    {
        _settings = settings;
        _log = log;
        _dispatcher = Application.Current.Dispatcher;
        _host = new WhiteboardServerHost(log);
        _host.Changed += (_, _) => _dispatcher.BeginInvoke(new Action(() => Changed?.Invoke(this, EventArgs.Empty)));
    }

    /// <summary>Siempre se dispara en el hilo de la interfaz.</summary>
    public event EventHandler? Changed;

    public ServerState State => _host.State;
    public string? AccessCode => _host.AccessCode;
    public string? LocalUrl => _host.LocalUrl;
    public string? LanUrl => _host.LanUrl;
    public int ClientCount => _host.ClientCount;
    public IReadOnlyList<string> ClientNames => _host.ClientNames;
    public int ElementCount => _host.ElementCount;
    public Task StopAsync() => _host.StopAsync();
    public WhiteboardServerHost Host => _host;

    public async Task<(bool Ok, string Message)> ToggleAsync()
    {
        if (State == ServerState.Running)
        {
            await StopAsync();
            return (true, "Servidor detenido.");
        }

        if (State != ServerState.Stopped)
            return (false, "El servidor está cambiando de estado; espera un momento.");

        return await StartAsync();
    }

    public async Task<(bool Ok, string Message)> StartAsync()
    {
        var settings = _settings.Current;
        var port = settings.WhiteboardServer.Port;
        IPAddress? lan = null;

        if (settings.WhiteboardServer.ShareOnLan)
        {
            var candidates = LanAddressFinder.GetCandidates();
            var configured = settings.WhiteboardServer.LanAddress;

            if (!string.IsNullOrWhiteSpace(configured))
            {
                if (!HostValidator.TryParseIPv4(configured, out var chosen))
                    return (false, "La dirección para la red local no es válida. Corrígela en Configuración.");

                if (!candidates.Any(c => c.Address.Equals(chosen)))
                    return (false, $"La dirección {chosen} no pertenece a ningún adaptador activo de este equipo.");

                lan = chosen;
            }
            else
            {
                lan = LanAddressFinder.Pick(candidates, settings.Network.RemoteHost)?.Address;
                if (lan is null)
                    return (false, "No se encontró un adaptador de red activo para compartir en la red local.");
            }
        }

        var addresses = lan is null ? new[] { IPAddress.Loopback } : new[] { IPAddress.Loopback, lan };
        foreach (var address in addresses)
        {
            if (!PortAvailability.IsFree(address, port))
                return (false, $"El puerto {port} ya está en uso en {address}. Cambia el puerto en Configuración o cierra el programa que lo usa.");
        }

        var webRoot = Path.Combine(AppContext.BaseDirectory, "wwwroot");
        if (!Directory.Exists(webRoot))
            return (false, "No se encontró la carpeta wwwroot con los archivos de la pizarra.");

        try
        {
            await _host.StartAsync(new ServerOptions(port, lan, webRoot));
        }
        catch (Exception ex)
        {
            _log.Error("No se pudo iniciar el servidor de la pizarra.", ex);
            return (false, $"No se pudo iniciar el servidor: {ex.Message}");
        }

        return (true, lan is null
            ? "Servidor iniciado solo en este equipo. Para que otro equipo se conecte, activa la red local en Configuración."
            : "Servidor iniciado. Comparte la dirección y el código con el otro equipo.");
    }
}