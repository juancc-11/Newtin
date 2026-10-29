using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Newtin.Core.Models;

namespace Newtin.Diagnostics.SystemInfo;

[SupportedOSPlatform("windows")]
public sealed class SystemInfoProvider : InfoProviderBase
{
    private const string CurrentVersionKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion";

    protected override IReadOnlyList<InfoSection> Collect(CancellationToken cancellationToken)
    {
        var items = new List<InfoItem>
        {
            InfoItem.Read("Nombre del equipo", () => Environment.MachineName),
            InfoItem.Read("Usuario actual", () => Environment.UserName),
            InfoItem.Read("Sistema operativo", GetWindowsName),
            InfoItem.Read("Versión", GetVersion),
            InfoItem.Read("Compilación", GetBuild),
            InfoItem.Read("Arquitectura del sistema", () => RuntimeInformation.OSArchitecture.ToString()),
            InfoItem.Read("Fecha y hora de la consulta", () => DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss")),
            InfoItem.Read("Tiempo de actividad", GetUptime,
                "Con el inicio rápido de Windows activado, este tiempo puede ser mayor al esperado.")
        };

        return [new InfoSection("Sistema operativo y equipo", items)];
    }

    private static string? GetWindowsName()
    {
        var name = RegistryReader.Read(CurrentVersionKey, "ProductName");
        // Windows 11 todavía guarda "Windows 10" en ProductName; el número de compilación lo distingue.
        if (name is not null && Environment.OSVersion.Version.Build >= 22000)
            name = name.Replace("Windows 10", "Windows 11");
        return name;
    }

    private static string? GetVersion() =>
        RegistryReader.Read(CurrentVersionKey, "DisplayVersion")
        ?? RegistryReader.Read(CurrentVersionKey, "ReleaseId");

    private static string? GetBuild()
    {
        var build = RegistryReader.Read(CurrentVersionKey, "CurrentBuild");
        var revision = RegistryReader.Read(CurrentVersionKey, "UBR");
        return build is null ? null : revision is null ? build : $"{build}.{revision}";
    }

    private static string GetUptime()
    {
        var uptime = TimeSpan.FromMilliseconds(Environment.TickCount64);
        return $"{(int)uptime.TotalDays} días, {uptime.Hours} h, {uptime.Minutes} min";
    }
}