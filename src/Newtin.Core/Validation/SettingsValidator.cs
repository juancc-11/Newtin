using Newtin.Core.Models;

namespace Newtin.Core.Validation;

public static class SettingsValidator
{
    public static IReadOnlyList<string> Validate(AppSettings settings)
    {
        var errors = new List<string>();

        if (!string.IsNullOrWhiteSpace(settings.Network.RemoteHost)
            && !HostValidator.TryNormalize(settings.Network.RemoteHost, out _, out var hostError))
            errors.Add($"Equipo remoto: {hostError}");

        if (!string.IsNullOrWhiteSpace(settings.Network.RemoteShare)
            && !ShareNameValidator.TryValidate(settings.Network.RemoteShare, out var shareError))
            errors.Add($"Carpeta compartida: {shareError}");

        if (settings.Network.ConnectionTimeoutSeconds is < 1 or > 60)
            errors.Add("El tiempo de espera debe estar entre 1 y 60 segundos.");

        if (settings.WhiteboardServer.Port is < 1024 or > 65535)
            errors.Add("El puerto del servidor debe estar entre 1024 y 65535.");
        
        if (!string.IsNullOrWhiteSpace(settings.WhiteboardServer.LanAddress)
            && !HostValidator.TryParseIPv4(settings.WhiteboardServer.LanAddress, out _))
            errors.Add("La dirección para la red local debe ser una dirección IPv4 válida, por ejemplo 192.168.100.1.");

        return errors;
    }
}