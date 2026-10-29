using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;

namespace Newtin.Diagnostics.Reports;

public static class ReportNaming
{
    public static string FileName(DateTimeOffset now, ReportFormat format) =>
        $"informe-{now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}.{format switch
        {
            ReportFormat.Json => "json",
            ReportFormat.Html => "html",
            _ => "txt"
        }}";
}

public static class HtmlReportFormatter
{
    private static readonly HtmlEncoder Encoder = HtmlEncoder.Create(UnicodeRanges.All);

    private static string E(string? value) => Encoder.Encode(value ?? "");

    private static string StatusLabel(string status) => status switch
    {
        "ok" => "Correcto",
        "warning" => "Revisar",
        "error" => "Necesita atención",
        _ => "Informativo"
    };

    public static string Format(DiagnosticReport report)
    {
        var context = report.Context;
        var summary = report.Summary;
        var headline = summary.Errors > 0
            ? "Hay resultados que necesitan atención."
            : summary.Warnings > 0
                ? "Algunos resultados conviene revisarlos."
                : "No se detectaron problemas en los módulos ejecutados.";
        var generated = context.GeneratedAt.ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture);

        var sb = new StringBuilder();
        sb.Append("""
            <!doctype html>
            <html lang="es">
            <head>
              <meta charset="utf-8">
              <meta name="viewport" content="width=device-width, initial-scale=1">
              <title>Informe de Newtin</title>
              <style>
                :root{color-scheme:light;--ink:#211b2a;--muted:#665f70;--line:#e4e0e9;--brand:#8a277e;--bg:#f6f4f8;--green:#176b43;--green-bg:#e4f5ec;--amber:#855700;--amber-bg:#fff2d9;--red:#b42332;--red-bg:#fce7e8}
                *{box-sizing:border-box}body{margin:0;background:var(--bg);color:var(--ink);font:15px/1.55 "Segoe UI",Arial,sans-serif}
                main{max-width:980px;margin:32px auto;padding:0 20px}.hero{background:linear-gradient(125deg,#5420a8,#a00082);color:white;padding:30px;border-radius:18px}
                h1{margin:0;font-size:30px}.hero p{margin:8px 0 0;color:#f5eafa}.meta{display:flex;flex-wrap:wrap;gap:8px;margin-top:22px}
                .meta span{background:#ffffff20;border:1px solid #ffffff35;border-radius:20px;padding:5px 11px;font-size:13px}
                section,.module{background:white;border:1px solid var(--line);border-radius:14px;padding:22px;margin-top:18px}
                h2{font-size:19px;margin:0 0 12px}h3{font-size:17px;margin:0}.summary{display:grid;grid-template-columns:repeat(auto-fit,minmax(140px,1fr));gap:10px}
                .metric{border:1px solid var(--line);border-radius:10px;padding:13px}.metric strong{display:block;font-size:24px}.metric span{color:var(--muted);font-size:13px}
                .headline{padding:12px 14px;border-radius:10px;background:#f0edf4;margin-bottom:14px;font-weight:600}
                .status{display:inline-block;border-radius:20px;padding:3px 10px;font-size:12px;font-weight:700}
                .ok{background:var(--green-bg);color:var(--green)}.warning{background:var(--amber-bg);color:var(--amber)}.error{background:var(--red-bg);color:var(--red)}.info{background:#f0edf4;color:var(--muted)}
                .module-head{display:flex;align-items:center;justify-content:space-between;gap:12px}.module time{color:var(--muted);font-size:12px}
                .group{margin-top:18px}.group h4{margin:0 0 8px;color:var(--muted);font-size:13px;text-transform:uppercase;letter-spacing:.04em}
                .item{display:grid;grid-template-columns:minmax(150px, .75fr) 1.5fr;gap:12px;padding:10px 0;border-top:1px solid var(--line)}
                .label{font-weight:600}.value{overflow-wrap:anywhere}.note{grid-column:2;color:var(--muted);font-size:13px;margin-top:-8px}
                .advice{background:#f8f6fb;border-left:3px solid var(--brand);padding:10px 13px;margin:8px 0;border-radius:0 8px 8px 0}
                .not-run{color:var(--muted);padding-left:20px}footer{color:var(--muted);font-size:12px;margin:20px 2px}
                @media(max-width:600px){main{margin:14px auto;padding:0 12px}.hero{padding:22px}.item{grid-template-columns:1fr;gap:2px}.note{grid-column:1;margin-top:0}.module-head{align-items:flex-start;flex-direction:column}}
                @media print{body{background:white}.hero{print-color-adjust:exact;-webkit-print-color-adjust:exact}main{max-width:none;margin:0}.module,section{break-inside:avoid;box-shadow:none}}
              </style>
            </head>
            <body><main>
            """);

