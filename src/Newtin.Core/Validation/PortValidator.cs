namespace Newtin.Core.Validation;

public static class PortValidator
{
    public static bool IsValid(int port) => port is >= 1 and <= 65535;

    public static bool TryParse(string? text, out int port) =>
        int.TryParse(text?.Trim(), out port) && IsValid(port);
}