using System.ComponentModel;
using Newtin.Core.Models;
using static Newtin.Core.Models.ShareAccessStatus;

namespace Newtin.Diagnostics.SharedAccess;

public static class SmbErrorMapper
{
    /// <summary>Extrae el código de error de Windows de una excepción de E/S, si lo contiene.</summary>
    public static int? Win32CodeFrom(Exception exception)
    {
        var hr = exception.HResult;
        return (hr & unchecked((int)0xFFFF0000)) == unchecked((int)0x80070000) ? hr & 0xFFFF : null;
    }

    public static (ShareAccessStatus Status, string Message) FromWin32(int code, string host) => code switch
    {
        0 => (Accessible, "Conexión establecida."),

        5 or 65 => (AccessDenied,
            "Windows denegó el acceso. Revisa que el usuario tenga permiso sobre la carpeta compartida."),

        53 or 1203 or 1231 => (HostUnreachable,
            $"Windows no encontró la ruta de red hacia {host}. Si usaste un nombre de equipo, prueba con su dirección IP."),

        67 => (ShareNotFound,
            "El equipo respondió, pero no existe una carpeta compartida con ese nombre. Revisa el nombre exacto."),

        86 or 1326 or 2202 => (BadCredentials,
            "Usuario o contraseña incorrectos. Para una cuenta local del otro equipo prueba con el formato NOMBRE_DEL_EQUIPO\\usuario."),

        1909 => (AccountProblem,
            "La cuenta está bloqueada por demasiados intentos fallidos. No sigas intentando: desbloquéala en el otro equipo."),

        1331 => (AccountProblem, "La cuenta está deshabilitada en el otro equipo."),

        1330 => (AccountProblem, "La contraseña de la cuenta expiró; debe cambiarse en el otro equipo."),

        1327 or 1385 => (AccountProblem,
            "La cuenta no tiene permitido iniciar sesión desde la red en ese equipo."),

        1219 => (SessionConflict,
            $"Windows ya tiene abierta una conexión con {host} usando otro usuario y no permite dos identidades al mismo equipo. " +
            "Newtin no la cierra por ti. Para revisarla, abre una terminal y ejecuta: net use. " +
            $"Si decides cerrarla, ejecuta: net use \\\\{host} /delete. Después vuelve a probar."),

        _ => (Error, $"Windows devolvió el código {code}: {new Win32Exception(code).Message}")
    };
}