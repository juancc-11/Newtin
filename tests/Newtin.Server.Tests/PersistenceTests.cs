using System.Text;
using Newtin.Server.Models;
using Newtin.Server.Persistence;
using Newtin.Server.Services;
using Newtin.Server.Validation;

namespace Newtin.Server.Tests;

public class PersistenceTests
{
    private static byte[] Bytes(string text) => Encoding.UTF8.GetBytes(text);

    internal static WhiteboardStateService RichBoard()
    {
        var state = new WhiteboardStateService();
        state.CreateCategory(new CategoryInput("c1", "Ideas", "#112233"));
        state.CreateCategory(new CategoryInput("c2", "Tareas", "#445566"));
        state.CreateElement(new ElementInput("n1", ElementType.Note, 100, 120, "Primera nota\nsegunda línea", null, "#FFF1A8", null, "c1"), "Ana");
        state.CreateElement(new ElementInput("n2", ElementType.Note, 400, 120, "Sin categoría", null, "#FFC9D4", null, null), "Beto");
        state.CreateElement(new ElementInput("t1", ElementType.Text, 60, 400, "Título libre", null, "#17131F", 36, null), "Ana");
        state.CreateElement(new ElementInput("s1", ElementType.Stroke, 0, 0, null, [new PointD(10, 10), new PointD(50, 60)], "#E60033", 5, null), "Ana");
        return state;
    }

    private static string? CategoryOf(WhiteboardSnapshot snapshot, string text)
    {
        var element = snapshot.Elements.First(e => e.Text == text);
        return snapshot.Categories.FirstOrDefault(c => c.Id == element.CategoryId)?.Name;
    }

    [Fact]
    public void Native_RoundTrips_EveryKindOfElement()
    {
        var original = RichBoard().Snapshot();
        var plan = NativeFormat.Read(NativeFormat.Write(original, DateTimeOffset.UtcNow)).Plan!;

        var restored = new WhiteboardStateService();
        var outcome = BoardImporter.Apply(restored, plan, ImportMode.Replace);
        var copy = restored.Snapshot();

        Assert.Equal((4, 2, 0), (outcome.Elements, outcome.Categories, outcome.Skipped));
        Assert.Equal(original.Elements.Select(e => e.Type), copy.Elements.Select(e => e.Type));
        Assert.Equal(original.Elements.Select(e => e.Text), copy.Elements.Select(e => e.Text));
        Assert.Equal(original.Elements.Select(e => (e.X, e.Y)), copy.Elements.Select(e => (e.X, e.Y)));
        Assert.Equal(original.Elements.Select(e => e.Color), copy.Elements.Select(e => e.Color));
        Assert.Equal(original.Elements.Select(e => e.Width), copy.Elements.Select(e => e.Width));
        Assert.Equal(original.Elements.Single(e => e.Type == ElementType.Stroke).Points,
            copy.Elements.Single(e => e.Type == ElementType.Stroke).Points);
        Assert.Equal(original.Categories.Select(c => (c.Name, c.Color)), copy.Categories.Select(c => (c.Name, c.Color)));
        Assert.Equal("Ideas", CategoryOf(copy, "Primera nota\nsegunda línea"));
        Assert.Null(CategoryOf(copy, "Sin categoría"));
    }

    [Fact]
    public void Import_AssignsFreshValidIds()
    {
        var original = RichBoard().Snapshot();
        var plan = NativeFormat.Read(NativeFormat.Write(original, DateTimeOffset.UtcNow)).Plan!;

        var state = new WhiteboardStateService();
        BoardImporter.Apply(state, plan, ImportMode.Replace);

        var ids = state.Snapshot().Elements.Select(e => e.Id).ToList();
        Assert.Empty(ids.Intersect(original.Elements.Select(e => e.Id)));
        Assert.All(ids, id => Assert.Null(WhiteboardValidator.ValidateId(id)));
    }

    [Theory]
    [InlineData("esto no es json")]
    [InlineData("{}")]
    [InlineData("{\"format\":\"otra-cosa\",\"version\":1}")]
    [InlineData("{\"format\":\"newtin-whiteboard\",\"version\":99}")]
    [InlineData("{\"format\":\"newtin-whiteboard\",\"version\":0}")]
    public void Native_RejectsFilesThatAreNotValidBoards(string content)
    {
        var result = NativeFormat.Read(Bytes(content));

        Assert.Null(result.Plan);
        Assert.NotEmpty(result.Error!);
    }

    [Fact]
    public void Native_AcceptsAUtf8Bom()
    {
        var bytes = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(NativeFormat.Write(RichBoard().Snapshot(), DateTimeOffset.UtcNow)).ToArray();

        Assert.NotNull(NativeFormat.Read(bytes).Plan);
    }

