using Newtin.Core.Models;

namespace Newtin.Diagnostics.Connectivity;

public static class ProbeFormatter
{
    public static string Describe(TcpProbeStatus status) => status switch
    {
        TcpProbeStatus.Connected => "Conexión establecida",
        TcpProbeStatus.Refused => "Conexión rechazada",
        TcpProbeStatus.TimedOut => "Tiempo de espera agotado",
        TcpProbeStatus.HostUnreachable => "Equipo o ruta inalcanzable",
        TcpProbeStatus.NameNotResolved => "No se pudo resolver el nombre",
        TcpProbeStatus.Cancelled => "Prueba cancelada",
        TcpProbeStatus.InvalidTarget => "Destino inválido",
        _ => "Error de conexión"
    };

    public static InfoLevel LevelOf(TcpProbeStatus status) => status switch
    {
        TcpProbeStatus.Connected => InfoLevel.Success,
        TcpProbeStatus.Cancelled => InfoLevel.Normal,
        TcpProbeStatus.Refused => InfoLevel.Warning,
        _ => InfoLevel.Error
    };

    public static string Describe(PingStatus status) => status switch
    {
        PingStatus.Replied => "Respondió",
        PingStatus.TimedOut => "Sin respuesta",
        PingStatus.Unreachable => "Inalcanzable",
        PingStatus.NameNotResolved => "No se pudo resolver el nombre",
        PingStatus.Cancelled => "Prueba cancelada",
        PingStatus.InvalidTarget => "Destino inválido",
        _ => "Error"
    };

    public static InfoLevel LevelOf(PingStatus status) => status switch
    {
        PingStatus.Replied => InfoLevel.Success,
        PingStatus.Cancelled => InfoLevel.Normal,
        PingStatus.TimedOut => InfoLevel.Warning,
        _ => InfoLevel.Error
    };

    public static IReadOnlyList<InfoSection> ToSections(ConnectivityReport report)
    {
        var ping = report.Ping;
        var tcp = report.Tcp;

        var pingItems = new List<InfoItem>
        {
            new("Resultado", Describe(ping.Status),
                ping.Status == PingStatus.TimedOut
                    ? "Un fallo de ping no significa que el equipo esté apagado: el firewall de Windows suele bloquear ICMP."
                    : null,
                null, LevelOf(ping.Status)),
            new("Dirección consultada", ping.Address ?? report.Host)
        };
        if (ping.RoundTripMs is { } rtt)
            pingItems.Add(new InfoItem("Tiempo de respuesta", $"{rtt} ms"));
        pingItems.Add(new InfoItem("Detalle", ping.Message));

        var tcpItems = new List<InfoItem>
        {
            new("Resultado", Describe(tcp.Status), null, null, LevelOf(tcp.Status))
        };
        if (tcp.ElapsedMs is { } elapsed)
            tcpItems.Add(new InfoItem("Tiempo transcurrido", $"{elapsed} ms"));
        tcpItems.Add(new InfoItem("Detalle", tcp.Message));

        return
        [
            new InfoSection("Conclusión",
                [new InfoItem("Resumen", report.Conclusion.Text, null, null, report.Conclusion.Level)]),
            new InfoSection("Ping (ICMP)", pingItems),
            new InfoSection($"Conexión TCP al puerto {tcp.Port}", tcpItems)
        ];
    }

    public static InfoSection ToPortSection(string host, IReadOnlyList<PortCheckResult> results)
    {
        var items = results.Select(r =>
        {
            var time = r.Result.ElapsedMs is { } ms ? $" ({ms} ms)" : "";
            return new InfoItem(
                $"TCP {r.Target.Port} · {r.Target.Service}",
                Describe(r.Result.Status),
                r.Result.Message + time,
                null,
                LevelOf(r.Result.Status));
        }).ToList();

        return new InfoSection($"Resultados en {host}", items);
    }
}