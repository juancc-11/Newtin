using Newtin.Core.Models;

namespace Newtin.Diagnostics.Permissions;

public static class PermissionFormatter
{
    public static string Describe(PermissionOutcome outcome) => outcome switch
    {
        PermissionOutcome.Passed => "Correcto",
        PermissionOutcome.Failed => "Falló",
        PermissionOutcome.Partial => "Parcial",
        _ => "No probado"
    };

    public static InfoLevel LevelOf(PermissionOutcome outcome) => outcome switch
    {
        PermissionOutcome.Passed => InfoLevel.Success,
        PermissionOutcome.Failed => InfoLevel.Error,
        PermissionOutcome.Partial => InfoLevel.Warning,
        _ => InfoLevel.Normal
    };

    public static Interpretation Interpret(PermissionTestReport report)
    {
        var (text, level) = (report.Read, report.Write, report.Modify, report.Delete) switch
        {
            (PermissionOutcome.Passed, PermissionOutcome.Passed, PermissionOutcome.Passed, PermissionOutcome.Passed) =>
                ("Esta cuenta puede leer, escribir, modificar y eliminar en la carpeta de prueba.", InfoLevel.Success),

            (PermissionOutcome.Failed, _, _, _) =>
                ("No se pudo leer el contenido. Revisa la ruta, las credenciales y los permisos de la carpeta compartida.",
                    InfoLevel.Error),

            (_, PermissionOutcome.Failed, _, _) =>
                ("La lectura funcionó, pero no se pudo escribir o crear el archivo de prueba.", InfoLevel.Warning),
            (_, _, PermissionOutcome.Failed, _) =>
                ("Se pudo escribir, pero no modificar el archivo de prueba.", InfoLevel.Warning),
            (_, _, _, PermissionOutcome.Failed) =>
                ("La prueba detectó que no se pudieron eliminar todos los elementos temporales.", InfoLevel.Warning),

            _ => ("El resultado es parcial: algunas comprobaciones no pudieron realizarse. Revisa el detalle.",
                InfoLevel.Warning)
        };

        if (report.CleanupFailed)
        {
            text += " Atención: no se pudieron eliminar todos los archivos temporales; elimínalos manualmente (ver detalle).";
            if (level == InfoLevel.Success) level = InfoLevel.Warning;
        }

        return new Interpretation(text, level);
    }

    public static IReadOnlyList<InfoSection> ToSections(PermissionTestReport report)
    {
        var conclusion = Interpret(report);

        var summary = new InfoSection("Resultado de la prueba",
        [
            new InfoItem("Carpeta probada", report.RootPath),
            new InfoItem("Lectura", Describe(report.Read), "Listar la carpeta y leer el archivo de prueba.",
                null, LevelOf(report.Read)),
            new InfoItem("Escritura", Describe(report.Write), "Crear la carpeta y el archivo de prueba.",
                null, LevelOf(report.Write)),
            new InfoItem("Modificación", Describe(report.Modify), "Cambiar y verificar el contenido del archivo.",
                null, LevelOf(report.Modify)),
            new InfoItem("Eliminación", Describe(report.Delete), "Eliminar los elementos temporales creados.",
                null, LevelOf(report.Delete)),
            new InfoItem("Conclusión", conclusion.Text, null, null, conclusion.Level)
        ]);

        var details = new InfoSection("Detalle paso a paso",
            report.Steps.Select(s => new InfoItem(s.Name, Describe(s.Outcome), s.Message, null, LevelOf(s.Outcome)))
                .ToList());

        return [summary, details];
    }
}