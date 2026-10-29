using Newtin.Core.Models;
using Newtin.Core.Validation;

namespace Newtin.Core.Tests;

public class ValidationTests
{
    [Theory]
    [InlineData("192.168.100.10")]
    [InlineData("bosgame")]
    [InlineData("BOSGAME-PC")]
    [InlineData("pc1.local")]
    public void Host_AcceptsValidTargets(string input) =>
        Assert.True(HostValidator.TryNormalize(input, out _, out _));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("999.1.1.1")]
    [InlineData("1")]
    [InlineData("192.168.1")]
    [InlineData("a b")]
    [InlineData("192.168.100.10:445")]
    [InlineData("\\\\bosgame")]
    [InlineData("host/share")]
    [InlineData("0.0.0.0")]
    [InlineData("255.255.255.255")]
    public void Host_RejectsInvalidTargets(string input)
    {
        Assert.False(HostValidator.TryNormalize(input, out _, out var error));
        Assert.NotEmpty(error);
    }

    [Fact]
    public void Host_TrimsWhitespace()
    {
        Assert.True(HostValidator.TryNormalize("  192.168.100.10  ", out var host, out _));
        Assert.Equal("192.168.100.10", host);
    }

    [Theory]
    [InlineData("1", true)]
    [InlineData("445", true)]
    [InlineData("65535", true)]
    [InlineData("0", false)]
    [InlineData("65536", false)]
    [InlineData("-1", false)]
    [InlineData("abc", false)]
    [InlineData("", false)]
    public void Port_ValidatesRange(string text, bool expected) =>
        Assert.Equal(expected, PortValidator.TryParse(text, out _));

    [Theory]
    [InlineData("Velocidad", true)]
    [InlineData("Documentos 2026", true)]
    [InlineData("", false)]
    [InlineData("a\\b", false)]
    [InlineData("a:b", false)]
    [InlineData("a*b", false)]
    public void ShareName_RejectsPathsAndInvalidCharacters(string name, bool expected) =>
        Assert.Equal(expected, ShareNameValidator.TryValidate(name, out _));

    [Fact]
    public void Settings_DefaultsAreValid() =>
        Assert.Empty(SettingsValidator.Validate(new AppSettings()));

    [Fact]
    public void Settings_ReportsEveryProblem()
    {
        var settings = new AppSettings();
        settings.Network.RemoteHost = "999.1.1.1";
        settings.Network.ConnectionTimeoutSeconds = 0;
        settings.WhiteboardServer.Port = 80;

        Assert.Equal(3, SettingsValidator.Validate(settings).Count);
    }
}