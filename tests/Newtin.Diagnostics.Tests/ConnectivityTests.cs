using System.Net;
using System.Net.Sockets;
using Newtin.Core.Models;
using Newtin.Diagnostics.Connectivity;

namespace Newtin.Diagnostics.Tests;

public class ConnectivityTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task Tcp_ConnectsToLocalListener()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var result = await new TcpPortProbe().ProbeAsync("127.0.0.1", port, Timeout, CancellationToken.None);

        Assert.Equal(TcpProbeStatus.Connected, result.Status);
        Assert.NotNull(result.ElapsedMs);
    }

    [Fact]
    public async Task Tcp_ReportsRefused_WhenNobodyListens()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        var result = await new TcpPortProbe().ProbeAsync("127.0.0.1", port, Timeout, CancellationToken.None);

        Assert.Equal(TcpProbeStatus.Refused, result.Status);
    }

    [Theory]
    [InlineData("", 445)]
    [InlineData("999.1.1.1", 445)]
    [InlineData("192.168.100.10", 0)]
    [InlineData("192.168.100.10", 70000)]
    public async Task Tcp_RejectsInvalidTargets(string host, int port)
    {
        var result = await new TcpPortProbe().ProbeAsync(host, port, Timeout, CancellationToken.None);
        Assert.Equal(TcpProbeStatus.InvalidTarget, result.Status);
    }

    [Fact]
    public async Task Tcp_ReportsCancellation()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await new TcpPortProbe().ProbeAsync("127.0.0.1", 9, Timeout, cts.Token);

        Assert.Equal(TcpProbeStatus.Cancelled, result.Status);
    }

    [Fact]
    public async Task Ping_LoopbackReplies()
    {
        var result = await new PingProbe().PingAsync("127.0.0.1", TimeSpan.FromSeconds(2), CancellationToken.None);
        Assert.Equal(PingStatus.Replied, result.Status);
    }

    [Fact]
    public async Task Ping_RejectsInvalidTarget()
    {
        var result = await new PingProbe().PingAsync("999.1.1.1", TimeSpan.FromSeconds(2), CancellationToken.None);
        Assert.Equal(PingStatus.InvalidTarget, result.Status);
    }

    [Theory]
    [InlineData(PingStatus.Replied, TcpProbeStatus.Connected, InfoLevel.Success)]
    [InlineData(PingStatus.TimedOut, TcpProbeStatus.Connected, InfoLevel.Success)]
    [InlineData(PingStatus.TimedOut, TcpProbeStatus.Refused, InfoLevel.Warning)]
    [InlineData(PingStatus.Replied, TcpProbeStatus.TimedOut, InfoLevel.Warning)]
    [InlineData(PingStatus.TimedOut, TcpProbeStatus.TimedOut, InfoLevel.Error)]
    [InlineData(PingStatus.NameNotResolved, TcpProbeStatus.NameNotResolved, InfoLevel.Error)]
    public void Interpret_CombinesPingAndTcp(PingStatus pingStatus, TcpProbeStatus tcpStatus, InfoLevel expected)
    {
        var ping = new PingResult("h", pingStatus, null, null, "");
        var tcp = new TcpProbeResult("h", 445, tcpStatus, null, "");

        Assert.Equal(expected, ConnectivityService.Interpret(ping, tcp).Level);
    }

    [Fact]
    public void Interpret_FailedPingWithOpenPort_ExplainsTheFirewall()
    {
        var ping = new PingResult("h", PingStatus.TimedOut, null, null, "");
        var tcp = new TcpProbeResult("h", 445, TcpProbeStatus.Connected, 3, "");

        Assert.Contains("firewall", ConnectivityService.Interpret(ping, tcp).Text);
    }
}