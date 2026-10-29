using Microsoft.AspNetCore.Http;
using Newtin.Core.Interfaces;
using Newtin.Server.Services;

namespace Newtin.Server.Hosting;

/// <summary>Protege el endpoint de SignalR con el código de acceso. Los archivos de la página son públicos y no contienen secretos.</summary>
internal sealed class AccessCodeMiddleware(RequestDelegate next, AccessGuard guard, ILogService log)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path;
        if (!path.StartsWithSegments(WhiteboardServerHost.HubPath) && !path.StartsWithSegments("/api"))
        {
            await next(context);
            return;
        }

        var client = context.Connection.RemoteIpAddress?.ToString() ?? "desconocido";
        var check = guard.Check(ExtractToken(context.Request), client, DateTimeOffset.UtcNow);

        switch (check.Decision)
        {
            case AccessDecision.Allowed:
                await next(context);
                break;

            case AccessDecision.Blocked:
                if (check.JustBlocked)
                    log.Warning($"Demasiados códigos incorrectos desde {client}; se bloquea temporalmente.");
                context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                context.Response.Headers.RetryAfter = "300";
                break;

            default:
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                break;
        }
    }

    private static string ExtractToken(HttpRequest request)
    {
        var header = request.Headers.Authorization.ToString();
        if (header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return header[7..].Trim();

        // Los WebSockets del navegador no pueden enviar cabeceras: SignalR usa la cadena de consulta.
        return request.Query["access_token"].ToString();
    }
}