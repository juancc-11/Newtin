using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.AspNetCore.SignalR.Client;
using Newtin.Core.Interfaces;
using Newtin.Server.Hosting;

namespace Newtin.Server.Tests;

internal sealed class SilentLog : ILogService
{
    public void Info(string message) { }
    public void Warning(string message) { }
    public void Error(string message, Exception? exception = null) { }
}

/// <summary>Servidor real de la pizarra en un puerto libre del equipo, solo en 127.0.0.1.</summary>
internal sealed class TestServer : IAsyncDisposable
{
    private TestServer(WhiteboardServerHost host, ServerOptions options)
    {
        Host = host;
        Options = options;
    }

    public WhiteboardServerHost Host { get; }
    public ServerOptions Options { get; }
    public string BaseUrl => $"http://127.0.0.1:{Options.Port}";
    public string Code => Host.AccessCode!;

    public static async Task<TestServer> StartAsync()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        var options = new ServerOptions(port, null, Path.Combine(AppContext.BaseDirectory, "wwwroot"));
        var host = new WhiteboardServerHost(new SilentLog());
        await host.StartAsync(options);
        return new TestServer(host, options);
    }

    public HubConnection CreateClient(string? code = null) =>
        new HubConnectionBuilder()
            .WithUrl($"{BaseUrl}{WhiteboardServerHost.HubPath}",
                o => o.AccessTokenProvider = () => Task.FromResult<string?>(code ?? Code))
            .Build();

    public async Task<HubConnection> JoinAsync(string name)
    {
        var client = CreateClient();
        await client.StartAsync();
        await client.InvokeAsync<JsonElement>("Join", name);
        return client;
    }

    public ValueTask DisposeAsync() => Host.DisposeAsync();
}

/// <summary>Recibe eventos del servidor y permite esperar uno concreto.</summary>
internal sealed class EventLog
{
    private readonly Channel<JsonElement> _events = Channel.CreateUnbounded<JsonElement>();

    public void Add(JsonElement element) => _events.Writer.TryWrite(element.Clone());

    public async Task<JsonElement> NextAsync(Func<JsonElement, bool>? match = null)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (true)
        {
            var item = await _events.Reader.ReadAsync(timeout.Token);
            if (match is null || match(item)) return item;
        }
    }
}