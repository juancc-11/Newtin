using System.Text;
using Newtin.Core.Interfaces;
using Newtin.Core.Models;

namespace Newtin.Diagnostics.Permissions;

/// <summary>
/// Prueba permisos reales creando una carpeta temporal propia dentro de la carpeta indicada.
/// Solo toca la carpeta y el archivo que ella misma crea; nunca borra de forma recursiva.
/// </summary>
public sealed class PermissionTester
{
    private const string StepList = "Listar el contenido de la carpeta";
    private const string StepCreateFolder = "Crear carpeta de prueba";
    private const string StepCreateFile = "Crear archivo de prueba";
    private const string StepRead = "Leer el archivo y comparar su contenido";
    private const string StepModify = "Modificar el archivo";
    private const string StepVerify = "Verificar la modificación";

    private static readonly UTF8Encoding Utf8 = new(false);
    private readonly ILogService _log;

    public PermissionTester(ILogService log) => _log = log;

    public Task<PermissionTestReport> RunAsync(string rootPath, CancellationToken cancellationToken) =>
        Task.Run(() => Run(rootPath, cancellationToken), cancellationToken);

    private PermissionTestReport Run(string rootPath, CancellationToken ct)
    {
        var steps = new List<PermissionStep>();
        var id = Guid.NewGuid().ToString("N")[..8];
        var folder = Path.Combine(rootPath, $"NewtinTest_{id}");
        var file = Path.Combine(folder, "permiso-prueba.txt");
        var original = $"NEWTIN-PERMISSION-TEST {id} {DateTime.UtcNow:O}";
        var modified = original + Environment.NewLine + $"MODIFICADO {Guid.NewGuid():N}";
        var folderCreated = false;
        var fileCreated = false;

        try
        {
            var rootMissing = false;
            try
            {
                _ = Directory.EnumerateFileSystemEntries(rootPath).FirstOrDefault();
                steps.Add(new PermissionStep(StepList, PermissionArea.Read, PermissionOutcome.Passed,
                    "Se pudo ver el contenido de la carpeta."));
            }
            catch (DirectoryNotFoundException)
            {
                rootMissing = true;
                steps.Add(new PermissionStep(StepList, PermissionArea.Read, PermissionOutcome.Failed,
                    "La carpeta no existe o no se encuentra la ruta. No se creó nada."));
            }
            catch (Exception ex) when (IsFileSystemFailure(ex))
            {
                steps.Add(new PermissionStep(StepList, PermissionArea.Read, PermissionOutcome.Failed, Describe(ex)));
            }

            ct.ThrowIfCancellationRequested();

            if (rootMissing)
            {
                steps.AddRange(NotTested("La carpeta raíz no se pudo abrir.",
                    (StepCreateFolder, PermissionArea.Write), (StepCreateFile, PermissionArea.Write),
                    (StepRead, PermissionArea.Read), (StepModify, PermissionArea.Modify),
                    (StepVerify, PermissionArea.Modify), ("Eliminar archivos de prueba", PermissionArea.Delete)));
            }
            else
            {
                steps.Add(Attempt(StepCreateFolder, PermissionArea.Write, () =>
                {
                    if (Directory.Exists(folder))
                        throw new IOException("Ya existe una carpeta con ese nombre; se canceló para no tocarla.");
                    Directory.CreateDirectory(folder);
                    folderCreated = true;
                }, "Se pudo crear la carpeta temporal."));

                if (!folderCreated)
                {
                    steps.AddRange(NotTested("No se pudo crear la carpeta de prueba.",
                        (StepCreateFile, PermissionArea.Write), (StepRead, PermissionArea.Read),
                        (StepModify, PermissionArea.Modify), (StepVerify, PermissionArea.Modify),
                        ("Eliminar archivos de prueba", PermissionArea.Delete)));
                }
                else
                {
                    ct.ThrowIfCancellationRequested();
                    steps.Add(Attempt(StepCreateFile, PermissionArea.Write, () =>
                    {
                        File.WriteAllText(file, original, Utf8);
                        fileCreated = true;
                    }, "Se pudo crear el archivo con contenido identificable."));

                    if (!fileCreated)
                    {
                        steps.AddRange(NotTested("No se pudo crear el archivo de prueba.",
                            (StepRead, PermissionArea.Read), (StepModify, PermissionArea.Modify),
                            (StepVerify, PermissionArea.Modify), ("Eliminar archivos de prueba", PermissionArea.Delete)));
                    }
                    else
                    {
                        ct.ThrowIfCancellationRequested();
                        steps.Add(Attempt(StepRead, PermissionArea.Read, () =>
                        {
                            if (File.ReadAllText(file, Utf8) != original)
                                throw new IOException("El contenido leído no coincide con el escrito.");
                        }, "El contenido leído coincide con el escrito."));

                        ct.ThrowIfCancellationRequested();
                        var modify = Attempt(StepModify, PermissionArea.Modify,
                            () => File.WriteAllText(file, modified, Utf8),
                            "Se pudo modificar el archivo.");
                        steps.Add(modify);

                        if (modify.Outcome == PermissionOutcome.Passed)
                        {
                            steps.Add(Attempt(StepVerify, PermissionArea.Modify, () =>
                            {
                                if (File.ReadAllText(file, Utf8) != modified)
                                    throw new IOException("El archivo no refleja la modificación.");
                            }, "La modificación quedó guardada realmente."));
                        }
                        else
                        {
                            steps.AddRange(NotTested("No se pudo modificar el archivo.",
                                (StepVerify, PermissionArea.Modify)));
                        }
                    }
                }
            }
        }
        finally
        {
            // La limpieza siempre se intenta, incluso si se cancela. Nunca se borra de forma recursiva.
            if (fileCreated)
                steps.Add(Attempt("Eliminar el archivo de prueba", PermissionArea.Delete,
                    () => File.Delete(file), "Archivo temporal eliminado.", $"Elimínalo manualmente: {file}"));

            if (folderCreated)
                steps.Add(Attempt("Eliminar la carpeta de prueba", PermissionArea.Delete,
                    () => Directory.Delete(folder, recursive: false), "Carpeta temporal eliminada.",
                    $"Elimínala manualmente: {folder}"));
        }

        var report = new PermissionTestReport(rootPath, steps);
        _log.Info($"Prueba de permisos en {rootPath}: lectura={report.Read}, escritura={report.Write}, " +
                  $"modificación={report.Modify}, eliminación={report.Delete}.");
        return report;
    }

    private static PermissionStep Attempt(string name, PermissionArea area, Action action, string success, string hint = "")
    {
        try
        {
            action();
            return new PermissionStep(name, area, PermissionOutcome.Passed, success);
        }
        catch (Exception ex) when (IsFileSystemFailure(ex))
        {
            return new PermissionStep(name, area, PermissionOutcome.Failed, $"{Describe(ex)} {hint}".Trim());
        }
    }

    private static IEnumerable<PermissionStep> NotTested(string reason, params (string Name, PermissionArea Area)[] items) =>
        items.Select(i => new PermissionStep(i.Name, i.Area, PermissionOutcome.NotTested, reason));

    private static bool IsFileSystemFailure(Exception ex) =>
        ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException;

    private static string Describe(Exception ex) => ex is UnauthorizedAccessException
        ? "Acceso denegado: esta cuenta no tiene permiso para esta operación."
        : ex.Message;
}