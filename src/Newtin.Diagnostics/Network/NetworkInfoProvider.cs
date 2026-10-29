using System.Net.NetworkInformation;
using System.Net.Sockets;
using Newtin.Core.Models;

namespace Newtin.Diagnostics.Network;

public sealed class NetworkInfoProvider : InfoProviderBase
{
    private static readonly string[] VirtualHints =
        ["virtual", "vmware", "hyper-v", "vethernet", "vbox", "tap-", "wintun", "wireguard", "docker"];

    protected override IReadOnlyList<InfoSection> Collect(CancellationToken cancellationToken)
    {
        NetworkInterface[] adapters;
        try
        {
            adapters = NetworkInterface.GetAllNetworkInterfaces();
        }
        catch (NetworkInformationException ex)
        {
            return [new InfoSection("Red",
                [new InfoItem("Adaptadores", InfoItem.NotAvailable, $"No se pudo consultar: {ex.Message}")])];
        }

        var sections = new List<InfoSection>();
        foreach (var adapter in adapters
                     .Where(a => a.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                     .OrderByDescending(a => a.OperationalStatus == OperationalStatus.Up)
                     .ThenBy(a => a.Name))
        {
            cancellationToken.ThrowIfCancellationRequested();
            sections.Add(Describe(adapter));
        }

        if (sections.Count == 0)
            sections.Add(new InfoSection("Red",
                [new InfoItem("Adaptadores", InfoItem.NotAvailable, "No se detectaron adaptadores de red.")]));

        return sections;
    }

    private static InfoSection Describe(NetworkInterface adapter)
    {
        var items = new List<InfoItem>
        {
            InfoItem.Read("Descripción", () => adapter.Description),
            InfoItem.Read("Estado", () => TranslateStatus(adapter.OperationalStatus)),
            InfoItem.Read("Tipo", () => TranslateType(adapter)),
            InfoItem.Read("Clasificación", () => Classify(adapter),
                "Estimada a partir del tipo y la descripción del adaptador."),
            InfoItem.Read("Dirección MAC", () => FormatMac(adapter.GetPhysicalAddress()))
        };

        IPInterfaceProperties? props = null;
        string? error = null;
        try { props = adapter.GetIPProperties(); }
        catch (Exception ex) when (ex is NetworkInformationException or PlatformNotSupportedException)
        {
            error = $"No se pudo consultar: {ex.Message}";
        }

        if (props is null)
        {
            items.Add(new InfoItem("Configuración IP", InfoItem.NotAvailable, error));
            return new InfoSection(adapter.Name, items);
        }

        var ipv4 = props.UnicastAddresses.Where(u => u.Address.AddressFamily == AddressFamily.InterNetwork).ToList();
        var ipv6 = props.UnicastAddresses.Where(u => u.Address.AddressFamily == AddressFamily.InterNetworkV6).ToList();

        items.Add(InfoItem.Read("Dirección IPv4", () => Join(ipv4.Select(u => u.Address.ToString()))));
        items.Add(InfoItem.Read("Máscara de subred", () => Join(ipv4.Select(u => u.IPv4Mask.ToString()))));
        items.Add(InfoItem.Read("Dirección IPv6", () => Join(ipv6.Select(u => u.Address.ToString()))));
        items.Add(InfoItem.Read("Puerta de enlace", () => Join(props.GatewayAddresses.Select(g => g.Address.ToString()))));
        items.Add(InfoItem.Read("Servidores DNS", () => Join(props.DnsAddresses.Select(d => d.ToString()))));

        return new InfoSection(adapter.Name, items);
    }

    private static string? Join(IEnumerable<string> values)
    {
        var list = values.ToList();
        return list.Count == 0 ? null : string.Join(", ", list);
    }

    private static string? FormatMac(PhysicalAddress address)
    {
        var bytes = address.GetAddressBytes();
        return bytes.Length == 0 ? null : string.Join("-", bytes.Select(b => b.ToString("X2")));
    }

    private static string Classify(NetworkInterface adapter)
    {
        var text = $"{adapter.Name} {adapter.Description}".ToLowerInvariant();
        if (adapter.NetworkInterfaceType == NetworkInterfaceType.Tunnel || VirtualHints.Any(text.Contains))
            return "Virtual";

        return adapter.NetworkInterfaceType is NetworkInterfaceType.Ethernet
            or NetworkInterfaceType.GigabitEthernet
            or NetworkInterfaceType.FastEthernetT
            or NetworkInterfaceType.Wireless80211
            ? "Física"
            : "No determinado";
    }

    internal static string TranslateStatus(OperationalStatus status) => status switch
    {
        OperationalStatus.Up => "Conectado",
        OperationalStatus.Down => "Desconectado",
        OperationalStatus.Testing => "En prueba",
        OperationalStatus.Dormant => "En espera",
        OperationalStatus.NotPresent => "No presente",
        OperationalStatus.LowerLayerDown => "Capa inferior inactiva",
        _ => "Desconocido"
    };

    internal static string TranslateType(NetworkInterface adapter) => adapter.NetworkInterfaceType switch
    {
        NetworkInterfaceType.Ethernet or NetworkInterfaceType.GigabitEthernet
            or NetworkInterfaceType.FastEthernetT or NetworkInterfaceType.FastEthernetFx => "Ethernet",
        NetworkInterfaceType.Wireless80211 => "Wi-Fi",
        NetworkInterfaceType.Tunnel => "Túnel",
        var other => other.ToString()
    };
}