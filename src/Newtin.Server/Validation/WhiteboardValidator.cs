using System.Text;
using System.Text.RegularExpressions;
using Newtin.Server.Models;
using static Newtin.Server.Validation.WhiteboardLimits;

namespace Newtin.Server.Validation;

public static partial class WhiteboardValidator
{
    // \A ... \z (y no ^ ... $) para que un salto de línea final no se cuele como válido.
    [GeneratedRegex(@"\A[A-Za-z0-9_-]{1,64}\z")]
    private static partial Regex IdPattern();

    [GeneratedRegex(@"\A#[0-9A-Fa-f]{6}\z")]
    private static partial Regex ColorPattern();

    [GeneratedRegex(@"\A[\p{L}\p{N} _.\-]{1,30}\z")]
    private static partial Regex NamePattern();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    public static string? ValidateId(string? id) =>
        id is not null && IdPattern().IsMatch(id)
            ? null
            : "El identificador no es válido (letras, números, guion y guion bajo; máximo 64).";

    public static string? ValidateColor(string? color) =>
        color is not null && ColorPattern().IsMatch(color) ? null : "El color debe tener el formato #RRGGBB.";

    public static string? ValidatePosition(double x, double y) =>
        IsInRange(x) && IsInRange(y) ? null : "La posición está fuera de los límites de la pizarra.";

    public static string? ValidateWidth(double? width) =>
        width is null || (double.IsFinite(width.Value) && width.Value is >= MinStrokeWidth and <= MaxStrokeWidth)
            ? null
            : "El grosor debe estar entre 1 y 64.";

    public static string? ValidatePoints(IReadOnlyList<PointD>? points, int maxPoints)
    {
        if (points is null || points.Count == 0) return "El trazo no tiene puntos.";
        if (points.Count > maxPoints) return $"El trazo supera el máximo de {maxPoints} puntos.";

        foreach (var point in points)
            if (!IsInRange(point.X) || !IsInRange(point.Y))
                return "Un punto del trazo está fuera de los límites de la pizarra.";

        return null;
    }

    public static bool TryNormalizeDisplayName(string? input, out string name, out string error)
    {
        name = Whitespace().Replace((input ?? "").Trim(), " ");
        if (NamePattern().IsMatch(name))
        {
            error = "";
            return true;
        }

        error = "El nombre debe tener entre 1 y 30 caracteres: letras, números, espacios, punto, guion o guion bajo.";
        return false;
    }

    public static bool TryNormalizeCategoryName(string? input, out string name, out string error)
    {
        name = Whitespace().Replace(NormalizeText(input).Trim(), " ");
        if (name.Length is >= 1 and <= MaxCategoryNameLength)
        {
            error = "";
            return true;
        }

        error = $"El nombre de la categoría debe tener entre 1 y {MaxCategoryNameLength} caracteres.";
        return false;
    }

    /// <summary>Unifica los saltos de línea y elimina caracteres de control (salvo salto de línea y tabulación).</summary>
    public static string NormalizeText(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";

        var builder = new StringBuilder(text.Length);
        foreach (var c in text.ReplaceLineEndings("\n"))
            if (c is '\n' or '\t' || !char.IsControl(c))
                builder.Append(c);

        return builder.ToString();
    }

    public static string? ValidateFontSize(double? size) =>
        size is null || (double.IsFinite(size.Value) && size.Value is >= MinFontSize and <= MaxFontSize)
            ? null
            : "El tamaño del texto debe estar entre 8 y 120.";

    private static bool IsInRange(double value) => double.IsFinite(value) && Math.Abs(value) <= MaxCoordinate;
}