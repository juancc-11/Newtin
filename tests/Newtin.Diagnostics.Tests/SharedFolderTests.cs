using System.Security;
using Newtin.Core.Interfaces;
using Newtin.Core.Models;
using Newtin.Diagnostics.SharedAccess;

namespace Newtin.Diagnostics.Tests;

public class SharedFolderTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(1);

    private sealed class SilentLog : ILogService
    {
        public void Info(string message) { }
        public void Warning(string message) { }
        public void Error(string message, Exception? exception = null) { }
    }

    private sealed class FakeTcp(TcpProbeStatus status) : ITcpPortProbe
    {
        public int Calls;

        public Task<TcpProbeResult> ProbeAsync(string host, int port, TimeSpan timeout, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new TcpProbeResult(host, port, status, 1, "mensaje"));
        }
    }

    private sealed class FakeNative(int connectCode) : ISmbNative
    {
        public int ConnectCalls;
        public int DisconnectCalls;
        public string? ConnectedPath;
        public string? DisconnectedPath;

        public int Connect(string uncPath, string user, SecureString password)
        {
            ConnectCalls++;
            ConnectedPath = uncPath;
            return connectCode;
        }

        public int Disconnect(string uncPath)
        {
            DisconnectCalls++;
            DisconnectedPath = uncPath;
            return 0;
        }
    }

    private static SecureString Secure(string text)
    {
        var secure = new SecureString();
        foreach (var c in text) secure.AppendChar(c);
        return secure;
    }

    private static ShareRequest Credentials(string password = "secreto") =>
        new("192.168.100.10", "Velocidad", AuthMode.Credentials, "FAST", Secure(password));

    private static ShareRequest CurrentSession() =>
        new("192.168.100.10", "Velocidad", AuthMode.CurrentSession);

    private static SharedFolderService Service(FakeTcp tcp, FakeNative native, Func<string, Task> lister) =>
        new(tcp, native, new SilentLog(), (path, _) => lister(path));

    [Fact]
    public async Task ClosedSmbPort_ReportsHostUnreachable_WithoutTouchingWindowsConnections()
    {
        var tcp = new FakeTcp(TcpProbeStatus.TimedOut);
        var native = new FakeNative(0);

        using var request = Credentials();
        var result = await Service(tcp, native, _ => Task.CompletedTask).CheckAsync(request, Timeout, default);

        Assert.Equal(ShareAccessStatus.HostUnreachable, result.Status);
        Assert.Equal(0, native.ConnectCalls);
    }

    [Fact]
    public async Task CurrentSession_NeverCreatesOrClosesConnections()
    {
        var native = new FakeNative(0);

        using var request = CurrentSession();
        var result = await Service(new FakeTcp(TcpProbeStatus.Connected), native, _ => Task.CompletedTask)
            .CheckAsync(request, Timeout, default);

        Assert.Equal(ShareAccessStatus.Accessible, result.Status);
        Assert.Equal(0, native.ConnectCalls);
        Assert.Equal(0, native.DisconnectCalls);
    }

    [Fact]
    public async Task Credentials_WithoutPassword_AreRejectedBeforeAnyNetworkCall()
    {
        var tcp = new FakeTcp(TcpProbeStatus.Connected);

        using var request = new ShareRequest("192.168.100.10", "Velocidad", AuthMode.Credentials, "FAST", Secure(""));
        var result = await Service(tcp, new FakeNative(0), _ => Task.CompletedTask).CheckAsync(request, Timeout, default);

        Assert.Equal(ShareAccessStatus.InvalidInput, result.Status);
        Assert.Equal(0, tcp.Calls);
    }

    [Fact]
    public async Task InvalidHost_IsRejected()
    {
        using var request = new ShareRequest("999.1.1.1", "Velocidad", AuthMode.CurrentSession);
        var result = await Service(new FakeTcp(TcpProbeStatus.Connected), new FakeNative(0), _ => Task.CompletedTask)
            .CheckAsync(request, Timeout, default);

        Assert.Equal(ShareAccessStatus.InvalidInput, result.Status);
    }

    [Fact]
    public async Task WrongPassword_ReportsBadCredentials_AndLeavesNothingOpen()
    {
        var native = new FakeNative(1326);

        using var request = Credentials();
        var result = await Service(new FakeTcp(TcpProbeStatus.Connected), native, _ => Task.CompletedTask)
            .CheckAsync(request, Timeout, default);

        Assert.Equal(ShareAccessStatus.BadCredentials, result.Status);
        Assert.Equal(0, native.DisconnectCalls);
    }

    [Fact]
    public async Task SessionConflict_ExplainsHowToReviewConnections_AndClosesNothing()
    {
        var native = new FakeNative(1219);

        using var request = Credentials();
        var result = await Service(new FakeTcp(TcpProbeStatus.Connected), native, _ => Task.CompletedTask)
            .CheckAsync(request, Timeout, default);

        Assert.Equal(ShareAccessStatus.SessionConflict, result.Status);
        Assert.Contains("net use", result.Message);
        Assert.Contains("192.168.100.10", result.Message);
        Assert.Equal(0, native.DisconnectCalls);
    }

    [Fact]
    public async Task Credentials_CloseTheTemporaryConnectionTheyCreated()
    {
        var native = new FakeNative(0);

        using var request = Credentials();
        var result = await Service(new FakeTcp(TcpProbeStatus.Connected), native, _ => Task.CompletedTask)
            .CheckAsync(request, Timeout, default);

        Assert.Equal(ShareAccessStatus.Accessible, result.Status);
        Assert.Equal(1, native.ConnectCalls);
        Assert.Equal(1, native.DisconnectCalls);
    }

    [Fact]
    public async Task Credentials_WithoutShare_AuthenticateAtIpcAndBrowseTheRemoteHost()
    {
        var native = new FakeNative(0);
        using var request = new ShareRequest("192.168.100.10", "", AuthMode.Credentials, "FAST", Secure("secreto"));
        var service = Service(new FakeTcp(TcpProbeStatus.Connected), native, path =>
        {
            Assert.Equal(@"\\192.168.100.10", path);
            return Task.CompletedTask;
        });

        using (var connection = await service.ConnectAsync(request, Timeout, default))
        {
            Assert.True(connection.Access.IsAccessible);
            Assert.Equal(@"\\192.168.100.10", connection.UncPath);
            Assert.Equal(@"\\192.168.100.10\IPC$", native.ConnectedPath);
        }

        Assert.Equal(@"\\192.168.100.10\IPC$", native.DisconnectedPath);
    }

    [Fact]
    public async Task ValidLoginWithoutFolderRights_ReportsInsufficientPermissions()
    {
        var native = new FakeNative(0);

        using var request = Credentials();
        var result = await Service(new FakeTcp(TcpProbeStatus.Connected), native,
                _ => throw new UnauthorizedAccessException())
            .CheckAsync(request, Timeout, default);

        Assert.Equal(ShareAccessStatus.InsufficientPermissions, result.Status);
        Assert.Equal(1, native.DisconnectCalls);
    }

    [Fact]
    public async Task MissingShare_IsReported()
    {
        using var request = CurrentSession();
        var result = await Service(new FakeTcp(TcpProbeStatus.Connected), new FakeNative(0),
                _ => throw new DirectoryNotFoundException())
            .CheckAsync(request, Timeout, default);

        Assert.Equal(ShareAccessStatus.ShareNotFound, result.Status);
    }

    [Fact]
    public async Task LogonFailureInsideAnIOException_IsMappedToBadCredentials()
    {
        using var request = CurrentSession();
        var result = await Service(new FakeTcp(TcpProbeStatus.Connected), new FakeNative(0),
                _ => throw new IOException("x", unchecked((int)0x8007052E)))
            .CheckAsync(request, Timeout, default);

        Assert.Equal(ShareAccessStatus.BadCredentials, result.Status);
    }

    [Theory]
    [InlineData(0, ShareAccessStatus.Accessible)]
    [InlineData(5, ShareAccessStatus.AccessDenied)]
    [InlineData(53, ShareAccessStatus.HostUnreachable)]
    [InlineData(67, ShareAccessStatus.ShareNotFound)]
    [InlineData(1326, ShareAccessStatus.BadCredentials)]
    [InlineData(1909, ShareAccessStatus.AccountProblem)]
    [InlineData(1219, ShareAccessStatus.SessionConflict)]
    [InlineData(99999, ShareAccessStatus.Error)]
    public void Mapper_TranslatesWindowsCodes(int code, ShareAccessStatus expected) =>
        Assert.Equal(expected, SmbErrorMapper.FromWin32(code, "host").Status);

    [Fact]
    public void Win32CodeFrom_ReadsOnlyWindowsHResults()
    {
        Assert.Equal(1326, SmbErrorMapper.Win32CodeFrom(new IOException("x", unchecked((int)0x8007052E))));
        Assert.Null(SmbErrorMapper.Win32CodeFrom(new IOException("x")));
    }
}