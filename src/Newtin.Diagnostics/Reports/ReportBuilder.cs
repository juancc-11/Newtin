using Newtin.Core.Models;

namespace Newtin.Diagnostics.Reports;

public static class ReportModules
{
    /// <summary>Diagnósticos que pueden aparecer en un informe, en el orden del menú.</summary>
    public static readonly string[] Keys =
        ["system", "hardware", "storage", "network", "connectivity", "ports", "shares", "permissions", "events", "battery"];
}

public static class ReportBuilder
{
    public static string LevelName(InfoLevel level) => level switch
    {
        InfoLevel.Success => "ok",
        InfoLevel.Warning => "warning",
        InfoLevel.Error => "error",
        _ => "info"
    };

    /// <summary>El peor nivel de las secciones que entran en el informe.</summary>
    public static string StatusOf(IReadOnlyList<InfoSection> sections)
    {
        var levels = sections.Where(s => s.IncludeInReport).SelectMany(s => s.Items).Select(i => i.Level).ToList();

        return levels.Contains(InfoLevel.Error) ? "error"
            : levels.Contains(InfoLevel.Warning) ? "warning"
            : levels.Contains(InfoLevel.Success) ? "ok"
            : "info";
    }

    public static DiagnosticReport Build(
        ReportContext context, IReadOnlyList<ModuleResult> results, IReadOnlyList<string> notRun)
    {
        var safeContext = context with
        {
            MachineName = ReportSanitizer.Clean(context.MachineName, false),
            AppVersion = ReportSanitizer.Clean(context.AppVersion, false),
            RemoteHost = string.IsNullOrWhiteSpace(context.RemoteHost) ? null : ReportSanitizer.Clean(context.RemoteHost, false),
            RemoteShare = string.IsNullOrWhiteSpace(context.RemoteShare) ? null : ReportSanitizer.Clean(context.RemoteShare, false)
        };

        var modules = results.Select(r => ToModule(r, context.RedactMac)).ToList();
        var summary = new ReportSummary(
            modules.Count,
            modules.Count(m => m.Status == "ok"),
            modules.Count(m => m.Status == "warning"),
            modules.Count(m => m.Status == "error"));

        return new DiagnosticReport(safeContext, summary, modules, notRun);
    }

    private static ReportModule ToModule(ModuleResult result, bool redactMac)
    {
        var sections = result.Sections
            .Where(s => s.IncludeInReport)
            .Select(s => new ReportSection(
                ReportSanitizer.Clean(s.Title, redactMac),
                s.Items.Select(i => new ReportItem(
                    ReportSanitizer.Clean(i.Label, redactMac),
                    ReportSanitizer.Clean(i.Value, redactMac),
                    string.IsNullOrWhiteSpace(i.Note) ? null : ReportSanitizer.Clean(i.Note, redactMac),
                    LevelName(i.Level),
                    i.Percent)).ToList()))
            .ToList();

        return new ReportModule(
            result.Key,
            ReportSanitizer.Clean(result.Title, false),
            result.Time,
            StatusOf(result.Sections),
            sections,
            Recommendations.For(result));
    }
}