using System.Net.NetworkInformation;
using System.Net.Sockets;
using Newtin.Core.Interfaces;
using Newtin.Core.Models;
using Newtin.Core.Validation;

namespace Newtin.Diagnostics.Connectivity;

public sealed class PingProbe : IPingProbe
{
    public async Task<PingResult> PingAsync(string host, TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (!HostValidator.TryNormalize(host, out var target, out var error))
            return new PingResult(host, PingStatus.InvalidTarget, null, null, error);

        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(target, timeout, null, null, cancellationToken);
            var address = reply.Address?.ToString();

            switch (reply.Status)
            {
                case IPStatus.Success:
                    return new PingResult(target, PingStatus.Replied, reply.RoundtripTime, address,
                        $"El equipo respondió al ping en {reply.RoundtripTime} ms.");

                case IPStatus.TimedOut:
                    return new PingResult(target, PingStatus.TimedOut, null, address,
                        "No hubo respuesta ICMP dentro del tiempo de espera.");

                case IPStatus.DestinationHostUnreachable
                    or IPStatus.DestinationNetworkUnreachable
                    or IPStatus.DestinationUnreachable:
                    return new PingResult(target, PingStatus.Unreachable, null, address,
                        "No existe una ruta hacia el equipo.");

                default:
                    return new PingResult(target, PingStatus.Error, null, address,
                        $"Windows informó: {reply.Status}.");
            }
        }
        catch (OperationCanceledException)
        {
            return new PingResult(target, PingStatus.Cancelled, null, null, "La prueba fue cancelada.");
        }
        catch (PingException ex) when (ex.InnerException is SocketException
                                       {
                                           SocketErrorCode: SocketError.HostNotFound or SocketError.TryAgain or SocketError.NoData
                                       })
        {
            return new PingResult(target, PingStatus.NameNotResolved, null, null,
                "No se pudo resolver el nombre del equipo.");
        }
        catch (PingException ex)
        {
            return new PingResult(target, PingStatus.Error, null, null, ex.InnerException?.Message ?? ex.Message);
        }
    }
}