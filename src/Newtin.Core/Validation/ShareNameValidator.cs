namespace Newtin.Core.Validation;

public static class ShareNameValidator
{
    private static readonly char[] InvalidChars = ['\\', '/', ':', '*', '?', '"', '<', '>', '|'];

    public static bool TryValidate(string? name, out string error)
    {
        error = "";
        var value = name?.Trim() ?? "";

        if (value.Length == 0)
        {
            error = "Escribe el nombre de la carpeta compartida.";
            return false;
        }

        if (value.Length > 80)
        {
            error = "El nombre de la carpeta compartida es demasiado largo.";
            return false;
        }

        if (value.IndexOfAny(InvalidChars) >= 0)
        {
            error = "El nombre no puede contener \\ / : * ? \" < > |. Escribe solo el nombre de la carpeta, sin la ruta.";
            return false;
        }

        return true;
    }
}