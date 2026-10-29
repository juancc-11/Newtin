using System.Text.Json;
using Newtin.Core.Models;
using Newtin.Diagnostics.Events;
using Newtin.Diagnostics.Reports;

namespace Newtin.Diagnostics.Tests;

public class ReportsTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 14, 30, 5, TimeSpan.Zero);

    private static ModuleResult Result(string key, params InfoItem[] items) =>
        new(key, "Módulo " + key, Now, [new InfoSection("Datos", items)]);

    private static ReportContext Context(bool redactMac = true) =>
        new("PC1", "0.1.0", "192.168.100.10", "Velocidad", Now, redactMac);

    private static DiagnosticReport Build(params ModuleResult[] results) =>
        ReportBuilder.Build(Context(), results, ["Eventos del sistema"]);

    // ---------- limpieza de secretos ----------

    [Theory]
    [InlineData("Authorization: Bearer abcdef1234567890xyz", "abcdef1234567890xyz")]
    [InlineData("password=12345 y más", "12345")]
    [InlineData("contraseña: secretaXYZ", "secretaXYZ")]
    [InlineData("https://host/?access_token=TOKEN987", "TOKEN987")]
    [InlineData("clave = abc123", "abc123")]
    public void Sanitizer_HidesSecrets(string input, string secret)
    {
        var clean = ReportSanitizer.Clean(input, false);

        Assert.DoesNotContain(secret, clean);
        Assert.Contains("[oculto]", clean);
    }

    [Fact]
    public void Sanitizer_RedactsMacAddresses_OnlyWhenAsked()
    {
        const string text = "MAC 00-1A-2B-3C-4D-5E";

        Assert.DoesNotContain("00-1A", ReportSanitizer.Clean(text, true));
        Assert.Contains("[MAC oculta]", ReportSanitizer.Clean(text, true));
        Assert.Contains("00-1A-2B-3C-4D-5E", ReportSanitizer.Clean(text, false));
    }

    [Fact]
    public void Sanitizer_RemovesControlCharacters_AndCapsTheLength()
    {
        Assert.Equal("ab", ReportSanitizer.Clean("a\u0007b", false));
        Assert.Equal("", ReportSanitizer.Clean(null, false));

        var long5000 = ReportSanitizer.Clean(new string('x', 5000), false);
        Assert.Equal(ReportSanitizer.MaxLength, long5000.Length);
        Assert.EndsWith("…", long5000);
    }

    // ---------- contenido del informe ----------

    [Fact]
    public void Report_ExcludesSectionsMarkedAsNotForReports()
    {
        var result = new ModuleResult("events", "Eventos", Now,
        [
            new InfoSection("Resumen", [new InfoItem("Errores", "2")]),
            new InfoSection("Eventos más recientes", [new InfoItem("12/10 · Sistema", "Error — Disk (ID 7)", "mensaje privado del equipo")],
                IncludeInReport: false)
        ]);

        var report = Build(result);

        Assert.Single(report.Modules[0].Sections);
        Assert.DoesNotContain("mensaje privado", TextReportFormatter.Format(report));
        Assert.DoesNotContain("mensaje privado", JsonReportFormatter.Format(report));
    }

    [Fact]
    public void Report_NeverContainsAPlantedSecret_InAnyFormat()
    {
        var report = Build(Result("shares", new InfoItem("Detalle", "contraseña: 12345", "token=abc123def")));

        Assert.DoesNotContain("12345", TextReportFormatter.Format(report));
        Assert.DoesNotContain("12345", JsonReportFormatter.Format(report));
        Assert.DoesNotContain("abc123def", TextReportFormatter.Format(report));
        Assert.DoesNotContain("abc123def", JsonReportFormatter.Format(report));
    }

    [Theory]
    [InlineData(InfoLevel.Error, "error")]
    [InlineData(InfoLevel.Warning, "warning")]
    [InlineData(InfoLevel.Success, "ok")]
    [InlineData(InfoLevel.Normal, "info")]
    public void Status_FollowsTheItemLevel(InfoLevel level, string expected) =>
        Assert.Equal(expected, Build(Result("ports", new InfoItem("A", "x", null, null, level))).Modules[0].Status);

    [Fact]
    public void Status_TheWorstLevelWins()
    {
        var module = Build(Result("ports",
            new InfoItem("A", "x", null, null, InfoLevel.Success),
            new InfoItem("B", "y", null, null, InfoLevel.Warning))).Modules[0];

        Assert.Equal("warning", module.Status);
    }

    [Fact]
    public void Summary_CountsEachStatus_AndKeepsTheNotRunList()
    {
        var report = Build(
            Result("system", new InfoItem("A", "x", null, null, InfoLevel.Success)),
            Result("storage", new InfoItem("A", "x", null, null, InfoLevel.Warning)),
            Result("ports", new InfoItem("A", "x", null, null, InfoLevel.Error)));

        Assert.Equal((3, 1, 1, 1), (report.Summary.Modules, report.Summary.Ok, report.Summary.Warnings, report.Summary.Errors));
        Assert.Equal(["Eventos del sistema"], report.NotRun);
    }

    // ---------- recomendaciones ----------

    [Fact]
    public void Recommendations_StorageNearlyFull()
    {
        var advice = Recommendations.For(Result("storage", new InfoItem("Porcentaje utilizado", "95%", null, 95)));

        Assert.Contains(advice, a => a.Contains("90 %"));
    }

    [Fact]
    public void Recommendations_MemoryNearTheLimit()
    {
        var advice = Recommendations.For(Result("hardware", new InfoItem("En uso", "7 GB (93%)", null, 93)));

        Assert.Contains(advice, a => a.Contains("RAM"));
    }

    [Fact]
    public void Recommendations_ReadableButNotWritable()
    {
        var advice = Recommendations.For(Result("permissions",
            new InfoItem("Lectura", "Correcto"), new InfoItem("Escritura", "Falló")));

        Assert.Contains(advice, a => a.Contains("leer pero no escribir"));
    }

    [Theory]
    [InlineData("Credenciales incorrectas", "usuario y la contraseña")]
    [InlineData("Equipo inaccesible", "conectividad")]
    [InlineData("La carpeta compartida no existe", "net share")]
    [InlineData("Conflicto con una sesión existente", "net use")]
    public void Recommendations_ForShareProblems(string state, string expected)
    {
        var advice = Recommendations.For(Result("shares", new InfoItem("Estado", state, null, null, InfoLevel.Error)));

        Assert.Contains(advice, a => a.Contains(expected));
    }

    [Fact]
    public void Recommendations_CriticalEvents()
    {
        var advice = Recommendations.For(Result("events",
            new InfoItem("Críticos", "2"), new InfoItem("Errores", "0")));

        Assert.Single(advice);
        Assert.Contains("eventvwr.msc", advice[0]);
    }

    [Fact]
    public void Recommendations_AHealthyModule_HasNone() =>
        Assert.Empty(Recommendations.For(Result("storage", new InfoItem("Porcentaje utilizado", "40%", null, 40))));

    [Fact]
    public void Recommendations_AnUnknownError_GetsAGenericHint()
    {
        var advice = Recommendations.For(Result("system", new InfoItem("A", "x", null, null, InfoLevel.Error)));

        Assert.Single(advice);
    }

    // ---------- formatos ----------

    [Fact]
    public void Text_HasTheExpectedStructure_AndUsesWindowsLineBreaks()
    {
        var report = Build(Result("connectivity",
            new InfoItem("Resumen", "Sin respuesta", "Detalle\ncon dos líneas", null, InfoLevel.Error)));

        var text = TextReportFormatter.Format(report);

        Assert.Contains("NEWTIN — Informe de diagnóstico", text);
        Assert.Contains("Equipo: PC1", text);
        Assert.Contains("192.168.100.10", text);
        Assert.Contains("[ERROR] Módulo connectivity", text);
        Assert.Contains("Sin ejecutar en esta sesión: Eventos del sistema", text);
        Assert.Contains("Recomendaciones:", text);
        Assert.DoesNotContain("\n", text.Replace("\r\n", ""));
    }

    [Fact]
    public void Json_IsValid_KeepsAccents_AndEscapesMarkup()
    {
        var report = Build(Result("ports", new InfoItem("TCP 445", "Conexión <script> establecida", null, null, InfoLevel.Success)));

        var json = JsonReportFormatter.Format(report);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.Equal("Newtin", root.GetProperty("application").GetString());
        Assert.Equal(1, root.GetProperty("summary").GetProperty("ok").GetInt32());
        Assert.Equal("ports", root.GetProperty("results")[0].GetProperty("module").GetString());
        Assert.Contains("Conexión", json);
        Assert.DoesNotContain("<script>", json);
    }

    [Fact]
    public void Html_IsReadable_EscapesMarkup_AndIncludesPlainLanguageSummary()
    {
        var report = Build(Result("ports",
            new InfoItem("TCP 445", "Conexión <script> establecida", "Revisa <el firewall>", null, InfoLevel.Warning)));

        var html = HtmlReportFormatter.Format(report);

        Assert.Contains("<html lang=\"es\">", html);
        Assert.Contains("Resumen general", html);
        Assert.Contains("Algunos resultados conviene revisarlos.", html);
        Assert.Contains("Revisar", html);
        Assert.Contains("Conexión &lt;script&gt; establecida", html);
        Assert.Contains("Revisa &lt;el firewall&gt;", html);
        Assert.DoesNotContain("<script>", html);
    }

    [Fact]
    public void FileNames_AreStable_AndAnyCulture()
    {
        Assert.Equal("informe-20261027-143005.txt", ReportNaming.FileName(Now, ReportFormat.Txt));
        Assert.Equal("informe-20261027-143005.json", ReportNaming.FileName(Now, ReportFormat.Json));
        Assert.Equal("informe-20261027-143005.html", ReportNaming.FileName(Now, ReportFormat.Html));
    }

    // ---------- sesión ----------

    [Fact]
    public void Session_KeepsTheLatestResultPerModule_InMenuOrder()
    {
        var session = new ReportSession(["system", "storage", "ports"]);
        var changes = 0;
        session.Changed += () => changes++;

        session.Record(Result("ports"));
        session.Record(Result("system"));
        session.Record(new ModuleResult("system", "Nuevo", Now.AddMinutes(1), []));
        session.Record(Result("desconocido"));

        Assert.Equal(["system", "ports", "desconocido"], session.All().Select(r => r.Key));
        Assert.Equal("Nuevo", session.All()[0].Title);
        Assert.Equal(4, changes);

        session.Clear();
        Assert.Empty(session.All());
    }

    [Fact]
    public void TheEventsMessagesSection_IsNeverMeantForReports()
    {
        var events = new[]
        {
            new EventSummary(Now, "System", EventSeverity.Error, "Disk", 7, "Texto del mensaje")
        };

        var sections = EventFormatter.ToSections(events, false, [], new EventQueryOptions(TimeSpan.FromHours(24), false));

        Assert.True(sections[0].IncludeInReport);
        Assert.False(sections.Single(s => s.Title.StartsWith("Eventos más recientes")).IncludeInReport);
    }
}