using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Newtin.Server.Models;

namespace Newtin.Server.Persistence;

public enum ImportMode { Replace, Merge }

public sealed record PlanCategory(string Key, string Name, string Color);

/// <summary>Un elemento por importar. El Id y la categoría real los decide el servidor al aplicarlo.</summary>
public sealed record PlanElement(ElementInput Input, string? CategoryKey, bool AutoPlace);

public sealed record ImportPlan(
    IReadOnlyList<PlanCategory> Categories,
    IReadOnlyList<PlanElement> Elements,
    IReadOnlyList<string> Warnings);

public sealed record ImportOutcome(int Elements, int Categories, int Skipped, IReadOnlyList<string> Warnings);

public sealed record PlanResult(ImportPlan? Plan, string? Error)
{
    public static PlanResult Ok(ImportPlan plan) => new(plan, null);
    public static PlanResult Fail(string error) => new(null, error);
}

public static partial class ImportHelpers
{
    private static readonly string[] CategoryPalette = ["#A00082", "#5420A8", "#1E6FD9", "#2EBD85", "#F2B544", "#E60033"];

    [GeneratedRegex(@"\A#[0-9A-Fa-f]{6}\z")]
    private static partial Regex ColorPattern();

    public static string PaletteColor(int index) => CategoryPalette[index % CategoryPalette.Length];

    public static string? ValidColor(string? value) =>
        value is not null && ColorPattern().IsMatch(value.Trim()) ? value.Trim() : null;

    public static string NewId(string prefix) =>
        prefix + "-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(12)).ToLowerInvariant();
}