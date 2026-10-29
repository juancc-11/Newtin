using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Newtin.Core.Interfaces;
using Newtin.Server.Hubs;
using Newtin.Server.Services;
using Newtin.Server.Validation;
using Microsoft.AspNetCore.SignalR;
using Newtin.Server.Models;
using Newtin.Server.Persistence;
using Microsoft.AspNetCore.Http;

namespace Newtin.Server.Hosting;

public enum ServerState { Stopped, Starting, Running, Stopping }

/// <param name="Port">Puerto TCP (1-65535).</param>
/// <param name="LanAddress">Dirección del adaptador de red local, o null para escuchar solo en este equipo.</param>
/// <param name="WebRootPath">Carpeta con los archivos de la página (wwwroot).</param>
public sealed record ServerOptions(int Port, IPAddress? LanAddress, string WebRootPath);

/// <summary>Aloja Kestrel + SignalR dentro del proceso de Newtin. Se inicia y se detiene de forma controlada.</summary>
public sealed class WhiteboardServerHost : IAsyncDisposable
{
    public const string HubPath = "/hub/whiteboard";

    private readonly ILogService _log;
    private readonly WhiteboardStateService _state = new();   // sobrevive a detener/iniciar mientras Newtin siga abierto
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private WebApplication? _app;
    private ClientSessionService? _sessions;

    public WhiteboardServerHost(ILogService log)
    {
        _log = log;
        _state.Changed += () => BoardChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Puede dispararse desde cualquier hilo.</summary>
    public event EventHandler? Changed;

    public ServerState State { get; private set; } = ServerState.Stopped;
    public string? AccessCode { get; private set; }
    public string? LocalUrl { get; private set; }
    public string? LanUrl { get; private set; }
    public int ClientCount => _sessions?.Count ?? 0;
    public IReadOnlyList<string> ClientNames => _sessions?.Names ?? [];
    public int ElementCount => _state.ElementCount;

    public async Task StartAsync(ServerOptions options, CancellationToken cancellationToken = default)
    {
        await _lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (State != ServerState.Stopped)
                throw new InvalidOperationException("El servidor ya está en ejecución.");

            SetState(ServerState.Starting);

            var code = AccessCodeGenerator.Generate();
            var sessions = new ClientSessionService();
            sessions.Changed += RaiseChanged;
            WebApplication? app = null;

            try
            {
                app = Build(options, code, sessions);
                await app.StartAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                sessions.Changed -= RaiseChanged;
                if (app is not null) await app.DisposeAsync().ConfigureAwait(false);
                SetState(ServerState.Stopped);
                throw;
            }

            _app = app;
            _sessions = sessions;
            AccessCode = code;
            LocalUrl = $"http://127.0.0.1:{options.Port}";
            LanUrl = options.LanAddress is null ? null : $"http://{options.LanAddress}:{options.Port}";
            SetState(ServerState.Running);

            _log.Info($"Servidor de la pizarra iniciado en {LocalUrl}" +
                      (LanUrl is null ? " (solo este equipo)." : $" y {LanUrl} (red local)."));
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    public async Task StopAsync()
    {
        await _lifecycle.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_app is null) return;

            SetState(ServerState.Stopping);
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await _app.StopAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                _log.Warning("El servidor tardó en cerrar las conexiones; se forzó el cierre.");
            }

            await _app.DisposeAsync().ConfigureAwait(false);
            if (_sessions is not null) _sessions.Changed -= RaiseChanged;

            _app = null;
            _sessions = null;
            AccessCode = null;
            LocalUrl = null;
            LanUrl = null;
            SetState(ServerState.Stopped);
            _log.Info("Servidor de la pizarra detenido.");
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);

