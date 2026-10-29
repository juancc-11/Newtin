using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Newtin.Core.Validation;

namespace Newtin.Diagnostics.Network;

public sealed record LanCandidate(string AdapterName, IPAddress Address, IPAddress Mask);

public static class LanAddressFinder
{
    private static readonly string[] VirtualHints = ["virtual", "vmware", "hyper-v", "vethernet", "vbox", "docker", "wsl"];

    /// <summary>Direcciones IPv4 de adaptadores activos. Los adaptadores físicos van primero.</summary>
    public static IReadOnlyList<LanCandidate> GetCandidates()
    {
        var physical = new List<LanCandidate>();
        var virtualOnes = new List<LanCandidate>();

        try
        {
            foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (adapter.OperationalStatus != OperationalStatus.Up
                    || adapter.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                    continue;

                var text = $"{adapter.Name} {adapter.Description}".ToLowerInvariant();
                var target = VirtualHints.Any(hint => text.Contains(hint)) ? virtualOnes : physical;

                foreach (var unicast in adapter.GetIPProperties().UnicastAddresses)
                {
                    if (unicast.Address.AddressFamily != AddressFamily.InterNetwork || IsLinkLocal(unicast.Address))
                        continue;

                    target.Add(new LanCandidate(adapter.Name, unicast.Address,
                        unicast.IPv4Mask ?? IPAddress.Parse("255.255.255.0")));
                }
            }
        }
        catch (NetworkInformationException)
        {
            // Sin información de red: la lista queda vacía y el llamador lo explica.
        }

        return physical.Concat(virtualOnes).ToList();
    }

    /// <summary>Prefiere la dirección que está en la misma red que el equipo remoto; si no hay, la primera.</summary>
    public static LanCandidate? Pick(IReadOnlyList<LanCandidate> candidates, string? remoteHost)
    {
        if (candidates.Count == 0) return null;

        if (HostValidator.TryParseIPv4(remoteHost, out var remote))
        {
            var sameNetwork = candidates.FirstOrDefault(c => SameNetwork(c.Address, remote, c.Mask));
            if (sameNetwork is not null) return sameNetwork;
        }

        return candidates[0];
    }

    public static bool SameNetwork(IPAddress a, IPAddress b, IPAddress mask)
    {
        var x = a.GetAddressBytes();
        var y = b.GetAddressBytes();
        var m = mask.GetAddressBytes();
        if (x.Length != 4 || y.Length != 4 || m.Length != 4) return false;

        for (var i = 0; i < 4; i++)
            if ((x[i] & m[i]) != (y[i] & m[i]))
                return false;

        return true;
    }

    private static bool IsLinkLocal(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        return bytes[0] == 169 && bytes[1] == 254;
    }
}