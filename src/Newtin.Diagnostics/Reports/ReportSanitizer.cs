using System.Text.RegularExpressions;

namespace Newtin.Diagnostics.Reports;

/// <summary>Última barrera antes de escribir un informe: enmascara cualquier cosa que parezca un secreto.</summary>
public static partial class ReportSanitizer
{
    public const int MaxLength = 2000;

    [GeneratedRegex(@"(?i)\bbearer\s+[A-Za-z0-9\-._~+/]{8,}=*")]
    private static partial Regex BearerPattern();

    [GeneratedRegex(@"(?i)\b(access[_-]?token|token|password|passwd|pwd|contraseña|clave|secret|secreto|api[_-]?key)\b\s*([=:])\s*\S+")]
    private static partial Regex SecretPattern();

    [GeneratedRegex(@"\b[0-9A-Fa-f]{2}(?:[-:][0-9A-Fa-f]{2}){5}\b")]
    private static partial Regex MacPattern();

    public static string Clean(string? text, bool redactMac)
    {
        if (string.IsNullOrEmpty(text)) return "";

        var clean = new string(text.Where(c => !char.IsControl(c) || c is '\n' or '\t').ToArray());
        clean = BearerPattern().Replace(clean, "Bearer [oculto]");
        clean = SecretPattern().Replace(clean, "$1$2 [oculto]");
        if (redactMac) clean = MacPattern().Replace(clean, "[MAC oculta]");

        return clean.Length <= MaxLength ? clean : clean[..(MaxLength - 1)] + "…";
    }
}