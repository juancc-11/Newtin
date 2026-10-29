using System.Net;
using Newtin.Diagnostics.Network;

namespace Newtin.Diagnostics.Tests;

public class LanAddressFinderTests
{
    private static LanCandidate Candidate(string name, string address) =>
        new(name, IPAddress.Parse(address), IPAddress.Parse("255.255.255.0"));

    [Fact]
    public void Pick_PrefersTheAdapterOnTheRemoteHostsNetwork()
    {
        var candidates = new[] { Candidate("Wi-Fi", "192.168.1.20"), Candidate("Ethernet", "192.168.100.1") };

        var picked = LanAddressFinder.Pick(candidates, "192.168.100.10");

        Assert.Equal("Ethernet", picked!.AdapterName);
    }

    [Fact]
    public void Pick_FallsBackToTheFirstCandidate()
    {
        var candidates = new[] { Candidate("Wi-Fi", "192.168.1.20"), Candidate("Ethernet", "192.168.100.1") };

        Assert.Equal("Wi-Fi", LanAddressFinder.Pick(candidates, "10.9.9.9")!.AdapterName);
        Assert.Equal("Wi-Fi", LanAddressFinder.Pick(candidates, null)!.AdapterName);
    }

    [Fact]
    public void Pick_ReturnsNull_WhenThereAreNoCandidates() =>
        Assert.Null(LanAddressFinder.Pick([], "192.168.100.10"));

    [Fact]
    public void PortAvailability_DetectsAPortInUse()
    {
        using var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        Assert.False(PortAvailability.IsFree(IPAddress.Loopback, port));

        listener.Stop();
        Assert.True(PortAvailability.IsFree(IPAddress.Loopback, port));
    }
}