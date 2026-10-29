namespace Newtin.Desktop.Services;

public sealed record ModuleInfo(string Key, int Number, string Title, string Description, string PhaseLabel);

public static class ModuleCatalog
{
    public static IReadOnlyList<ModuleInfo> All { get; } = new[]
    {
        new ModuleInfo("whiteboard", 1, "Pizarra colaborativa", "Dibuja y escribe notas junto a otro equipo de tu red.", "Fase 6"),
        new ModuleInfo("system", 2, "Información del sistema", "Consulta tu versión de Windows, el nombre del equipo y cuánto tiempo lleva encendido.", "Fase 4"),
        new ModuleInfo("hardware", 3, "Información del hardware", "Mira tu procesador, tu memoria RAM y tus adaptadores.", "Fase 4"),
        new ModuleInfo("storage", 4, "Almacenamiento", "Revisa tus unidades y cuánto espacio libre queda.", "Fase 4"),
        new ModuleInfo("network", 5, "Configuración de red", "Consulta tus adaptadores, direcciones IP y DNS.", "Fase 4"),
        new ModuleInfo("connectivity", 6, "Diagnóstico de conectividad", "Comprueba si tu equipo puede comunicarse con otros equipos.", "Fase 5"),
        new ModuleInfo("ports", 7, "Diagnóstico de puertos", "Verifica si un puerto, como el de carpetas compartidas, responde.", "Fase 5"),
        new ModuleInfo("shares", 8, "Acceso remoto", "Conéctate a un equipo remoto, explora carpetas autorizadas, transfiere archivos y verifica permisos.", "Fase 5"),
        new ModuleInfo("events", 9, "Eventos del sistema", "Revisa los errores y advertencias recientes que Windows ha registrado.", "Fase 8"),
        new ModuleInfo("reports", 10, "Informes", "Guarda un resumen de los resultados de tus pruebas.", "Fase 8"),
        new ModuleInfo("battery", 11, "Diagnóstico de batería", "Consulta el nivel de carga, la alimentación y la autonomía estimada del equipo.", "Fase 9"),
    };

    public static ModuleInfo Settings { get; } = new(
        "settings", 0, "Configuración",
        "Ajusta el equipo remoto, el puerto del servidor y otras opciones.", "Fase 5");

    public static ModuleInfo About { get; } = new(
        "about", 0, "Acerca de",
        "Información sobre el desarrollador y la organización.", "Información");

    public static ModuleInfo? Find(string key) =>
        key switch
        {
            var value when value == Settings.Key => Settings,
            var value when value == About.Key => About,
            _ => All.FirstOrDefault(m => m.Key == key)
        };
}