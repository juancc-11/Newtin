using System.Text;
using Newtin.Core.Interfaces;
using Newtin.Infrastructure.FileSystem;
using Newtin.Server.Hosting;
using Newtin.Server.Persistence;
using System.IO;

namespace Newtin.Desktop.Services;

public sealed record FileOpResult(bool Ok, string Message, IReadOnlyList<string> Warnings);

/// <summary>Guardar, abrir, exportar e importar la pizarra, más el autoguardado. Siempre devuelve un resultado: nunca lanza al usuario una excepción.</summary>
public sealed class BoardFilesService : IDisposable
{
    private static readonly UTF8Encoding Utf8 = new(false);

    private readonly WhiteboardServerHost _host;
    private readonly ILogService _log;
    private readonly BoardAutosave _autosave;

    public BoardFilesService(WhiteboardServerHost host, ILogService log)
    {
        _host = host;
        _log = log;
        _autosave = new BoardAutosave(host, Path.Combine(AppPaths.Whiteboard, "autosave.newtin"), log);
    }

    public string AutosavePath => _autosave.Path;
    public bool IsEmpty => _host.Snapshot().Elements.Count == 0;

    public RestoreResult RestoreLast() => _autosave.Restore();
    public void FlushAutosave() => _autosave.Flush();
    public void Dispose() => _autosave.Dispose();

    private static FileOpResult Fail(string message) => new(false, message, []);

    public FileOpResult Save(string path)
    {
        var data = NativeFormat.Write(_host.Snapshot(), DateTimeOffset.UtcNow);
        var result = AtomicFile.Write(path, data);

        _log.Info(result.Ok ? "Pizarra guardada en un archivo." : $"No se pudo guardar la pizarra: {result.Error}");
        return result.Ok ? new FileOpResult(true, "Pizarra guardada.", []) : Fail(result.Error!);
    }

    public FileOpResult Export(string path)
    {
        var format = BoardFormats.FromPath(path);
        if (format is null or BoardFormat.Text)
            return Fail("Formato no admitido para exportar. Usa .png, .svg, .md, .csv o .newtin.");

        var snapshot = _host.Snapshot();
        if (snapshot.Elements.Count == 0) return Fail("La pizarra está vacía: no hay nada que exportar.");

        byte[] data;
        try
        {
            data = format switch
            {
                BoardFormat.Native => NativeFormat.Write(snapshot, DateTimeOffset.UtcNow),
                BoardFormat.Svg => Utf8.GetBytes(SvgFormat.Write(snapshot)),
                BoardFormat.Markdown => Utf8.GetBytes(MarkdownFormat.Write(snapshot, DateTimeOffset.Now)),
                BoardFormat.Csv => CsvFormat.Write(snapshot),
                _ => PngExporter.Render(snapshot)
            };
        }
        catch (Exception ex)
        {
            _log.Error("No se pudo generar el archivo de exportación.", ex);
            return Fail($"No se pudo generar el archivo: {ex.Message}");
        }

        var result = AtomicFile.Write(path, data);
        if (!result.Ok)
        {
            _log.Warning($"No se pudo exportar la pizarra: {result.Error}");
            return Fail(result.Error!);
        }

        var warnings = new List<string>();
        if (format is BoardFormat.Markdown or BoardFormat.Csv
            && snapshot.Elements.All(e => e.Type == Newtin.Server.Models.ElementType.Stroke))
            warnings.Add("La pizarra solo tiene dibujos: este formato guarda notas y textos, así que el archivo quedó casi vacío.");

        _log.Info($"Pizarra exportada ({format}).");
        return new FileOpResult(true, "Pizarra exportada.", warnings);
    }

    /// <summary>Abre un archivo .newtin y reemplaza la pizarra actual, también en los navegadores conectados.</summary>
    public Task<FileOpResult> OpenAsync(string path) => ImportAsync(path, ImportMode.Replace, nativeOnly: true);

    /// <summary>Suma el contenido del archivo a la pizarra actual.</summary>
    public Task<FileOpResult> ImportAsync(string path) => ImportAsync(path, ImportMode.Merge, nativeOnly: false);

    private async Task<FileOpResult> ImportAsync(string path, ImportMode mode, bool nativeOnly)
    {
        var format = BoardFormats.FromPath(path);
        if (nativeOnly && format != BoardFormat.Native)
            return Fail("Para abrir una pizarra elige un archivo .newtin.");

        var read = AtomicFile.Read(path, BoardAutosave.MaxFileBytes);
        if (read.Data is null) return Fail(read.Error!);

        PlanResult plan = format switch
        {
            BoardFormat.Native => NativeFormat.Read(read.Data),
            BoardFormat.Markdown or BoardFormat.Text => MarkdownFormat.Read(read.Data),
            BoardFormat.Csv => CsvFormat.Read(read.Data),
            _ => PlanResult.Fail("Formato no admitido para importar. Usa .newtin, .md, .txt o .csv.")
        };
        if (plan.Plan is null) return Fail(plan.Error!);

        var outcome = await _host.ImportAsync(plan.Plan, mode);
        _log.Info($"Pizarra {(mode == ImportMode.Replace ? "abierta" : "importada")}: {outcome.Elements} elementos.");

        var message = mode == ImportMode.Replace
            ? $"Pizarra abierta: {outcome.Elements} elemento(s) y {outcome.Categories} categoría(s)."
            : $"Importación completada: {outcome.Elements} elemento(s) y {outcome.Categories} categoría(s) nuevas.";
        return new FileOpResult(true, message, outcome.Warnings);
    }

    public async Task NewBoardAsync()
    {
        await _host.ClearBoardAsync();
        _log.Info("Pizarra nueva (se vació la pizarra).");
    }
}