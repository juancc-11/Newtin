using System.Diagnostics;
using System.Net.Sockets;
using Newtin.Core.Interfaces;
using Newtin.Core.Models;
using Newtin.Core.Validation;

namespace Newtin.Diagnostics.Connectivity;

/// <summary>Una única conexión TCP a un destino elegido por el usuario. No escanea redes ni evade firewalls.</summary>
public sealed class TcpPortProbe : ITcpPortProbe
{
    public async Task<TcpProbeResult> ProbeAsync(string host, int port, TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (!HostValidator.TryNormalize(host, out var target, out var error))
            return new TcpProbeResult(host, port, TcpProbeStatus.InvalidTarget, null, error);

        if (!PortValidator.IsValid(port))
            return new TcpProbeResult(target, port, TcpProbeStatus.InvalidTarget, null,
                "El puerto debe estar entre 1 y 65535.");

        var stopwatch = Stopwatch.StartNew();
        using var client = new TcpClient();
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);

        try
        {
            await client.ConnectAsync(target, port, timeoutSource.Token);
            return new TcpProbeResult(target, port, TcpProbeStatus.Connected, stopwatch.ElapsedMilliseconds,
                "Se estableció la conexión TCP.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new TcpProbeResult(target, port, TcpProbeStatus.Cancelled, stopwatch.ElapsedMilliseconds,
                "La prueba fue cancelada.");
        }
        catch (OperationCanceledException)
        {
            return new TcpProbeResult(target, port, TcpProbeStatus.TimedOut, stopwatch.ElapsedMilliseconds,
                $"No hubo respuesta en {timeout.TotalSeconds:0.#} segundos. Un firewall puede estar descartando el tráfico, " +
                "o el equipo está apagado o fuera de la red.");
        }
        catch (SocketException ex)
        {
            var elapsed = stopwatch.ElapsedMilliseconds;
            return ex.SocketErrorCode switch
            {
                SocketError.ConnectionRefused => new TcpProbeResult(target, port, TcpProbeStatus.Refused, elapsed,
                    "El equipo respondió, pero no hay ningún servicio escuchando en ese puerto o un firewall lo rechazó."),
                SocketError.HostNotFound or SocketError.TryAgain or SocketError.NoData =>
                    new TcpProbeResult(target, port, TcpProbeStatus.NameNotResolved, elapsed,
                        "No se pudo resolver el nombre del equipo."),
                SocketError.NetworkUnreachable or SocketError.HostUnreachable =>
                    new TcpProbeResult(target, port, TcpProbeStatus.HostUnreachable, elapsed,
                        "No existe una ruta hacia el equipo. Revisa la dirección IP, la máscara y el cable."),
                SocketError.TimedOut => new TcpProbeResult(target, port, TcpProbeStatus.TimedOut, elapsed,
                    "La conexión excedió el tiempo de espera del sistema."),
                _ => new TcpProbeResult(target, port, TcpProbeStatus.Error, elapsed,
                    $"Error de conexión ({ex.SocketErrorCode}): {ex.Message}")
            };
        }
    }
}