        sb.Append("<header class=\"hero\"><h1>Informe de diagnóstico</h1><p>")
            .Append(E(headline))
            .Append("</p><div class=\"meta\"><span>Equipo: ").Append(E(context.MachineName))
            .Append("</span><span>Generado: ").Append(E(generated))
            .Append("</span><span>Newtin ").Append(E(context.AppVersion)).Append("</span>");
        if (context.RemoteHost is { } host)
        {
            sb.Append("<span>Equipo remoto: ").Append(E(host));
            if (context.RemoteShare is { } share) sb.Append(" · ").Append(E(share));
            sb.Append("</span>");
        }

        sb.Append("</div></header><section><h2>Resumen general</h2><div class=\"summary\">")
            .Append(Metric(summary.Modules, "módulos incluidos"))
            .Append(Metric(summary.Ok, "correctos"))
            .Append(Metric(summary.Warnings, "para revisar"))
            .Append(Metric(summary.Errors, "con errores"))
            .Append("</div>");
        if (report.NotRun.Count > 0)
        {
            sb.Append("<h3 style=\"margin-top:18px\">Módulos no ejecutados</h3><ul class=\"not-run\">");
            foreach (var title in report.NotRun) sb.Append("<li>").Append(E(title)).Append("</li>");
            sb.Append("</ul><p style=\"color:var(--muted);font-size:13px\">Estos módulos no se incluyeron porque no se ejecutaron durante esta sesión.</p>");
        }
        sb.Append("</section>");

        foreach (var module in report.Modules)
        {
            sb.Append("<article class=\"module\"><div class=\"module-head\"><h2>")
                .Append(E(module.Title)).Append("</h2><span class=\"status ").Append(E(module.Status))
                .Append("\">").Append(E(StatusLabel(module.Status))).Append("</span></div><time>Consultado: ")
                .Append(E(module.ExecutedAt.ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture)))
                .Append("</time>");

            foreach (var section in module.Sections)
            {
                sb.Append("<div class=\"group\"><h4>").Append(E(section.Title)).Append("</h4>");
                foreach (var item in section.Items)
                {
                    sb.Append("<div class=\"item\"><span class=\"label\">").Append(E(item.Label))
                        .Append("</span><span class=\"value\">").Append(E(item.Value)).Append("</span>");
                    if (item.Note is not null)
                        sb.Append("<span class=\"note\">").Append(E(item.Note)).Append("</span>");
                    if (item.Percent is { } percent)
                        sb.Append("<span class=\"note\">Indicador: ").Append(E(percent.ToString("0.#", CultureInfo.InvariantCulture))).Append("%</span>");
                    sb.Append("</div>");
                }

                sb.Append("</div>");
            }

            if (module.Recommendations.Count > 0)
            {
                sb.Append("<div class=\"group\"><h4>Qué puedes hacer</h4>");
                foreach (var recommendation in module.Recommendations)
                    sb.Append("<p class=\"advice\">").Append(E(recommendation)).Append("</p>");
                sb.Append("</div>");
            }

