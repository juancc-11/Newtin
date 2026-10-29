using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using Newtin.Core.Interfaces;
using Newtin.Diagnostics.Reports;
using Newtin.Infrastructure.FileSystem;
using Newtin.Server.Persistence;
using System.IO;

namespace Newtin.Desktop.Services;

public sealed record ReportFile(string Path, string Name, DateTime Modified, long Size);

public sealed record ReportOutcome(bool Ok, string Message, IReadOnlyList<string> Paths);

/// <summary>Genera los informes en la carpeta de datos de Newtin. Nunca escribe fuera de ella ni borra archivos.</summary>
public sealed class ReportFilesService
{
    private static readonly UTF8Encoding Utf8 = new(false);
    private static readonly UTF8Encoding Utf8Bom = new(true);

    private readonly ISettingsService _settings;
    private readonly ILogService _log;

    public ReportFilesService(ISettingsService settings, ILogService log)
    {
        _settings = settings;
        _log = log;
    }

    private static string AppVersion()
    {
        var version = Assembly.GetEntryAssembly()?.GetName().Version;
        return version is null ? "0.1.0" : $"{version.Major}.{version.Minor}.{version.Build}";
    }

    private static string UniquePath(DateTimeOffset now, ReportFormat format)
    {
        var name = ReportNaming.FileName(now, format);
        var path = Path.Combine(AppPaths.Reports, name);

        for (var i = 2; File.Exists(path) && i < 100; i++)
            path = Path.Combine(AppPaths.Reports,
                Path.GetFileNameWithoutExtension(name) + $"-{i}" + Path.GetExtension(name));

        return path;
    }

    public ReportOutcome Generate(
        IReadOnlyList<ModuleResult> selected,
        IReadOnlyList<string> notRun,
        IReadOnlyList<ReportFormat> formats,
        bool redactMac)
    {
        if (selected.Count == 0) return new ReportOutcome(false, "Selecciona al menos un resultado para el informe.", []);
        if (formats.Count == 0) return new ReportOutcome(false, "Elige un formato para el informe.", []);

        var now = DateTimeOffset.Now;
        var network = _settings.Current.Network;
        var context = new ReportContext(Environment.MachineName, AppVersion(), network.RemoteHost, network.RemoteShare, now, redactMac);
        var report = ReportBuilder.Build(context, selected, notRun);

        var paths = new List<string>();
        foreach (var format in formats)
        {
            byte[] bytes;
            if (format == ReportFormat.Json)
            {
                bytes = Utf8.GetBytes(JsonReportFormatter.Format(report));
            }
            else if (format == ReportFormat.Html)
            {
                bytes = Utf8.GetBytes(HtmlReportFormatter.Format(report));
            }
            else
            {
                // El BOM hace que el Bloc de notas reconozca las tildes.
                bytes = [.. Utf8Bom.GetPreamble(), .. Utf8.GetBytes(TextReportFormatter.Format(report))];
            }

            var path = UniquePath(now, format);
            var result = AtomicFile.Write(path, bytes, createDirectory: true);
            if (!result.Ok)
            {
                _log.Warning($"No se pudo guardar el informe: {result.Error}");
                return new ReportOutcome(false, $"No se pudo guardar el informe: {result.Error}", paths);
            }

            paths.Add(path);
        }

        _log.Info($"Informe generado ({selected.Count} módulos, {string.Join(", ", formats)}).");
        return new ReportOutcome(true, paths.Count == 1 ? "Informe generado." : "Informes generados.", paths);
    }

    public IReadOnlyList<ReportFile> Recent(int count = 10)
    {
        try
        {
            var directory = new DirectoryInfo(AppPaths.Reports);
            if (!directory.Exists) return [];

            return directory.EnumerateFiles("informe-*.*")
                .Where(f => f.Extension is ".txt" or ".json" or ".html")
                .OrderByDescending(f => f.LastWriteTime)
                .Take(count)
                .Select(f => new ReportFile(f.FullName, f.Name, f.LastWriteTime, f.Length))
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>Abre un archivo o carpeta con el programa predeterminado. Devuelve el motivo si falla.</summary>
    public string? Open(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            return null;
        }
        catch (Win32Exception ex)
        {
            return ex.Message;
        }
    }

    public string? OpenFolder()
    {
        try
        {
            Directory.CreateDirectory(AppPaths.Reports);
            return Open(AppPaths.Reports);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ex.Message;
        }
    }
}