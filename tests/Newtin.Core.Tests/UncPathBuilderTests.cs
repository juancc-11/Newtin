using Newtin.Core.Validation;

namespace Newtin.Core.Tests;

public class UncPathBuilderTests
{
    [Fact]
    public void Builds_TheExpectedPath()
    {
        Assert.True(UncPathBuilder.TryBuild(" 192.168.100.10 ", " Velocidad ", out var unc, out _));
        Assert.Equal(@"\\192.168.100.10\Velocidad", unc);
    }

    [Fact]
    public void Builds_TheRemoteHostPath_WhenShareIsOmitted()
    {
        Assert.True(UncPathBuilder.TryBuild(" equipo-remoto ", "", out var unc, out _));
        Assert.Equal(@"\\equipo-remoto", unc);
    }

    [Theory]
    [InlineData("", "Velocidad")]
    [InlineData("999.1.1.1", "Velocidad")]
    [InlineData("192.168.100.10", @"Velocidad\Subcarpeta")]
    public void Rejects_InvalidParts(string host, string share)
    {
        Assert.False(UncPathBuilder.TryBuild(host, share, out var unc, out var error));
        Assert.Equal("", unc);
        Assert.NotEmpty(error);
    }
}