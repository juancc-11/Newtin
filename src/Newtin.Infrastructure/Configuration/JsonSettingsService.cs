using System.Text.Json;
using Newtin.Core.Interfaces;
using Newtin.Core.Models;
using Newtin.Core.Validation;

namespace Newtin.Infrastructure.Configuration;

public sealed class JsonSettingsService : ISettingsService
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    private readonly string _path;
    private readonly ILogService _log;

    public JsonSettingsService(string path, ILogService log)
    {
        _path = path;
        _log = log;
    }

    public AppSettings Current { get; private set; } = new();
    public string? LoadWarning { get; private set; }

    public void Load()
    {
        Current = new AppSettings();
        LoadWarning = null;

        if (!File.Exists(_path)) return;

        try
        {
            var loaded = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_path), Options);
            if (loaded is null) throw new JsonException("El archivo está vacío.");

            loaded.Network ??= new NetworkSettings();
            loaded.WhiteboardServer ??= new ServerSettings();
            Current = loaded;

            var errors = SettingsValidator.Validate(Current);
            if (errors.Count > 0)
            {
                LoadWarning = "La configuración guardada tiene valores no válidos. Corrígelos en Configuración: "
                              + string.Join(" ", errors);
                _log.Warning(LoadWarning);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            Current = new AppSettings();
            LoadWarning = "No se pudo leer la configuración guardada; se usaron los valores predeterminados.";
            _log.Warning($"{LoadWarning} Motivo: {ex.Message}");
        }
    }

    public bool TrySave(AppSettings settings, out string error)
    {
        var errors = SettingsValidator.Validate(settings);
        if (errors.Count > 0)
        {
            error = string.Join(" ", errors);
            return false;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);

            // Se escribe a un archivo temporal y luego se reemplaza, para no dejar el JSON a medias.
            var temp = _path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(settings, Options));
            File.Move(temp, _path, overwrite: true);

            Current = settings.Clone();
            LoadWarning = null;
            _log.Info("Configuración guardada.");
            error = "";
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Error("No se pudo guardar la configuración.", ex);
            error = $"No se pudo guardar la configuración: {ex.Message}";
            return false;
        }
    }
}