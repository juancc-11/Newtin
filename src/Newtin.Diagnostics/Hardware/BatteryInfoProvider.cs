using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Newtin.Core.Models;

namespace Newtin.Diagnostics.Hardware;

[SupportedOSPlatform("windows")]
public sealed class BatteryInfoProvider : InfoProviderBase
{
    protected override IReadOnlyList<InfoSection> Collect(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            if (!GetSystemPowerStatus(out var status))
                throw new Win32Exception(Marshal.GetLastWin32Error());

            cancellationToken.ThrowIfCancellationRequested();
            return [CreateSection(status)];
        }
        catch (Win32Exception ex)
        {
            return
            [
                new InfoSection("Batería",
                [
                    new InfoItem("Estado", InfoItem.NotAvailable,
                        $"Windows no pudo consultar el estado de energía: {ex.Message}.")
                ])
            ];
        }
    }

    private static InfoSection CreateSection(SystemPowerStatus status)
    {
        if (status.BatteryFlag == 128)
        {
            return new InfoSection("Batería",
            [
                new InfoItem("Disponibilidad", "No se detectó una batería instalada.",
                    "Este equipo puede ser de escritorio o no exponer una batería a Windows.")
            ]);
        }

        var items = new List<InfoItem>
        {
            new("Alimentación", status.AcLineStatus switch
            {
                0 => "Usando batería",
                1 => "Conectado a la corriente",
                _ => InfoItem.NotAvailable
            }),
            CreateChargeItem(status.BatteryLifePercent, status.BatteryFlag),
            new("Estado de carga", GetChargeState(status.BatteryFlag)),
            new("Autonomía estimada", FormatDuration(status.BatteryLifeTime))
        };

        return new InfoSection("Estado de la batería", items);
    }

    private static InfoItem CreateChargeItem(byte percent, byte flags)
    {
        if (percent == byte.MaxValue)
            return new InfoItem("Carga actual", InfoItem.NotAvailable,
                "Windows no proporcionó el porcentaje de carga.");

        var level = (flags & 4) != 0 || percent <= 10
            ? InfoLevel.Error
            : (flags & 2) != 0 || percent <= 20
                ? InfoLevel.Warning
                : InfoLevel.Normal;
        var note = level switch
        {
            InfoLevel.Error => "La carga es crítica. Conecta el equipo a la corriente para evitar que se apague.",
            InfoLevel.Warning => "La carga está baja. Conviene conectar el equipo a la corriente.",
            _ => null
        };

        return new InfoItem("Carga actual", $"{percent}%", note, percent, level);
    }

    private static string GetChargeState(byte flags)
    {
        if (flags == byte.MaxValue)
            return "Windows no pudo determinar el estado.";
        if ((flags & 8) != 0)
            return "Cargando";
        if ((flags & 4) != 0)
            return "Carga crítica";
        if ((flags & 2) != 0)
            return "Carga baja";
        if ((flags & 1) != 0)
            return "Carga alta";
        return "Estado normal";
    }

    private static string FormatDuration(uint seconds)
    {
        if (seconds == uint.MaxValue)
            return "No disponible";

        var duration = TimeSpan.FromSeconds(seconds);
        return duration.TotalMinutes < 1
            ? "Menos de 1 minuto"
            : $"{(int)duration.TotalHours} h {duration.Minutes} min";
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemPowerStatus
    {
        public byte AcLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public uint BatteryLifeTime;
        public uint BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemPowerStatus(out SystemPowerStatus systemPowerStatus);
}
