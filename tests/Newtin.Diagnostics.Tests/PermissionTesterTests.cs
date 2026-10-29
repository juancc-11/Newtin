using Newtin.Core.Interfaces;
using Newtin.Core.Models;
using Newtin.Diagnostics.Permissions;

namespace Newtin.Diagnostics.Tests;

public sealed class PermissionTesterTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "newtin-perm-" + Guid.NewGuid().ToString("N"));

    public PermissionTesterTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private sealed class SilentLog : ILogService
    {
        public void Info(string message) { }
        public void Warning(string message) { }
        public void Error(string message, Exception? exception = null) { }
    }

    private static PermissionTestReport Report(PermissionOutcome read, PermissionOutcome write) =>
        new("x",
        [
            new PermissionStep("a", PermissionArea.Read, read, ""),
            new PermissionStep("b", PermissionArea.Write, write, ""),
            new PermissionStep("c", PermissionArea.Modify, write, ""),
            new PermissionStep("d", PermissionArea.Delete, PermissionOutcome.Passed, "")
        ]);

    [Fact]
    public async Task WritableFolder_PassesAndLeavesNothingBehind()
    {
        var report = await new PermissionTester(new SilentLog()).RunAsync(_root, default);

        Assert.Equal(PermissionOutcome.Passed, report.Read);
        Assert.Equal(PermissionOutcome.Passed, report.Write);
        Assert.Equal(PermissionOutcome.Passed, report.Modify);
        Assert.Equal(PermissionOutcome.Passed, report.Delete);
        Assert.False(report.CleanupFailed);
        Assert.Empty(Directory.GetFileSystemEntries(_root));
    }

    [Fact]
    public async Task MissingFolder_FailsWithoutCreatingAnything()
    {
        var missing = Path.Combine(_root, "no-existe", "mas-adentro");

        var report = await new PermissionTester(new SilentLog()).RunAsync(missing, default);

        Assert.Equal(PermissionOutcome.Failed, report.Read);
        Assert.Equal(PermissionOutcome.NotTested, report.Write);
        Assert.False(Directory.Exists(missing));
    }

    [Fact]
    public async Task CancelledBeforeStarting_CreatesNothing()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => new PermissionTester(new SilentLog()).RunAsync(_root, cts.Token));

        Assert.Empty(Directory.GetFileSystemEntries(_root));
    }

    [Fact]
    public void Summary_ReadableButNotWritable_IsExplainedClearly()
    {
        var interpretation = PermissionFormatter.Interpret(Report(PermissionOutcome.Passed, PermissionOutcome.Failed));

        Assert.Contains("no se pudo escribir", interpretation.Text);
        Assert.Equal(InfoLevel.Warning, interpretation.Level);
    }

    [Fact]
    public void Summary_AllPassed_IsSuccess() =>
        Assert.Equal(InfoLevel.Success,
            PermissionFormatter.Interpret(Report(PermissionOutcome.Passed, PermissionOutcome.Passed)).Level);

    [Fact]
    public void Summary_FailedCleanup_DowngradesSuccessToWarning()
    {
        var report = new PermissionTestReport("x",
        [
            new PermissionStep("a", PermissionArea.Read, PermissionOutcome.Passed, ""),
            new PermissionStep("b", PermissionArea.Write, PermissionOutcome.Passed, ""),
            new PermissionStep("c", PermissionArea.Modify, PermissionOutcome.Passed, ""),
            new PermissionStep("d", PermissionArea.Delete, PermissionOutcome.Passed, ""),
            new PermissionStep("e", PermissionArea.Cleanup, PermissionOutcome.Failed, "")
        ]);

        var interpretation = PermissionFormatter.Interpret(report);

        Assert.Equal(InfoLevel.Warning, interpretation.Level);
        Assert.Contains("manualmente", interpretation.Text);
    }
}