            sb.Append("</article>");
        }

        sb.Append("<footer>Este informe no contiene contraseñas, códigos de acceso ni mensajes de eventos. ")
            .Append(context.RedactMac ? "Las direcciones MAC se ocultaron." : "")
            .Append("</footer></main></body></html>");
        return sb.ToString();
    }

    private static string Metric(int value, string label) =>
        $"<div class=\"metric\"><strong>{value.ToString(CultureInfo.InvariantCulture)}</strong><span>{E(label)}</span></div>";
}

public static class TextReportFormatter
{
    private static string Stamp(DateTimeOffset time) => time.ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture);

    private static string StatusLabel(string status) => status switch
    {
        "ok" => "CORRECTO",
        "warning" => "ADVERTENCIA",
        "error" => "ERROR",
        _ => "INFORMACIÓN"
    };

    private static string Indent(string text, int spaces) =>
        text.Replace("\r", "").Replace("\n", "\r\n" + new string(' ', spaces));

    public static string Format(DiagnosticReport report)
    {
        var sb = new StringBuilder();
        void Line(string text = "") => sb.Append(text).Append("\r\n");

        var context = report.Context;
        var summary = report.Summary;

        Line("NEWTIN — Informe de diagnóstico");
        Line(new string('=', 64));
        Line($"Fecha:  {Stamp(context.GeneratedAt)}");
        Line($"Equipo: {context.MachineName}");
        if (context.RemoteHost is { } host)
            Line($"Equipo remoto configurado: {host}" + (context.RemoteShare is { } share ? $"  (carpeta compartida: {share})" : ""));
        Line($"Versión de Newtin: {context.AppVersion}");
        Line();
        Line("RESUMEN");
        Line($"  Módulos incluidos: {summary.Modules}  |  Correctos: {summary.Ok}  |  Advertencias: {summary.Warnings}  |  Errores: {summary.Errors}");
        if (report.NotRun.Count > 0)
            Line("  Sin ejecutar en esta sesión: " + string.Join(", ", report.NotRun));
        Line();

        foreach (var module in report.Modules)
        {
            Line(new string('-', 64));
            Line($"[{StatusLabel(module.Status)}] {module.Title}  ({Stamp(module.ExecutedAt)})");

            foreach (var section in module.Sections)
            {
                Line();
                Line($"  {section.Title}");
                foreach (var item in section.Items)
                {
                    Line($"    {item.Label}: {Indent(item.Value, 6)}");
                    if (item.Note is not null) Line($"      Nota: {Indent(item.Note, 12)}");
                }
            }

            if (module.Recommendations.Count > 0)
            {
                Line();
                Line("  Recomendaciones:");
                foreach (var advice in module.Recommendations) Line($"    - {advice}");
            }

            Line();
        }

        Line(new string('-', 64));
        Line("Este informe no incluye contraseñas, códigos de acceso ni el contenido de archivos o mensajes.");
        return sb.ToString();
    }
}

public static class JsonReportFormatter
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        // Deja las tildes legibles; los caracteres especiales de marcado (< > &) siguen escapados.
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
    };

    public static string Format(DiagnosticReport report)
    {
        var context = report.Context;

        var document = new
        {
            application = "Newtin",
            reportVersion = 1,
            generatedAt = context.GeneratedAt,
            appVersion = context.AppVersion,
            machine = context.MachineName,
            remoteHost = context.RemoteHost,
            remoteShare = context.RemoteShare,
            summary = new
            {
                modules = report.Summary.Modules,
                ok = report.Summary.Ok,
                warnings = report.Summary.Warnings,
                errors = report.Summary.Errors
            },
            notRun = report.NotRun,
            results = report.Modules.Select(module => new
            {
                module = module.Key,
                title = module.Title,
                executedAt = module.ExecutedAt,
                status = module.Status,
                recommendations = module.Recommendations,
                sections = module.Sections.Select(section => new
                {
                    title = section.Title,
                    items = section.Items.Select(item => new
                    {
                        label = item.Label,
                        value = item.Value,
                        note = item.Note,
                        level = item.Level,
                        percent = item.Percent
                    })
                })
            })
        };

        return JsonSerializer.Serialize(document, Options);
    }
}