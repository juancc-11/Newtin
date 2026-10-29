using Newtin.Core.Interfaces;
using Newtin.Core.Models;
using Newtin.Infrastructure.Configuration;
using Newtin.Infrastructure.Logging;

namespace Newtin.Infrastructure.Tests;

public sealed class InfrastructureTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "newtin-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private sealed class SilentLog : ILogService
    {
        public void Info(string message) { }
        public void Warning(string message) { }
        public void Error(string message, Exception? exception = null) { }
    }

    private string SettingsPath => Path.Combine(_dir, "settings.json");

    [Fact]
    public void Settings_SurviveSaveAndLoad()
    {
        var first = new JsonSettingsService(SettingsPath, new SilentLog());
        var settings = new AppSettings();
        settings.Network.RemoteHost = "192.168.100.10";
        settings.Network.RemoteShare = "Velocidad";
        settings.Network.ConnectionTimeoutSeconds = 7;
        settings.WhiteboardServer.Port = 5050;

        Assert.True(first.TrySave(settings, out _));

        var second = new JsonSettingsService(SettingsPath, new SilentLog());
        second.Load();

        Assert.Equal("192.168.100.10", second.Current.Network.RemoteHost);
        Assert.Equal("Velocidad", second.Current.Network.RemoteShare);
        Assert.Equal(7, second.Current.Network.ConnectionTimeoutSeconds);
        Assert.Equal(5050, second.Current.WhiteboardServer.Port);
        Assert.Null(second.LoadWarning);
    }

    [Fact]
    public void Settings_NeverContainAPasswordField()
    {
        var service = new JsonSettingsService(SettingsPath, new SilentLog());
        Assert.True(service.TrySave(new AppSettings(), out _));

        Assert.DoesNotContain("password", File.ReadAllText(SettingsPath), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TrySave_RejectsInvalidValues_AndWritesNothing()
    {
        var service = new JsonSettingsService(SettingsPath, new SilentLog());
        var settings = new AppSettings();
        settings.Network.RemoteHost = "999.1.1.1";

        Assert.False(service.TrySave(settings, out var error));
        Assert.NotEmpty(error);
        Assert.False(File.Exists(SettingsPath));
    }

    [Fact]
    public void Load_CorruptFile_FallsBackToDefaultsAndWarns()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(SettingsPath, "{ esto no es json");

        var service = new JsonSettingsService(SettingsPath, new SilentLog());
        service.Load();

        Assert.NotNull(service.LoadWarning);
        Assert.Equal(5, service.Current.Network.ConnectionTimeoutSeconds);
    }

    [Fact]
    public void Log_WritesOneLinePerEntry_WithoutLineBreaksInMessage()
    {
        var log = new FileLogService(_dir);
        log.Info("hola\nmundo");

        var file = Assert.Single(Directory.GetFiles(_dir, "newtin-*.log"));
        var text = File.ReadAllText(file);

        Assert.Contains("[Information] hola mundo", text);
    }
}