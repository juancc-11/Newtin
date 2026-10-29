using Newtin.Server.Services;
using Newtin.Server.Validation;

namespace Newtin.Server.Tests;

public class SecurityTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Guard_AcceptsTheCode_IgnoringCaseAndSpaces()
    {
        var guard = new AccessGuard("ABC234");

        Assert.Equal(AccessDecision.Allowed, guard.Check(" abc234 ", "pc", Now).Decision);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("ABC235")]
    [InlineData("ABC2345")]
    public void Guard_DeniesWrongCodes(string? presented) =>
        Assert.Equal(AccessDecision.Denied, new AccessGuard("ABC234").Check(presented, "pc", Now).Decision);

    [Fact]
    public void Guard_BlocksAfterTenFailures_EvenWithTheRightCode()
    {
        var guard = new AccessGuard("ABC234");

        for (var i = 1; i <= 9; i++)
            Assert.Equal(AccessDecision.Denied, guard.Check("MALO", "pc", Now).Decision);

        var tenth = guard.Check("MALO", "pc", Now);
        Assert.Equal(AccessDecision.Blocked, tenth.Decision);
        Assert.True(tenth.JustBlocked);

        var afterwards = guard.Check("ABC234", "pc", Now);
        Assert.Equal(AccessDecision.Blocked, afterwards.Decision);
        Assert.False(afterwards.JustBlocked);
    }

    [Fact]
    public void Guard_UnblocksAfterTheWindow_AndKeepsClientsIndependent()
    {
        var guard = new AccessGuard("ABC234");
        for (var i = 0; i < 10; i++) guard.Check("MALO", "pc1", Now);

        Assert.Equal(AccessDecision.Allowed, guard.Check("ABC234", "pc2", Now).Decision);
        Assert.Equal(AccessDecision.Allowed, guard.Check("ABC234", "pc1", Now.AddMinutes(6)).Decision);
    }

    [Fact]
    public void Guard_ASuccessfulLoginClearsThePreviousFailures()
    {
        var guard = new AccessGuard("ABC234");
        for (var i = 0; i < 9; i++) guard.Check("MALO", "pc", Now);

        Assert.Equal(AccessDecision.Allowed, guard.Check("ABC234", "pc", Now).Decision);
        Assert.Equal(AccessDecision.Denied, guard.Check("MALO", "pc", Now).Decision);
    }

    [Fact]
    public void Generator_ProducesSixCharactersWithoutAmbiguousOnes()
    {
        for (var i = 0; i < 200; i++)
        {
            var code = AccessCodeGenerator.Generate();

            Assert.Equal(AccessCodeGenerator.Length, code.Length);
            Assert.All(code, c => Assert.DoesNotContain(c, "0O1IL"));
            Assert.All(code, c => Assert.True(char.IsAsciiLetterUpper(c) || char.IsAsciiDigit(c)));
        }
    }

    [Fact]
    public void Sessions_TrackNamesAndNotifyChanges()
    {
        var sessions = new ClientSessionService();
        var changes = 0;
        sessions.Changed += () => changes++;

        sessions.Join("c1", "Beto");
        sessions.Join("c2", "Ana");

        Assert.Equal(2, sessions.Count);
        Assert.Equal(["Ana", "Beto"], sessions.Names);

        Assert.True(sessions.Remove("c1"));
        Assert.False(sessions.Remove("c1"));
        Assert.Equal(1, sessions.Count);
        Assert.Equal(3, changes);
    }

    [Fact]
    public void RateLimiter_AllowsTheLimitPerSecond_AndResetsAfterwards()
    {
        var sessions = new ClientSessionService();
        sessions.Join("c1", "Ana");

        for (var i = 0; i < WhiteboardLimits.MaxOperationsPerSecond; i++)
            Assert.True(sessions.TryConsume("c1", 5000));

        Assert.False(sessions.TryConsume("c1", 5500));
        Assert.True(sessions.TryConsume("c1", 6100));
        Assert.False(sessions.TryConsume("desconocido", 6100));
    }
}