using System.ComponentModel;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Newtin.Core.Models;
using Newtin.Core.Utilities;
using Newtin.Diagnostics.Network;

namespace Newtin.Diagnostics.Hardware;

[SupportedOSPlatform("windows")]
public sealed class HardwareInfoProvider : InfoProviderBase
{
    private const string CpuKey = @"HARDWARE\DESCRIPTION\System\CentralProcessor\0";
    private const string BiosKey = @"HARDWARE\DESCRIPTION\System\BIOS";

    protected override IReadOnlyList<InfoSection> Collect(CancellationToken cancellationToken)
    {
        var sections = new List<InfoSection>
        {
            new("Equipo",
            [
                InfoItem.Read("Fabricante", () => RegistryReader.Read(BiosKey, "SystemManufacturer")),
                InfoItem.Read("Modelo", () => RegistryReader.Read(BiosKey, "SystemProductName")),
                InfoItem.Read("Versión de BIOS", () => RegistryReader.Read(BiosKey, "BIOSVersion"))
            ]),
            new("Procesador",
            [
                InfoItem.Read("Modelo", () => RegistryReader.Read(CpuKey, "ProcessorNameString")),
                InfoItem.Read("Fabricante", () => RegistryReader.Read(CpuKey, "VendorIdentifier")),
                InfoItem.Read("Procesadores lógicos", () => Environment.ProcessorCount.ToString())
            ])
        };

        cancellationToken.ThrowIfCancellationRequested();
        sections.Add(ReadMemory());

        cancellationToken.ThrowIfCancellationRequested();
        sections.Add(ReadAdapters());

        return sections;
    }

    private static InfoSection ReadMemory()
    {
        try
        {
            var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
            if (!GlobalMemoryStatusEx(ref status))
                throw new Win32Exception(Marshal.GetLastWin32Error());

            var total = (long)status.TotalPhys;
            var free = (long)status.AvailPhys;
            var used = total - free;
            var percent = PercentCalculator.Of(used, total);

            return new InfoSection("Memoria RAM",
            [
                new InfoItem("Total detectada", ByteFormatter.Format(total),
                    "Es la memoria que Windows puede usar; puede ser algo menor a la instalada físicamente."),
                new InfoItem("Disponible", ByteFormatter.Format(free)),
                new InfoItem("En uso", $"{ByteFormatter.Format(used)} ({percent}%)", null, percent)
            ]);
        }
        catch (Exception ex)
        {
            return new InfoSection("Memoria RAM",
                [new InfoItem("Memoria", InfoItem.NotAvailable, $"No se pudo consultar: {ex.Message}")]);
        }
    }

    private static InfoSection ReadAdapters()
    {
        try
        {
            var adapters = NetworkInterface.GetAllNetworkInterfaces()
                .Where(a => a.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel))
                .OrderBy(a => a.Name)
                .Select(a => new InfoItem(a.Name, a.Description,
                    $"{NetworkInfoProvider.TranslateType(a)} · {NetworkInfoProvider.TranslateStatus(a.OperationalStatus)}"))
                .ToList();

            if (adapters.Count == 0)
                adapters.Add(new InfoItem("Adaptadores", InfoItem.NotAvailable, "No se detectaron adaptadores de red."));

            return new InfoSection("Adaptadores de red detectados", adapters);
        }
        catch (Exception ex)
        {
            return new InfoSection("Adaptadores de red detectados",
                [new InfoItem("Adaptadores", InfoItem.NotAvailable, $"No se pudo consultar: {ex.Message}")]);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);
}