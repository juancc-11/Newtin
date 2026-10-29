using System.Globalization;
using System.Text;
using System.Xml.Linq;
using Newtin.Server.Models;
using Newtin.Server.Persistence;
using Newtin.Server.Services;

namespace Newtin.Server.Tests;

public class TextFormatsTests
{
    private static byte[] Bytes(string text) => Encoding.UTF8.GetBytes(text);

    private static WhiteboardSnapshot Apply(PlanResult plan)
    {
        var state = new WhiteboardStateService();
        BoardImporter.Apply(state, plan.Plan!, ImportMode.Replace);
        return state.Snapshot();
    }

    private static WhiteboardStateService BoardWith(params string[] notes)
    {
        var state = new WhiteboardStateService();
        for (var i = 0; i < notes.Length; i++)
            state.CreateElement(new ElementInput($"n{i}", ElementType.Note, 50 + i * 230, 50, notes[i], null, null, null, null), "Ana");
        return state;
    }

    // ---------- Markdown ----------

    [Fact]
    public void Markdown_RoundTrips_NotesCategoriesAndFreeTexts()
    {
        var snapshot = PersistenceTests.RichBoard().Snapshot();

        var restored = Apply(MarkdownFormat.Read(Bytes(MarkdownFormat.Write(snapshot, DateTimeOffset.Now))));

        Assert.Equal(["Ideas", "Tareas"], restored.Categories.Select(c => c.Name));
        Assert.Equal(3, restored.Elements.Count);   // 2 notas y 1 texto; el dibujo no se exporta
        var note = restored.Elements.Single(e => e.Text!.StartsWith("Primera"));
        Assert.Equal("Primera nota\nsegunda línea", note.Text);
        Assert.Equal("Ideas", restored.Categories.Single(c => c.Id == note.CategoryId).Name);
        Assert.Equal(ElementType.Text, restored.Elements.Single(e => e.Text == "Título libre").Type);
    }

    [Fact]
    public void Markdown_EscapesAndRestoresMarkup()
    {
        var state = BoardWith("<b>negrita</b> & más");

        var markdown = MarkdownFormat.Write(state.Snapshot(), DateTimeOffset.Now);
        Assert.DoesNotContain("<b>", markdown);

        Assert.Equal("<b>negrita</b> & más", Apply(MarkdownFormat.Read(Bytes(markdown))).Elements.Single().Text);
    }

    [Fact]
    public void Markdown_ReadsPlainTextLinesAsNotes()
    {
        var snapshot = Apply(MarkdownFormat.Read(Bytes("uno\r\ndos\r\n- tres\r\n* cuatro")));

        Assert.Equal(["uno", "dos", "tres", "cuatro"], snapshot.Elements.Select(e => e.Text));
    }

    [Fact]
    public void Markdown_UsesHeadingsAsCategories_AndIgnoresTheTitle()
    {
        const string text = "# Mi pizarra\n_Exportada el 01/01/2026 10:00_\n\n## Ideas\n- a\n- b\n\n## Sin categoría\n- c\n";

        var snapshot = Apply(MarkdownFormat.Read(Bytes(text)));

        Assert.Equal(["Ideas"], snapshot.Categories.Select(c => c.Name));
        Assert.Equal(3, snapshot.Elements.Count);
        Assert.Equal("Ideas", snapshot.Categories.Single(c => c.Id == snapshot.Elements.First(e => e.Text == "a").CategoryId).Name);
        Assert.Null(snapshot.Elements.First(e => e.Text == "c").CategoryId);
    }

    [Fact]
    public void Markdown_JoinsIndentedLinesIntoTheSameNote()
    {
        var snapshot = Apply(MarkdownFormat.Read(Bytes("- primera línea\n  segunda línea\n- otra nota")));

        Assert.Equal(["primera línea\nsegunda línea", "otra nota"], snapshot.Elements.Select(e => e.Text));
    }

    [Fact]
    public void Markdown_ReadsLatin1Files()
    {
        var latin1 = Encoding.Latin1.GetBytes("- canción y pingüino");

        Assert.Equal("canción y pingüino", Apply(MarkdownFormat.Read(latin1)).Elements.Single().Text);
    }

    [Fact]
    public void Markdown_TruncatesVeryLongNotes_AndWarns()
    {
        var result = MarkdownFormat.Read(Bytes("- " + new string('x', 2500)));

        Assert.Equal(2000, result.Plan!.Elements.Single().Input.Text!.Length);
        Assert.NotEmpty(result.Plan.Warnings);
    }

    [Fact]
    public void Markdown_WithoutNotes_IsRejected() =>
        Assert.Null(MarkdownFormat.Read(Bytes("# Solo un título\n\n")).Plan);
    
    [Fact]
    public void Markdown_KeepsEmptyCategories()
    {
        var snapshot = Apply(MarkdownFormat.Read(Bytes("## Ideas\n- a\n\n## Tareas\n\n## Pendientes\n")));

        Assert.Equal(["Ideas", "Pendientes", "Tareas"], snapshot.Categories.Select(c => c.Name));
        Assert.Single(snapshot.Elements);
    }

    // ---------- CSV ----------

