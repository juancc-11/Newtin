using Newtin.Server.Models;
using Newtin.Server.Services;
using Newtin.Server.Validation;

namespace Newtin.Server.Tests;

public class StateServiceTests
{
    private static ElementInput Note(string id = "n1", string? text = "hola", string? categoryId = null) =>
        new(id, ElementType.Note, 10, 20, text, null, "#F2B544", null, categoryId);

    private static ElementInput Stroke(string id = "s1") =>
        new(id, ElementType.Stroke, 0, 0, null, [new PointD(1, 1), new PointD(2, 2)], "#FFFFFF", null, null);

    [Fact]
    public void CreateNote_StoresServerSideMetadata()
    {
        var state = new WhiteboardStateService();

        var result = state.CreateElement(Note(), "Ana");

        Assert.True(result.Ok);
        Assert.Equal("Ana", result.Value!.Author);
        Assert.Equal(1, result.Value.Version);
        Assert.True(result.Value.Order > 0);
        Assert.Equal(result.Value.CreatedAt, result.Value.ModifiedAt);
    }

    [Fact]
    public void DuplicateId_IsRejected()
    {
        var state = new WhiteboardStateService();
        state.CreateElement(Note(), "Ana");

        Assert.False(state.CreateElement(Note(), "Beto").Ok);
        Assert.Equal(1, state.ElementCount);
    }

    [Fact]
    public void InvalidInput_IsRejected()
    {
        var state = new WhiteboardStateService();

        Assert.False(state.CreateElement(null, "Ana").Ok);
        Assert.False(state.CreateElement(Note() with { Type = null }, "Ana").Ok);
        Assert.False(state.CreateElement(Note() with { Color = "red" }, "Ana").Ok);
        Assert.False(state.CreateElement(Note() with { Id = "a b" }, "Ana").Ok);
        Assert.False(state.CreateElement(Note() with { X = 1e9 }, "Ana").Ok);
        Assert.False(state.CreateElement(Note(text: new string('x', WhiteboardLimits.MaxTextLength + 1)), "Ana").Ok);
        Assert.False(state.CreateElement(Stroke() with { Points = null }, "Ana").Ok);
        Assert.False(state.CreateElement(Note(categoryId: "no-existe"), "Ana").Ok);
        Assert.Equal(0, state.ElementCount);
    }

    [Fact]
    public void Stroke_GetsADefaultWidth()
    {
        var result = new WhiteboardStateService().CreateElement(Stroke(), "Ana");

        Assert.True(result.Ok);
        Assert.Equal(WhiteboardLimits.DefaultStrokeWidth, result.Value!.Width);
    }

    [Fact]
    public void Update_ChangesOnlyAllowedFields_AndBumpsTheVersion()
    {
        var state = new WhiteboardStateService();
        state.CreateElement(Note(), "Ana");

        var result = state.UpdateElement(Note(text: "nuevo") with { X = 999, Color = "#112233" });

        Assert.True(result.Ok);
        Assert.Equal("nuevo", result.Value!.Text);
        Assert.Equal("#112233", result.Value.Color);
        Assert.Equal(10, result.Value.X);
        Assert.Equal(2, result.Value.Version);
    }

    [Fact]
    public void Update_OfAnUnknownElement_Fails() =>
        Assert.False(new WhiteboardStateService().UpdateElement(Note()).Ok);

    [Fact]
    public void Move_ChangesThePosition_AndRejectsInvalidOnes()
    {
        var state = new WhiteboardStateService();
        state.CreateElement(Note(), "Ana");

        var moved = state.MoveElement("n1", 50, 60);
        Assert.True(moved.Ok);
        Assert.Equal((50d, 60d), (moved.Value!.X, moved.Value.Y));
        Assert.Equal(2, moved.Value.Version);

        Assert.False(state.MoveElement("n1", 1e9, 0).Ok);
        Assert.False(state.MoveElement("no-existe", 1, 1).Ok);
    }

    [Fact]
    public void Delete_RemovesTheElement()
    {
        var state = new WhiteboardStateService();
        state.CreateElement(Note(), "Ana");

        Assert.True(state.DeleteElement("n1").Ok);
        Assert.False(state.DeleteElement("n1").Ok);
        Assert.Equal(0, state.ElementCount);
    }

    [Fact]
    public void Clear_RemovesEverything_AndReportsTheCount()
    {
        var state = new WhiteboardStateService();
        state.CreateElement(Note("a"), "Ana");
        state.CreateElement(Note("b"), "Ana");

        Assert.Equal(2, state.Clear());
        Assert.Empty(state.Snapshot().Elements);
    }

    [Fact]
    public void Categories_CannotRepeatNames_EvenWithDifferentCase()
    {
        var state = new WhiteboardStateService();

        Assert.True(state.CreateCategory(new CategoryInput("c1", "Ideas", "#112233")).Ok);
        Assert.False(state.CreateCategory(new CategoryInput("c2", "ideas", null)).Ok);
        Assert.False(state.CreateCategory(new CategoryInput("c1", "Otra", null)).Ok);
        Assert.False(state.CreateCategory(new CategoryInput("c3", "", null)).Ok);
        Assert.False(state.CreateCategory(new CategoryInput("c4", "Mala", "azul")).Ok);
    }

