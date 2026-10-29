namespace Newtin.Core.Validation;

public static class UncPathBuilder
{
    public static bool TryBuild(string? host, string? share, out string uncPath, out string error)
    {
        uncPath = "";

        if (!HostValidator.TryNormalize(host, out var normalizedHost, out error)) return false;

        if (string.IsNullOrWhiteSpace(share))
        {
            uncPath = $@"\\{normalizedHost}";
            error = "";
            return true;
        }

        if (!ShareNameValidator.TryValidate(share, out error)) return false;

        uncPath = $@"\\{normalizedHost}\{share.Trim()}";
        return true;
    }
}