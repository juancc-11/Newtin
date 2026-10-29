using Newtin.Core.Interfaces;
using Newtin.Core.Models;
using Newtin.Diagnostics.Events;

namespace Newtin.Diagnostics.Tests;

public class EventsTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 12, 0, 0, TimeSpan.Zero);

    private static EventSummary Event(string source, EventSeverity severity, int id, int minutesAgo,
        string log = "System", string? message = null) =>
        new(Now.AddMinutes(-minutesAgo), log, severity, source, id, message);

    private sealed class FakeReader(Dictionary<string, EventReadResult> results) : IEventLogReader
    {
        public List<(string Log, TimeSpan Window, bool Warnings)> Calls { get; } = [];

        public EventReadResult Read(string logName, TimeSpan window, bool includeWarnings, int maxEvents,
            int messagesFor, CancellationToken cancellationToken)
        {
            Calls.Add((logName, window, includeWarnings));
            return results[logName];
        }
    }

    [Fact]
    public void Summarize_CountsEachLevel()
    {
        var stats = EventAnalyzer.Summarize(
        [
            Event("A", EventSeverity.Critical, 1, 1),
            Event("A", EventSeverity.Error, 2, 2),
            Event("B", EventSeverity.Error, 3, 3),
            Event("B", EventSeverity.Warning, 4, 4)
        ]);

        Assert.Equal((1, 2, 1), (stats.Critical, stats.Errors, stats.Warnings));
    }

    [Fact]
    public void Summarize_RanksSourcesByCount_AndReportsTheirDetails()
    {
        var stats = EventAnalyzer.Summarize(
        [
            Event("Disk", EventSeverity.Warning, 7, 10),
            Event("Disk", EventSeverity.Error, 11, 5),
            Event("Disk", EventSeverity.Error, 11, 20),
            Event("Tcpip", EventSeverity.Error, 4199, 1)
        ]);

        var top = stats.TopSources[0];
        Assert.Equal("Disk", top.Source);
        Assert.Equal(3, top.Count);
        Assert.Equal(EventSeverity.Error, top.Worst);
        Assert.Equal(11, top.MostFrequentId);
        Assert.Equal(Now.AddMinutes(-5), top.Last);
        Assert.Equal("Tcpip", stats.TopSources[1].Source);
    }

    [Fact]
    public void Summarize_OfNoEvents_IsEmpty()
    {
        var stats = EventAnalyzer.Summarize([]);

        Assert.Equal((0, 0, 0), (stats.Critical, stats.Errors, stats.Warnings));
        Assert.Empty(stats.TopSources);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("  \r\n  ", null)]
    [InlineData("Primera línea\r\nSegunda línea", "Primera línea")]
    [InlineData("Con\u0007control", "Concontrol")]
    public void Shorten_KeepsOnlyTheFirstCleanLine(string? message, string? expected) =>
        Assert.Equal(expected, EventAnalyzer.Shorten(message));

    [Fact]
    public void Shorten_CutsLongMessages()
    {
        var shortened = EventAnalyzer.Shorten(new string('x', 500), 100);

        Assert.Equal(100, shortened!.Length);
        Assert.EndsWith("…", shortened);
    }

    [Theory]
    [InlineData(1, 0, 0, InfoLevel.Error)]
    [InlineData(0, 3, 0, InfoLevel.Warning)]
    [InlineData(0, 0, 2, InfoLevel.Normal)]
    [InlineData(0, 0, 0, InfoLevel.Success)]
    public void Conclude_MatchesTheWorstLevel(int critical, int errors, int warnings, InfoLevel expected) =>
        Assert.Equal(expected, EventFormatter.Conclude(new EventStats(critical, errors, warnings, []), false).Level);

    [Fact]
    public void Conclude_AnIncompleteQuery_NeverReportsPlainSuccess()
    {
        var conclusion = EventFormatter.Conclude(new EventStats(0, 0, 0, []), incomplete: true);

        Assert.Equal(InfoLevel.Warning, conclusion.Level);
        Assert.Contains("incompleta", conclusion.Text);
    }

    [Theory]
    [InlineData(24, "Últimas 24 horas")]
    [InlineData(168, "Últimos 7 días")]
    [InlineData(720, "Últimos 30 días")]
    public void PeriodLabel_IsReadable(int hours, string expected) =>
        Assert.Equal(expected, EventFormatter.PeriodLabel(TimeSpan.FromHours(hours)));

    [Fact]
    public void Provider_MergesBothLogs_NewestFirst_AndLimitsWhatIsShown()
    {
        var system = Enumerable.Range(0, 40).Select(i => Event("Sys", EventSeverity.Error, 1, i * 2)).ToList();
        var application = Enumerable.Range(0, 40).Select(i => Event("App", EventSeverity.Error, 2, i * 2 + 1, "Application")).ToList();

        var reader = new FakeReader(new()
        {
            ["System"] = new EventReadResult(system, false, null),
            ["Application"] = new EventReadResult(application, false, null)
        });

        var options = new EventQueryOptions(TimeSpan.FromHours(24), true);
        var sections = new RecentEventsProvider(reader, options).CollectAsync().Result;

        var recent = sections.Single(s => s.Title.StartsWith("Eventos más recientes"));
        Assert.Equal(options.MaxShown, recent.Items.Count);
        Assert.Equal(2, reader.Calls.Count);
        Assert.All(reader.Calls, call => Assert.Equal((TimeSpan.FromHours(24), true), (call.Window, call.Warnings)));

        // El más reciente de todos es el primero de System (hace 0 minutos).
        Assert.Contains("Sys", recent.Items[0].Value);
        Assert.Contains("Aplicación", recent.Items[1].Label);
    }

    [Fact]
    public void Provider_ReportsAnUnreadableLog_WithoutHidingTheOther()
    {
        var reader = new FakeReader(new()
        {
            ["System"] = new EventReadResult([], false, "Windows no permite a tu usuario leer este registro."),
            ["Application"] = new EventReadResult([Event("App", EventSeverity.Error, 2, 1, "Application")], false, null)
        });

        var sections = new RecentEventsProvider(reader, new EventQueryOptions(TimeSpan.FromHours(24), false))
            .CollectAsync().Result;

        var summary = sections[0];
        var problem = summary.Items.Single(i => i.Label == "Registro Sistema");
        Assert.Equal(InfoItem.NotAvailable, problem.Value);

        var conclusion = summary.Items.Single(i => i.Label == "Conclusión");
        Assert.NotEqual(InfoLevel.Success, conclusion.Level);
        Assert.Contains(sections, s => s.Title.StartsWith("Eventos más recientes"));
    }

    [Fact]
    public void Provider_WithNoEvents_SaysSoClearly()
    {
        var reader = new FakeReader(new()
        {
            ["System"] = new EventReadResult([], false, null),
            ["Application"] = new EventReadResult([], false, null)
        });

        var sections = new RecentEventsProvider(reader, new EventQueryOptions(TimeSpan.FromHours(24), false))
            .CollectAsync().Result;

        Assert.Equal(InfoLevel.Success, sections[0].Items.Single(i => i.Label == "Conclusión").Level);
        Assert.Equal("Ninguno en el período", sections.Last().Items.Single().Value);
        Assert.DoesNotContain(sections, s => s.Title == "Fuentes con más eventos");
    }

    [Fact]
    public void Provider_FlagsTruncatedReads()
    {
        var reader = new FakeReader(new()
        {
            ["System"] = new EventReadResult([Event("Sys", EventSeverity.Error, 1, 1)], true, null),
            ["Application"] = new EventReadResult([], false, null)
        });

        var sections = new RecentEventsProvider(reader, new EventQueryOptions(TimeSpan.FromHours(24), false))
            .CollectAsync().Result;

        var conclusion = sections[0].Items.Single(i => i.Label == "Conclusión");
        Assert.Contains("puede haber más", conclusion.Note);
    }

    [Fact]
    public async Task Provider_StopsWhenCancelled()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var reader = new FakeReader(new());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new RecentEventsProvider(reader, new EventQueryOptions(TimeSpan.FromHours(24), false))
                .CollectAsync(cts.Token));
    }

    [Fact]
    public void RealReader_ReadsTheSystemLogWithinTheRequestedWindow()
    {
        var window = TimeSpan.FromDays(7);

        var result = new WindowsEventLogReader().Read("System", window, true, 20, 3, CancellationToken.None);

        // Si Windows no deja leer el registro, debe informarlo sin lanzar excepciones.
        Assert.True(result.Error is null || result.Events.Count == 0);
        Assert.True(result.Events.Count <= 20);
        Assert.All(result.Events, e => Assert.True(e.Time >= DateTimeOffset.Now - window - TimeSpan.FromMinutes(5)));
        Assert.Equal(result.Events.OrderByDescending(e => e.Time).Select(e => e.Time), result.Events.Select(e => e.Time));
    }

    [Fact]
    public void RealReader_AMissingLog_ReturnsNoEvents_InsteadOfCrashing()
    {
        var result = new WindowsEventLogReader().Read("Newtin-Registro-Inexistente",
            TimeSpan.FromHours(1), false, 10, 0, CancellationToken.None);

        Assert.Empty(result.Events);
    }

        [Fact]
    public void BuildXPath_UsesAnAbsoluteUtcTimestamp_SoLongWindowsAreValid()
    {
        var since = new DateTimeOffset(2026, 9, 26, 12, 0, 0, TimeSpan.Zero);

        var xpath = WindowsEventLogReader.BuildXPath(since, includeWarnings: true);

        Assert.Equal(
            "*[System[(Level=1 or Level=2 or Level=3) and TimeCreated[@SystemTime >= '2026-09-26T12:00:00.000Z']]]",
            xpath);
        Assert.DoesNotContain("timeDiff", xpath);
    }

    [Fact]
    public void BuildXPath_ConvertsToUtc_AndIgnoresWarningsWhenNotRequested()
    {
        var since = new DateTimeOffset(2026, 9, 26, 17, 30, 0, TimeSpan.FromHours(5));

        var xpath = WindowsEventLogReader.BuildXPath(since, includeWarnings: false);

        Assert.Contains("'2026-09-26T12:30:00.000Z'", xpath);
        Assert.Contains("(Level=1 or Level=2)", xpath);
        Assert.DoesNotContain("Level=3", xpath);
    }

    [Fact]
    public void RealReader_AcceptsTheThirtyDayWindow()
    {
        // Regresión: con timeDiff, 30 días (2.592.000.000 ms) superaban el máximo y Windows rechazaba la consulta.
        var result = new WindowsEventLogReader().Read("System", TimeSpan.FromDays(30), true, 20, 0, CancellationToken.None);

        Assert.Null(result.Error);
    }

    [Fact]
    public void Conclude_WhenNothingCouldBeRead_NeverClaimsThereAreNoErrors()
    {
        var conclusion = EventFormatter.Conclude(new EventStats(0, 0, 0, []), incomplete: true);

        Assert.DoesNotContain("No se encontraron errores", conclusion.Text);
        Assert.Equal(InfoLevel.Warning, conclusion.Level);
    }
}