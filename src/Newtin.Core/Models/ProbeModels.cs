namespace Newtin.Core.Models;

public enum TcpProbeStatus { Connected, Refused, TimedOut, HostUnreachable, NameNotResolved, Cancelled, InvalidTarget, Error }

public enum PingStatus { Replied, TimedOut, Unreachable, NameNotResolved, Cancelled, InvalidTarget, Error }

public sealed record TcpProbeResult(string Host, int Port, TcpProbeStatus Status, long? ElapsedMs, string Message);

public sealed record PingResult(string Host, PingStatus Status, long? RoundTripMs, string? Address, string Message);

public sealed record PortTarget(int Port, string Service);

public sealed record PortCheckResult(PortTarget Target, TcpProbeResult Result);

public sealed record Interpretation(string Text, InfoLevel Level);

public sealed record ConnectivityReport(string Host, PingResult Ping, TcpProbeResult Tcp, Interpretation Conclusion);