    private WebApplication Build(ServerOptions options, string code, ClientSessionService sessions)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ContentRootPath = AppContext.BaseDirectory,
            WebRootPath = options.WebRootPath,
            EnvironmentName = Environments.Production
        });

        builder.Logging.ClearProviders();
        builder.Host.ConfigureHostOptions(o => o.ShutdownTimeout = TimeSpan.FromSeconds(3));

        builder.WebHost.ConfigureKestrel(kestrel =>
        {
            kestrel.AddServerHeader = false;
            kestrel.Limits.MaxRequestBodySize = WhiteboardLimits.MaxMessageBytes * 2;
            kestrel.Listen(IPAddress.Loopback, options.Port);
            if (options.LanAddress is not null)
                kestrel.Listen(options.LanAddress, options.Port);
        });

        builder.Services.AddSingleton(_log);
        builder.Services.AddSingleton(_state);
        builder.Services.AddSingleton(sessions);
        builder.Services.AddSingleton(new AccessGuard(code));

        builder.Services
            .AddSignalR(o =>
            {
                o.MaximumReceiveMessageSize = WhiteboardLimits.MaxMessageBytes;
                o.EnableDetailedErrors = false;
            })
            .AddJsonProtocol(o =>
            {
                o.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
                o.PayloadSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
            });

        var app = builder.Build();

        app.Use(async (context, next) =>
        {
            var headers = context.Response.Headers;
            headers["X-Content-Type-Options"] = "nosniff";
            headers["X-Frame-Options"] = "DENY";
            headers["Referrer-Policy"] = "no-referrer";
            headers["Content-Security-Policy"] =
                "default-src 'self'; connect-src 'self' ws: wss:; img-src 'self' data:; frame-ancestors 'none'";
            await next();
        });

        app.UseMiddleware<AccessCodeMiddleware>();
        app.UseDefaultFiles();
        app.UseStaticFiles();
        app.MapHub<WhiteboardHub>(HubPath);
                // Descarga de la pizarra en vivo. Pide el mismo código de acceso que la conexión (ver AccessCodeMiddleware).
        app.MapGet("/api/export/{format}", (string format, HttpContext context, WhiteboardStateService state) =>
        {
            context.Response.Headers.CacheControl = "no-store";

            var key = format.ToLowerInvariant();
            if (key is not ("newtin" or "svg" or "markdown" or "csv")) return Results.NotFound();

            var snapshot = state.Snapshot();
            if (snapshot.Elements.Count == 0) return Results.StatusCode(StatusCodes.Status409Conflict);

            return key switch
            {
                "newtin" => Results.File(NativeFormat.Write(snapshot, DateTimeOffset.UtcNow), "application/json; charset=utf-8"),
                "svg" => Results.Text(SvgFormat.Write(snapshot), "image/svg+xml; charset=utf-8"),
                "markdown" => Results.Text(MarkdownFormat.Write(snapshot, DateTimeOffset.Now), "text/markdown; charset=utf-8"),
                _ => Results.File(CsvFormat.Write(snapshot), "text/csv; charset=utf-8")
            };
        });

        return app;
    }

    private void SetState(ServerState state)
    {
        State = state;
        RaiseChanged();
    }

    private void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);

        /// <summary>Se dispara cuando cambia el contenido de la pizarra, desde cualquier hilo.</summary>
    public event EventHandler? BoardChanged;

    public WhiteboardSnapshot Snapshot() => _state.Snapshot();

    /// <summary>Aplica una importación sin avisar a los clientes (sirve también con el servidor detenido).</summary>
    public ImportOutcome ApplyImport(ImportPlan plan, ImportMode mode) => BoardImporter.Apply(_state, plan, mode);

    public async Task<ImportOutcome> ImportAsync(ImportPlan plan, ImportMode mode)
    {
        var outcome = ApplyImport(plan, mode);
        await BroadcastBoardAsync().ConfigureAwait(false);
        return outcome;
    }

    public async Task ClearBoardAsync()
    {
        _state.ClearAll();
        _state.MarkChanged();
        await BroadcastBoardAsync().ConfigureAwait(false);
    }

    private async Task BroadcastBoardAsync()
    {
        try
        {
            var hub = _app?.Services.GetService<IHubContext<WhiteboardHub>>();
            if (hub is null) return;
            await hub.Clients.All.SendAsync("BoardReplaced", _state.Snapshot()).ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
            // El servidor se estaba deteniendo: no hay a quién avisar.
        }
    }
}