using System.Net;
using System.Net.Sockets;

namespace Newtin.Core.Validation;

public static class HostValidator
{
    private static readonly char[] ForbiddenChars = ['\\', '/', ':', '@', '?', '#'];

    /// <summary>Valida una dirección IPv4 o un nombre de equipo. Por ahora no admite IPv6.</summary>
    public static bool TryNormalize(string? input, out string host, out string error)
    {
        host = (input ?? "").Trim();
        error = "";

        if (host.Length == 0)
        {
            error = "Escribe la dirección IP o el nombre del equipo.";
            return false;
        }

        if (host.Any(char.IsWhiteSpace) || host.IndexOfAny(ForbiddenChars) >= 0)
        {
            error = "Escribe solo la dirección IP o el nombre del equipo, sin puerto, barras ni espacios.";
            return false;
        }

        if (host.All(c => char.IsAsciiDigit(c) || c == '.'))
        {
            if (host.Split('.').Length != 4
                || !IPAddress.TryParse(host, out var ip)
                || ip.AddressFamily != AddressFamily.InterNetwork)
            {
                error = "La dirección IP no es válida. Usa el formato 192.168.100.10.";
                return false;
            }

            if (ip.Equals(IPAddress.Any) || ip.Equals(IPAddress.Broadcast))
            {
                error = "Esa dirección IP no corresponde a un equipo concreto.";
                return false;
            }

            host = ip.ToString();
            return true;
        }

        if (host.Length > 253 || Uri.CheckHostName(host) != UriHostNameType.Dns)
        {
            error = "El nombre del equipo no es válido.";
            return false;
        }

        return true;
    }

        /// <summary>Solo direcciones IPv4 literales (cuatro números separados por puntos).</summary>
    public static bool TryParseIPv4(string? input, out IPAddress address)
    {
        address = IPAddress.None;
        var text = (input ?? "").Trim();

        if (text.Split('.').Length != 4
            || !IPAddress.TryParse(text, out var parsed)
            || parsed.AddressFamily != AddressFamily.InterNetwork
            || parsed.Equals(IPAddress.Any)
            || parsed.Equals(IPAddress.Broadcast))
            return false;

        address = parsed;
        return true;
    }
}