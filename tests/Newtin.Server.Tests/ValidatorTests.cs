using Newtin.Server.Models;
using Newtin.Server.Validation;

namespace Newtin.Server.Tests;

public class ValidatorTests
{
    [Theory]
    [InlineData("note-1")]
    [InlineData("A_b-9")]
    public void Id_AcceptsSafeValues(string id) => Assert.Null(WhiteboardValidator.ValidateId(id));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("a b")]
    [InlineData("a/b")]
    [InlineData("<script>")]
    [InlineData("id\n")]
    public void Id_RejectsUnsafeValues(string? id) => Assert.NotNull(WhiteboardValidator.ValidateId(id));

    [Fact]
    public void Id_RejectsMoreThan64Characters() =>
        Assert.NotNull(WhiteboardValidator.ValidateId(new string('a', 65)));

    [Theory]
    [InlineData("#A00082", true)]
    [InlineData("#ffffff", true)]
    [InlineData("red", false)]
    [InlineData("#FFF", false)]
    [InlineData("#GGGGGG", false)]
    [InlineData("#A00082\n", false)]
    [InlineData(null, false)]
    public void Color_MustBeSixDigitHex(string? color, bool valid) =>
        Assert.Equal(valid, WhiteboardValidator.ValidateColor(color) is null);

    [Fact]
    public void Position_RejectsOutOfRangeAndNonFiniteValues()
    {
        Assert.Null(WhiteboardValidator.ValidatePosition(0, 0));
        Assert.Null(WhiteboardValidator.ValidatePosition(-99_999, 99_999));
        Assert.NotNull(WhiteboardValidator.ValidatePosition(1e9, 0));
        Assert.NotNull(WhiteboardValidator.ValidatePosition(double.NaN, 0));
        Assert.NotNull(WhiteboardValidator.ValidatePosition(0, double.PositiveInfinity));
    }

    [Fact]
    public void Points_AreCheckedForCountAndRange()
    {
        Assert.NotNull(WhiteboardValidator.ValidatePoints(null, 10));
        Assert.NotNull(WhiteboardValidator.ValidatePoints([], 10));
        Assert.Null(WhiteboardValidator.ValidatePoints([new PointD(1, 2)], 10));
        Assert.NotNull(WhiteboardValidator.ValidatePoints(Enumerable.Repeat(new PointD(0, 0), 11).ToList(), 10));
        Assert.NotNull(WhiteboardValidator.ValidatePoints([new PointD(1e9, 0)], 10));
    }

    [Fact]
    public void DisplayName_IsNormalized()
    {
        Assert.True(WhiteboardValidator.TryNormalizeDisplayName("  Ana   María ", out var name, out _));
        Assert.Equal("Ana María", name);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("<b>Ana</b>")]
    [InlineData("a;b")]
    public void DisplayName_RejectsInvalidValues(string input) =>
        Assert.False(WhiteboardValidator.TryNormalizeDisplayName(input, out _, out _));

    [Fact]
    public void DisplayName_RejectsMoreThan30Characters() =>
        Assert.False(WhiteboardValidator.TryNormalizeDisplayName(new string('a', 31), out _, out _));

    [Fact]
    public void Text_RemovesControlCharactersAndUnifiesLineBreaks() =>
        Assert.Equal("a\nbc", WhiteboardValidator.NormalizeText("a\r\nb\u0000c"));
}