    [Fact]
    public void Csv_RoundTrips_QuotesCommasAndLineBreaks()
    {
        var state = BoardWith("Dijo \"hola\", y se fue", "línea 1\nlínea 2");
        state.CreateCategory(new CategoryInput("c1", "Ideas; y más", "#112233"));
        state.UpdateElement(new ElementInput("n0", null, 0, 0, null, null, null, null, "c1"));

        var restored = Apply(CsvFormat.Read(CsvFormat.Write(state.Snapshot())));

        Assert.Equal(["Dijo \"hola\", y se fue", "línea 1\nlínea 2"], restored.Elements.Select(e => e.Text));
        Assert.Equal("Ideas; y más", restored.Categories.Single().Name);
        Assert.Equal("#112233", restored.Categories.Single().Color);
        Assert.Equal((50d, 50d), (restored.Elements[0].X, restored.Elements[0].Y));
    }

    [Fact]
    public void Csv_ProtectsAgainstSpreadsheetFormulas_AndRestoresTheText()
    {
        var state = BoardWith("=HYPERLINK(\"http://x\")", "+1 más", "@usuario");

        var bytes = CsvFormat.Write(state.Snapshot());
        var text = Encoding.UTF8.GetString(bytes);

        Assert.Contains("'=HYPERLINK", text);
        Assert.Contains("'+1 más", text);
        Assert.Contains("'@usuario", text);
        Assert.Equal(["=HYPERLINK(\"http://x\")", "+1 más", "@usuario"],
            Apply(CsvFormat.Read(bytes)).Elements.Select(e => e.Text));
    }

    [Fact]
    public void Csv_StartsWithABom_SoExcelReadsAccents() =>
        Assert.Equal([0xEF, 0xBB, 0xBF], CsvFormat.Write(BoardWith("á").Snapshot()).Take(3));

    [Fact]
    public void Csv_ReadsSemicolonSeparatedFiles_WithAHeaderInAnyCaseOrAccent()
    {
        var csv = "\uFEFFTexto;Categoría\r\nHola;Ideas\r\nAdiós;";

        var snapshot = Apply(CsvFormat.Read(Bytes(csv)));

        Assert.Equal(["Hola", "Adiós"], snapshot.Elements.Select(e => e.Text));
        Assert.Equal("Ideas", snapshot.Categories.Single().Name);
    }

    [Fact]
    public void Csv_WithoutATextColumn_IsRejected()
    {
        var result = CsvFormat.Read(Bytes("nombre,edad\nAna,30"));

        Assert.Null(result.Plan);
        Assert.Contains("texto", result.Error);
    }

    [Fact]
    public void Csv_UsesGivenPositions_AndPlacesTheRestInAGrid()
    {
        var plan = CsvFormat.Read(Bytes("texto,x,y\nfija,500,600\nlibre,,")).Plan!;

        Assert.False(plan.Elements[0].AutoPlace);
        Assert.Equal((500d, 600d), (plan.Elements[0].Input.X, plan.Elements[0].Input.Y));
        Assert.True(plan.Elements[1].AutoPlace);
    }

    [Fact]
    public void Csv_DoesNotExportDrawings()
    {
        var csv = Encoding.UTF8.GetString(CsvFormat.Write(PersistenceTests.RichBoard().Snapshot()));

        Assert.Equal(1 + 3, csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries).Length);   // cabecera + 2 notas + 1 texto
    }

    // ---------- SVG ----------

    [Fact]
    public void Svg_IsWellFormed_EvenWithHostileText()
    {
        var state = BoardWith("</svg><script>alert(1)</script> & \"comillas\" 'simples'");
        state.CreateElement(new ElementInput("t1", ElementType.Text, 10, 300, "<tag>&", null, null, 24, null), "Ana");

        var document = XDocument.Parse(SvgFormat.Write(state.Snapshot()));

        Assert.DoesNotContain(document.Descendants(), e => e.Name.LocalName == "script");
        Assert.Contains("alert(1)", document.ToString());   // el texto sigue ahí, pero escapado
    }

    [Fact]
    public void Svg_DrawsEveryKindOfElement()
    {
        var svg = SvgFormat.Write(PersistenceTests.RichBoard().Snapshot());

        Assert.Contains("<polyline", svg);
        Assert.Contains("Título libre", svg);
        Assert.Contains("Primera nota", svg);
        Assert.Contains("Ideas", svg);
        Assert.Contains("viewBox=", svg);
    }

    [Fact]
    public void Svg_UsesInvariantNumbers_InAnyCulture()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("es-ES");
            var state = new WhiteboardStateService();
            state.CreateElement(new ElementInput("s1", ElementType.Stroke, 0, 0, null,
                [new PointD(10.5, 20.25), new PointD(30.5, 40.75)], "#E60033", 2.5, null), "Ana");

            var svg = SvgFormat.Write(state.Snapshot());

            Assert.Contains("10.5,20.25 30.5,40.75", svg);
            Assert.Contains("stroke-width=\"2.5\"", svg);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void Svg_OfAnEmptyBoard_IsStillValid() =>
        Assert.NotNull(XDocument.Parse(SvgFormat.Write(new WhiteboardSnapshot([], []))).Root);

    
}