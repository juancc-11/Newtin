using System.Security;

namespace Newtin.Server.Persistence;

public sealed record FileWriteResult(bool Ok, string? Error);

public sealed record FileReadResult(byte[]? Data, string? Error);

public static class AtomicFile
{
    /// <summary>Escribe en un archivo temporal y lo reemplaza al final: un fallo nunca deja el archivo a medias.</summary>
    public static FileWriteResult Write(string path, byte[] data, bool createDirectory = false)
    {
        string fullPath;
        try
        {
            if (string.IsNullOrWhiteSpace(path)) return Fail("No se indicó la ruta del archivo.");
            fullPath = Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException or SecurityException)
        {
            return Fail("La ruta del archivo no es válida.");
        }

        var directory = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrEmpty(directory) || string.IsNullOrEmpty(Path.GetFileName(fullPath)))
            return Fail("La ruta del archivo no es válida.");
        if (Directory.Exists(fullPath))
            return Fail("La ruta indicada es una carpeta, no un archivo.");

        var temp = fullPath + ".tmp";
        try
        {
            if (createDirectory) Directory.CreateDirectory(directory);
            else if (!Directory.Exists(directory)) return Fail("La carpeta de destino no existe.");

            File.WriteAllBytes(temp, data);
            File.Move(temp, fullPath, overwrite: true);
            return new FileWriteResult(true, null);
        }
        catch (UnauthorizedAccessException)
        {
            DeleteQuietly(temp);
            return Fail("Windows no permite escribir en esa ubicación, o el archivo es de solo lectura.");
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or SecurityException)
        {
            DeleteQuietly(temp);
            return Fail($"No se pudo guardar el archivo: {ex.Message}");
        }
    }

    public static FileReadResult Read(string path, long maxBytes)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists) return new FileReadResult(null, "El archivo no existe.");
            if (info.Length > maxBytes)
                return new FileReadResult(null, $"El archivo es demasiado grande (máximo {maxBytes / (1024 * 1024)} MB).");

            return new FileReadResult(File.ReadAllBytes(path), null);
        }
        catch (UnauthorizedAccessException)
        {
            return new FileReadResult(null, "Windows no permite leer ese archivo.");
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or NotSupportedException or SecurityException)
        {
            return new FileReadResult(null, $"No se pudo leer el archivo: {ex.Message}");
        }
    }

    private static FileWriteResult Fail(string error) => new(false, error);

    private static void DeleteQuietly(string path)
    {
        try { File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* se limpia al próximo guardado */ }
    }
}