using Newtin.Core.Models;
using Newtin.Core.Utilities;

namespace Newtin.Diagnostics.Storage;

public sealed class StorageInfoProvider : InfoProviderBase
{
    protected override IReadOnlyList<InfoSection> Collect(CancellationToken cancellationToken)
    {
        DriveInfo[] drives;
        try
        {
            drives = DriveInfo.GetDrives();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [new InfoSection("Unidades",
                [new InfoItem("Unidades", InfoItem.NotAvailable, $"No se pudo consultar: {ex.Message}")])];
        }

        var sections = new List<InfoSection>();
        foreach (var drive in drives)
        {
            cancellationToken.ThrowIfCancellationRequested();
            sections.Add(Describe(drive));
        }

        if (sections.Count == 0)
            sections.Add(new InfoSection("Unidades",
                [new InfoItem("Unidades", InfoItem.NotAvailable, "Windows no informó ninguna unidad.")]));

        return sections;
    }

    private static InfoSection Describe(DriveInfo drive)
    {
        var title = $"Unidad {drive.Name.TrimEnd('\\')}";
        var items = new List<InfoItem> { InfoItem.Read("Tipo", () => TranslateType(drive.DriveType)) };

        bool ready;
        try { ready = drive.IsReady; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { ready = false; }

        if (!ready)
        {
            items.Add(new InfoItem("Estado", InfoItem.NotAvailable,
                "La unidad no está lista (por ejemplo, no tiene disco insertado o no responde)."));
            return new InfoSection(title, items);
        }

        items.Add(InfoItem.Read("Etiqueta", () =>
            string.IsNullOrWhiteSpace(drive.VolumeLabel) ? "(sin nombre)" : drive.VolumeLabel));
        items.Add(InfoItem.Read("Sistema de archivos", () => drive.DriveFormat));

        try
        {
            var total = drive.TotalSize;
            var free = drive.AvailableFreeSpace;
            var used = total - free;
            var percent = PercentCalculator.Of(used, total);

            items.Add(new InfoItem("Capacidad total", ByteFormatter.Format(total)));
            items.Add(new InfoItem("Espacio libre", ByteFormatter.Format(free)));
            items.Add(new InfoItem("Espacio utilizado", ByteFormatter.Format(used)));
            items.Add(new InfoItem("Porcentaje utilizado", $"{percent}%", null, percent));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            items.Add(new InfoItem("Capacidad", InfoItem.NotAvailable, $"No se pudo consultar: {ex.Message}"));
        }

        return new InfoSection(title, items);
    }

    private static string TranslateType(DriveType type) => type switch
    {
        DriveType.Fixed => "Disco local",
        DriveType.Removable => "Extraíble (USB u otro)",
        DriveType.Network => "Unidad de red",
        DriveType.CDRom => "Unidad óptica",
        DriveType.Ram => "Disco en RAM",
        DriveType.NoRootDirectory => "Sin directorio raíz",
        _ => "Desconocido"
    };
}