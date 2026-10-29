namespace Newtin.Core.Models;

public enum InfoLevel { Normal, Success, Warning, Error }

/// <summary>Un dato mostrado al usuario. Si no se pudo obtener, Value es "No disponible" y Note explica por qué.</summary>
public sealed record InfoItem(
    string Label,
    string Value,
    string? Note = null,
    double? Percent = null,
    InfoLevel Level = InfoLevel.Normal)
{
    public const string NotAvailable = "No disponible";

    public static InfoItem Read(string label, Func<string?> read, string? note = null)
    {
        try
        {
            var value = read();
            return string.IsNullOrWhiteSpace(value)
                ? new InfoItem(label, NotAvailable, "Windows no devolvió este dato.")
                : new InfoItem(label, value, note);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new InfoItem(label, NotAvailable, $"No se pudo consultar: {ex.Message}");
        }
    }
}

/// <param name="IncludeInReport">false para secciones con datos que no deben salir en los informes (por ejemplo, textos de mensajes).</param>
public sealed record InfoSection(string Title, IReadOnlyList<InfoItem> Items, bool IncludeInReport = true);