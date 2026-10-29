using Newtin.Core.Models;
using static Newtin.Core.Models.ShareAccessStatus;

namespace Newtin.Diagnostics.SharedAccess;

public static class ShareFormatter
{
    public static string Describe(ShareAccessStatus status) => status switch
    {
        Accessible => "Acceso correcto",
        InvalidInput => "Datos no válidos",
        HostUnreachable => "Equipo inaccesible",
        ShareNotFound => "La carpeta compartida no existe",
        AccessDenied => "Acceso denegado",
        BadCredentials => "Credenciales incorrectas",
        AccountProblem => "Problema con la cuenta",
        SessionConflict => "Conflicto con una sesión existente",
        InsufficientPermissions => "Permisos insuficientes",
        Cancelled => "Prueba cancelada",
        _ => "Error"
    };

    public static InfoLevel LevelOf(ShareAccessStatus status) => status switch
    {
        Accessible => InfoLevel.Success,
        Cancelled => InfoLevel.Normal,
        InsufficientPermissions or SessionConflict => InfoLevel.Warning,
        _ => InfoLevel.Error
    };

    public static InfoSection ToSection(ShareAccessResult result, AuthMode mode) => new("Carpeta compartida",
    [
        new InfoItem("Ruta", string.IsNullOrEmpty(result.UncPath) ? InfoItem.NotAvailable : result.UncPath),
        new InfoItem("Modalidad", mode == AuthMode.Credentials ? "Credenciales proporcionadas" : "Sesión actual de Windows"),
        new InfoItem("Estado", Describe(result.Status), null, null, LevelOf(result.Status)),
        new InfoItem("Detalle", result.Message)
    ]);
}