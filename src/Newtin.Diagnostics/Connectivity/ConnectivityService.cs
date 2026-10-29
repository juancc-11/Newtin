using Newtin.Core.Interfaces;
using Newtin.Core.Models;

namespace Newtin.Diagnostics.Connectivity;

public sealed class ConnectivityService
{
    private readonly IPingProbe _ping;
    private readonly ITcpPortProbe _tcp;
    private readonly ILogService _log;

    public ConnectivityService(IPingProbe ping, ITcpPortProbe tcp, ILogService log)
    {
        _ping = ping;
        _tcp = tcp;
        _log = log;
    }

    public async Task<ConnectivityReport> CheckAsync(string host, int port, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var pingTask = _ping.PingAsync(host, timeout, cancellationToken);
        var tcpTask = _tcp.ProbeAsync(host, port, timeout, cancellationToken);

        var ping = await pingTask;
        var tcp = await tcpTask;

        _log.Info($"Conectividad con {host}: ping={ping.Status}, TCP {port}={tcp.Status}.");
        return new ConnectivityReport(host, ping, tcp, Interpret(ping, tcp));
    }

    public async Task<IReadOnlyList<PortCheckResult>> CheckPortsAsync(
        string host, IReadOnlyList<PortTarget> targets, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var results = await Task.WhenAll(targets.Select(async target =>
            new PortCheckResult(target, await _tcp.ProbeAsync(host, target.Port, timeout, cancellationToken))));

        _log.Info($"Diagnóstico de puertos en {host}: " +
                  string.Join(", ", results.Select(r => $"{r.Target.Port}={r.Result.Status}")) + ".");
        return results;
    }

    /// <summary>Combina ping y TCP en una conclusión. El ping fallido por sí solo nunca se interpreta como "equipo apagado".</summary>
    public static Interpretation Interpret(PingResult ping, TcpProbeResult tcp)
    {
        var pingOk = ping.Status == PingStatus.Replied;

        return tcp.Status switch
        {
            TcpProbeStatus.InvalidTarget => new Interpretation(tcp.Message, InfoLevel.Error),
            TcpProbeStatus.Cancelled => new Interpretation("La prueba fue cancelada.", InfoLevel.Normal),
            TcpProbeStatus.NameNotResolved => new Interpretation(
                "No se pudo encontrar el equipo por su nombre. Prueba con su dirección IP.", InfoLevel.Error),

            TcpProbeStatus.Connected when pingOk => new Interpretation(
                $"El equipo responde al ping y acepta conexiones en el puerto {tcp.Port}.", InfoLevel.Success),
            TcpProbeStatus.Connected => new Interpretation(
                $"El equipo acepta conexiones en el puerto {tcp.Port}. Que el ping no responda es normal: " +
                "el firewall de Windows lo bloquea por defecto.", InfoLevel.Success),

            TcpProbeStatus.Refused => new Interpretation(
                $"El equipo respondió, pero el puerto {tcp.Port} rechazó la conexión: el servicio no está activo " +
                "o un firewall lo rechaza.", InfoLevel.Warning),

            TcpProbeStatus.TimedOut when pingOk => new Interpretation(
                $"El equipo responde al ping, pero el puerto {tcp.Port} no respondió: lo más probable es que un " +
                "firewall lo bloquee o que el servicio no esté activo.", InfoLevel.Warning),

            TcpProbeStatus.TimedOut or TcpProbeStatus.HostUnreachable => new Interpretation(
                "No hubo respuesta del equipo. Puede estar apagado, desconectado, tener otra dirección IP o bloquear " +
                "todo el tráfico con su firewall. Revisa el cable, la IP y que ambos equipos estén en la misma red.",
                InfoLevel.Error),

            _ => new Interpretation(
                "El resultado no permite concluir si el equipo está disponible. Revisa el detalle de cada prueba.",
                InfoLevel.Warning)
        };
    }
}