    [Fact]
    public void UpdateCategory_ChangesColorAndName()
    {
        var state = new WhiteboardStateService();
        state.CreateCategory(new CategoryInput("c1", "Ideas", "#112233"));

        var result = state.UpdateCategory(new CategoryInput("c1", "Tareas", "#445566"));

        Assert.True(result.Ok);
        Assert.Equal("Tareas", result.Value!.Name);
        Assert.Equal("#445566", result.Value.Color);
    }

    [Fact]
    public void DeletingACategory_ClearsItFromElements_AndBumpsTheirVersion()
    {
        var state = new WhiteboardStateService();
        state.CreateCategory(new CategoryInput("c1", "Ideas", null));
        state.CreateElement(Note("n1", categoryId: "c1"), "Ana");
        state.CreateElement(Note("n2"), "Ana");

        var deletion = state.DeleteCategory("c1");

        Assert.True(deletion.Ok);
        var updated = Assert.Single(deletion.Value!.UpdatedElements);
        Assert.Equal("n1", updated.Id);
        Assert.Null(updated.CategoryId);
        Assert.Equal(2, updated.Version);
        Assert.Empty(state.Snapshot().Categories);
    }

    [Fact]
    public void ElementLimit_IsEnforced()
    {
        var state = new WhiteboardStateService();
        for (var i = 0; i < WhiteboardLimits.MaxElements; i++)
            Assert.True(state.CreateElement(Note($"n{i}"), "Ana").Ok);

        Assert.False(state.CreateElement(Note("uno-mas"), "Ana").Ok);
    }

    [Fact]
    public void Snapshot_KeepsCreationOrder()
    {
        var state = new WhiteboardStateService();
        state.CreateElement(Note("b"), "Ana");
        state.CreateElement(Note("a"), "Ana");

        Assert.Equal(["b", "a"], state.Snapshot().Elements.Select(e => e.Id));
    }

        private static ElementInput FreeText(string id = "t1", string? text = "Hola", double? size = 28) =>
        new(id, ElementType.Text, 10, 20, text, null, "#17131F", size, null);

    [Fact]
    public void FreeText_StoresTheFontSizeInWidth()
    {
        var result = new WhiteboardStateService().CreateElement(FreeText(size: 40), "Ana");

        Assert.True(result.Ok);
        Assert.Equal(ElementType.Text, result.Value!.Type);
        Assert.Equal(40, result.Value.Width);
        Assert.Equal("Hola", result.Value.Text);
        Assert.Null(result.Value.Points);
    }

    [Fact]
    public void FreeText_GetsDefaultsWhenColorAndSizeAreOmitted()
    {
        var input = FreeText(size: null) with { Color = null };

        var element = new WhiteboardStateService().CreateElement(input, "Ana").Value!;

        Assert.Equal(WhiteboardLimits.DefaultTextColor, element.Color);
        Assert.Equal(WhiteboardLimits.DefaultFontSize, element.Width);
    }

    [Theory]
    [InlineData(8, true)]
    [InlineData(120, true)]
    [InlineData(7.9, false)]
    [InlineData(121, false)]
    [InlineData(double.NaN, false)]
    public void FreeText_FontSizeMustBeInRange(double size, bool valid) =>
        Assert.Equal(valid, new WhiteboardStateService().CreateElement(FreeText(size: size), "Ana").Ok);

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\n\n")]
    [InlineData(null)]
    public void FreeText_CannotBeEmpty(string? text) =>
        Assert.False(new WhiteboardStateService().CreateElement(FreeText(text: text), "Ana").Ok);

    [Fact]
    public void FreeText_CannotExceedTheTextLimit() =>
        Assert.False(new WhiteboardStateService()
            .CreateElement(FreeText(text: new string('x', WhiteboardLimits.MaxTextLength + 1)), "Ana").Ok);

    [Fact]
    public void FreeText_UpdateChangesTextAndSize_AndMoveWorks()
    {
        var state = new WhiteboardStateService();
        state.CreateElement(FreeText(), "Ana");

        var updated = state.UpdateElement(FreeText(text: "Cambiado", size: 60));

        Assert.True(updated.Ok);
        Assert.Equal("Cambiado", updated.Value!.Text);
        Assert.Equal(60, updated.Value.Width);
        Assert.Equal(2, updated.Value.Version);

        var moved = state.MoveElement("t1", 300, 400);
        Assert.True(moved.Ok);
        Assert.Equal((300d, 400d), (moved.Value!.X, moved.Value.Y));
    }

    [Fact]
    public void FreeText_UpdateRejectsEmptyText_AndOutOfRangeSize()
    {
        var state = new WhiteboardStateService();
        state.CreateElement(FreeText(), "Ana");

        Assert.False(state.UpdateElement(FreeText(text: "  ")).Ok);
        Assert.False(state.UpdateElement(FreeText(size: 500)).Ok);
        Assert.Equal("Hola", state.Snapshot().Elements.Single().Text);
    }

    [Fact]
    public void Strokes_StillUseTheStrokeWidthRange()
    {
        var state = new WhiteboardStateService();
        state.CreateElement(Stroke(), "Ana");

        // 100 es un tamaño de letra válido, pero no un grosor de trazo válido (máximo 64).
        Assert.False(state.UpdateElement(Stroke() with { Width = 100 }).Ok);
        Assert.True(state.UpdateElement(Stroke() with { Width = 20 }).Ok);
    }
}