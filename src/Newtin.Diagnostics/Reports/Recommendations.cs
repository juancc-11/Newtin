using Newtin.Core.Models;

namespace Newtin.Diagnostics.Reports;

/// <summary>Consejos de revisión. Ninguno modifica el sistema: solo indican qué mirar o a quién pedir permiso.</summary>
public static class Recommendations
{
    public static IReadOnlyList<string> For(ModuleResult result)
    {
        var items = result.Sections.Where(s => s.IncludeInReport).SelectMany(s => s.Items).ToList();
        var advice = new List<string>();

        bool ValueHas(string text) => items.Any(i => i.Value.Contains(text, StringComparison.OrdinalIgnoreCase));
        bool Labeled(string label, Func<string, bool> test) =>
            items.Any(i => i.Label.Equals(label, StringComparison.OrdinalIgnoreCase) && test(i.Value));
        bool Level(InfoLevel level) => items.Any(i => i.Level == level);

        switch (result.Key)
        {
            case "storage":
                if (items.Any(i => i.Label == "Porcentaje utilizado" && i.Percent >= 90))
                    advice.Add("Hay unidades con más del 90 % de uso. Libera espacio (vacía la Papelera, desinstala programas que no uses o mueve archivos a otra unidad) para evitar fallos y lentitud.");
                else if (items.Any(i => i.Label == "Porcentaje utilizado" && i.Percent >= 80))
                    advice.Add("Hay unidades con más del 80 % de uso. Conviene revisar qué ocupa más espacio antes de que se llenen.");
                break;

            case "hardware":
                if (items.Any(i => i.Label == "En uso" && i.Percent >= 90))
                    advice.Add("La memoria RAM está casi al límite. Cierra programas que no uses; si ocurre a menudo, valora ampliar la memoria.");
                break;

            case "battery":
                if (items.Any(i => i.Label == "Carga actual" && i.Percent <= 10))
                    advice.Add("La batería está en un nivel crítico. Conecta el equipo a la corriente y guarda tu trabajo.");
                else if (items.Any(i => i.Label == "Carga actual" && i.Percent <= 20))
                    advice.Add("La batería está baja. Conecta el equipo a la corriente cuando puedas.");
                break;

            case "connectivity":
                if (Level(InfoLevel.Error))
                    advice.Add("No hubo comunicación con el equipo remoto. Revisa el cable o la conexión, que ambos equipos estén en la misma red, que la dirección IP sea correcta y que el firewall del otro equipo no bloquee la conexión.");
                else if (Level(InfoLevel.Warning))
                    advice.Add("El equipo responde, pero un servicio o puerto no. Comprueba que el servicio esté activo y que el firewall del otro equipo permita ese puerto.");
                break;

            case "ports":
                if (ValueHas("rechazada"))
                    advice.Add("Un puerto rechazado significa que el equipo respondió pero ningún servicio escucha ahí, o que el firewall lo rechaza. Comprueba que el servicio esté activo.");
                if (ValueHas("Tiempo de espera") || ValueHas("inalcanzable") || ValueHas("resolver el nombre"))
                    advice.Add("Un puerto sin respuesta puede deberse a un firewall que descarta el tráfico, un equipo apagado o una dirección incorrecta.");
                break;

            case "shares":
                if (ValueHas("Credenciales incorrectas"))
                    advice.Add("Comprueba el usuario y la contraseña. Para una cuenta local del otro equipo usa el formato NOMBRE_DEL_EQUIPO\\usuario.");
                if (ValueHas("Equipo inaccesible"))
                    advice.Add("Verifica la conectividad y los puertos, y que el uso compartido de archivos esté habilitado en el otro equipo.");
                if (ValueHas("La carpeta compartida no existe"))
                    advice.Add("Revisa el nombre exacto de la carpeta compartida (en el otro equipo se ve con el comando net share).");
                if (ValueHas("Acceso denegado") || ValueHas("Permisos insuficientes"))
                    advice.Add("La cuenta no tiene permiso sobre la carpeta. Pide al administrador del otro equipo que lo conceda en el recurso compartido y en la pestaña de seguridad.");
                if (ValueHas("Conflicto con una sesión existente"))
                    advice.Add("Windows ya tiene una conexión con ese equipo con otro usuario. Revísala con el comando net use y ciérrala tú si decides hacerlo.");
                if (ValueHas("Problema con la cuenta"))
                    advice.Add("La cuenta tiene un problema (bloqueada, deshabilitada, contraseña caducada o sin permiso de inicio de sesión por red). Debe corregirlo el administrador del otro equipo.");
                break;

            case "permissions":
                var writeFailed = Labeled("Escritura", v => v == "Falló");
                var readFailed = Labeled("Lectura", v => v == "Falló");
                if (writeFailed && !readFailed)
                    advice.Add("La cuenta puede leer pero no escribir. Si necesitas escribir, pide permiso de modificación sobre la carpeta.");
                if (readFailed)
                    advice.Add("No se pudo leer la carpeta. Revisa la ruta, la autenticación y los permisos.");
                if (items.Any(i => i.Note?.Contains("manualmente", StringComparison.OrdinalIgnoreCase) == true))
                    advice.Add("Quedaron archivos temporales de la prueba. Elimínalos manualmente (la ruta aparece en el detalle).");
                break;

            case "events":
                if (Labeled("Críticos", v => v != "0"))
                    advice.Add("Hay eventos críticos. Abre el Visor de eventos (eventvwr.msc) y revisa las fuentes con más eventos.");
                if (Labeled("Errores", v => v != "0"))
                    advice.Add("Revisa en el Visor de eventos si los errores se repiten. Un error aislado no implica un problema actual.");
                break;
        }

        if (advice.Count == 0 && Level(InfoLevel.Error))
            advice.Add("Revisa el detalle de este módulo y repite la prueba cuando hayas corregido el problema.");

        return advice.Distinct().ToList();
    }
}