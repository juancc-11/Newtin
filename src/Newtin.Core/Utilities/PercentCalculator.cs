namespace Newtin.Core.Utilities;

public static class PercentCalculator
{
    /// <summary>Porcentaje de part sobre total, entre 0 y 100. Devuelve 0 si total no es positivo o part es negativo.</summary>
    public static double Of(long part, long total)
    {
        if (total <= 0 || part < 0) return 0;
        return Math.Min(100, Math.Round(part * 100.0 / total, 1));
    }
}