using Newtin.Core.Models;

namespace Newtin.Diagnostics.Reports;

public enum ReportFormat { Txt, Json, Html }

/// <summary>Resultado de la última ejecución de un diagnóstico en esta sesión.</summary>
public sealed record ModuleResult(string Key, string Title, DateTimeOffset Time, IReadOnlyList<InfoSection> Sections);

public sealed record ReportContext(
    string MachineName,
    string AppVersion,
    string? RemoteHost,
    string? RemoteShare,
    DateTimeOffset GeneratedAt,
    bool RedactMac);

public sealed record ReportItem(string Label, string Value, string? Note, string Level, double? Percent);

public sealed record ReportSection(string Title, IReadOnlyList<ReportItem> Items);

public sealed record ReportModule(
    string Key,
    string Title,
    DateTimeOffset ExecutedAt,
    string Status,
    IReadOnlyList<ReportSection> Sections,
    IReadOnlyList<string> Recommendations);

public sealed record ReportSummary(int Modules, int Ok, int Warnings, int Errors);

public sealed record DiagnosticReport(
    ReportContext Context,
    ReportSummary Summary,
    IReadOnlyList<ReportModule> Modules,
    IReadOnlyList<string> NotRun);