    [Fact]
    public void Native_SkipsInvalidEntries_AndReportsThem()
    {
        const string json = """
        {"format":"newtin-whiteboard","version":1,
         "categories":[{"id":"c1","name":"Ideas","color":"#112233"},{"id":"c1","name":"Repetida"},{"id":"c2","name":""}],
         "elements":[
           {"id":"a","type":"note","x":10,"y":20,"text":"Hola","color":"rojo","categoryId":"c1"},
           {"id":"b","type":"dibujo","x":0,"y":0},
           {"id":"c","type":"note","x":1000000000,"y":0,"text":"fuera de rango"},
           {"id":"d","type":"note","x":5,"y":5,"text":"x","categoryId":"no-existe"}
         ]}
        """;

        var plan = NativeFormat.Read(Bytes(json)).Plan!;
        var state = new WhiteboardStateService();
        var outcome = BoardImporter.Apply(state, plan, ImportMode.Replace);

        Assert.Equal(2, outcome.Elements);
        Assert.Equal(1, outcome.Skipped);
        Assert.NotEmpty(outcome.Warnings);

        var snapshot = state.Snapshot();
        Assert.Single(snapshot.Categories);
        Assert.Equal(WhiteboardLimits.DefaultNoteColor, snapshot.Elements.First(e => e.Text == "Hola").Color);
        Assert.Equal("Ideas", CategoryOf(snapshot, "Hola"));
        Assert.Null(snapshot.Elements.First(e => e.Text == "x").CategoryId);
    }

    [Fact]
    public void Apply_Replace_RemovesThePreviousContent()
    {
        var state = RichBoard();
        var plan = MarkdownFormat.Read(Bytes("- solo esta")).Plan!;

        BoardImporter.Apply(state, plan, ImportMode.Replace);

        var snapshot = state.Snapshot();
        Assert.Equal("solo esta", Assert.Single(snapshot.Elements).Text);
        Assert.Empty(snapshot.Categories);
    }

    [Fact]
    public void Apply_Merge_KeepsWhatIsThere_ReusesCategoriesByName_AndPlacesNewNotesBelow()
    {
        var state = RichBoard();
        var existingBottom = BoardLayoutCalculator.ContentBottom(state.Snapshot());
        var plan = MarkdownFormat.Read(Bytes("## ideas\n- nueva")).Plan!;

        var outcome = BoardImporter.Apply(state, plan, ImportMode.Merge);

        var snapshot = state.Snapshot();
        Assert.Equal((1, 0), (outcome.Elements, outcome.Categories));
        Assert.Equal(2, snapshot.Categories.Count);
        Assert.Equal(5, snapshot.Elements.Count);
        Assert.Equal("Ideas", CategoryOf(snapshot, "nueva"));
        Assert.True(snapshot.Elements.First(e => e.Text == "nueva").Y > existingBottom);
    }

    [Fact]
    public void Apply_RespectsTheElementLimit_WithAHostileFile()
    {
        var elements = Enumerable.Range(0, WhiteboardLimits.MaxElements + 100)
            .Select(_ => new PlanElement(new ElementInput(null, ElementType.Note, 10, 10, "n", null, null, null, null), null, false))
            .ToList();

        var state = new WhiteboardStateService();
        var outcome = BoardImporter.Apply(state, new ImportPlan([], elements, []), ImportMode.Replace);

        Assert.Equal(WhiteboardLimits.MaxElements, outcome.Elements);
        Assert.Equal(100, outcome.Skipped);
    }

    [Fact]
    public void Apply_ReportsNotesThatDoNotFitOnTheBoard()
    {
        var text = string.Join("\n", Enumerable.Range(1, 200).Select(i => $"- nota {i}"));

        var state = new WhiteboardStateService();
        var outcome = BoardImporter.Apply(state, MarkdownFormat.Read(Bytes(text)).Plan!, ImportMode.Replace);

        Assert.InRange(outcome.Elements, 50, 100);
        Assert.Equal(200 - outcome.Elements, outcome.Skipped);
        Assert.Contains(outcome.Warnings, w => w.Contains("no cupieron"));
    }

    [Fact]
    public void Apply_NotifiesOnce_WhenTheBoardChanges()
    {
        var state = new WhiteboardStateService();
        var changes = 0;
        state.Changed += () => changes++;

        BoardImporter.Apply(state, MarkdownFormat.Read(Bytes("- a\n- b\n- c")).Plan!, ImportMode.Replace);

        Assert.Equal(1, changes);
    }

    [Fact]
    public void ClearAll_RemovesElementsAndCategories()
    {
        var state = RichBoard();

        state.ClearAll();

        Assert.Empty(state.Snapshot().Elements);
        Assert.Empty(state.Snapshot().Categories);
    }

    [Fact]
    public void Layout_CropsToTheContent_WithAMargin()
    {
        var state = new WhiteboardStateService();
        state.CreateElement(new ElementInput("n1", ElementType.Note, 1000, 1000, "x", null, null, null, null), "Ana");

        var layout = BoardLayoutCalculator.Compute(state.Snapshot());

        Assert.Equal(1000 - BoardLayoutCalculator.Margin, layout.X);
        Assert.True(layout.Width < BoardLayoutCalculator.BoardWidth);
        Assert.Equal(new BoardLayout(0, 0, 800, 500), BoardLayoutCalculator.Compute(new WhiteboardSnapshot([], [])));
    }
}