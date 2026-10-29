using Newtin.Core.Models;

namespace Newtin.Core.Interfaces;

public interface IPingProbe
{
    Task<PingResult> PingAsync(string host, TimeSpan timeout, CancellationToken cancellationToken);
}

public interface ITcpPortProbe
{
    Task<TcpProbeResult> ProbeAsync(string host, int port, TimeSpan timeout, CancellationToken cancellationToken);
}