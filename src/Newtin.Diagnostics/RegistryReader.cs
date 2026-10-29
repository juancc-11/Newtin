using System.Runtime.Versioning;
using Microsoft.Win32;

namespace Newtin.Diagnostics;

[SupportedOSPlatform("windows")]
internal static class RegistryReader
{
    // Solo lectura en HKLM: no requiere permisos de administrador.
    public static string? Read(string subKey, string valueName)
    {
        using var key = Registry.LocalMachine.OpenSubKey(subKey);
        return key?.GetValue(valueName)?.ToString()?.Trim();
    }
}