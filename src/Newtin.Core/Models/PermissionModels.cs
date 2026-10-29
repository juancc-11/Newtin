namespace Newtin.Core.Models;

public enum PermissionOutcome { Passed, Failed, Partial, NotTested }

public enum PermissionArea { Read, Write, Modify, Delete, Cleanup }

public sealed record PermissionStep(string Name, PermissionArea Area, PermissionOutcome Outcome, string Message);

public sealed record PermissionTestReport(string RootPath, IReadOnlyList<PermissionStep> Steps)
{
    public PermissionOutcome Read => Summarize(PermissionArea.Read);
    public PermissionOutcome Write => Summarize(PermissionArea.Write);
    public PermissionOutcome Modify => Summarize(PermissionArea.Modify);
    public PermissionOutcome Delete => Summarize(PermissionArea.Delete);

    public bool CleanupFailed =>
        Steps.Any(s => (s.Area is PermissionArea.Delete or PermissionArea.Cleanup) &&
                       s.Outcome == PermissionOutcome.Failed);

    private PermissionOutcome Summarize(PermissionArea area)
    {
        var steps = Steps.Where(s => s.Area == area).ToList();

        if (steps.Any(s => s.Outcome == PermissionOutcome.Failed)) return PermissionOutcome.Failed;
        if (steps.Count > 0 && steps.All(s => s.Outcome == PermissionOutcome.Passed)) return PermissionOutcome.Passed;
        return steps.Any(s => s.Outcome == PermissionOutcome.Passed)
            ? PermissionOutcome.Partial
            : PermissionOutcome.NotTested;
    }
}