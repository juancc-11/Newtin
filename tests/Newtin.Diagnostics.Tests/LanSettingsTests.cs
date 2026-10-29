using Newtin.Core.Models;
using Newtin.Core.Validation;

namespace Newtin.Core.Tests;

public class LanSettingsTests
{
    [Theory]
    [InlineData("192.168.100.1", true)]
    [InlineData(" 10.0.0.5 ", true)]
    [InlineData("", false)]
    [InlineData("bosgame", false)]
    [InlineData("192.168.1", false)]
    [InlineData("999.1.1.1", false)]
    [InlineData("0.0.0.0", false)]
    public void TryParseIPv4_AcceptsOnlyLiteralAddresses(string input, bool expected) =>
        Assert.Equal(expected, HostValidator.TryParseIPv4(input, out _));

    [Fact]
    public void Settings_RejectAnInvalidLanAddress()
    {
        var settings = new AppSettings();
        settings.WhiteboardServer.LanAddress = "no-es-una-ip";

        Assert.Single(SettingsValidator.Validate(settings));
    }

    [Fact]
    public void Settings_AcceptAnEmptyLanAddress() =>
        Assert.Empty(SettingsValidator.Validate(new AppSettings()));
}