using Newtin.Core.Models;

namespace Newtin.Core.Interfaces;

public interface ISettingsService
{
    AppSettings Current { get; }

    /// <summary>Mensaje si al iniciar el archivo estaba dañado o tenía valores no válidos; null si todo estaba bien.</summary>
    string? LoadWarning { get; }

    bool TrySave(AppSettings settings, out string error);
}