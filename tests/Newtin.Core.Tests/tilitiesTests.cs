using System.Globalization;
using Newtin.Core.Utilities;

namespace Newtin.Core.Tests;

public class UtilitiesTests
{
    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(1023, "1023 B")]
    [InlineData(1024, "1 KB")]
    [InlineData(1073741824, "1 GB")]
    [InlineData(-5, "No disponible")]
    public void ByteFormatter_ReturnsExpectedText(long bytes, string expected) =>
        Assert.Equal(expected, ByteFormatter.Format(bytes, CultureInfo.InvariantCulture));

    [Theory]
    [InlineData(50, 100, 50)]
    [InlineData(1, 3, 33.3)]
    [InlineData(10, 0, 0)]
    [InlineData(-1, 100, 0)]
    [InlineData(150, 100, 100)]
    public void PercentCalculator_HandlesEdgeCases(long part, long total, double expected) =>
        Assert.Equal(expected, PercentCalculator.Of(part, total));
}