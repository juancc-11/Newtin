using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security;
using Newtin.Core.Interfaces;

namespace Newtin.Diagnostics.SharedAccess;

/// <summary>Usa las API nativas de Windows (mpr.dll). No construye comandos de consola ni muestra diálogos.</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsSmbNative : ISmbNative
{
    private const int ResourceGlobalNet = 2;
    private const int ResourceTypeDisk = 1;
    private const int ConnectTemporary = 4;   // no se guarda en el perfil del usuario

    public int Connect(string uncPath, string user, SecureString password)
    {
        var resource = new NetResource
        {
            Scope = ResourceGlobalNet,
            Type = ResourceTypeDisk,
            RemoteName = uncPath
        };

        var passwordPointer = IntPtr.Zero;
        try
        {
            // La contraseña pasa de SecureString a memoria no administrada sin convertirse en string.
            passwordPointer = Marshal.SecureStringToGlobalAllocUnicode(password);
            return WNetAddConnection2(ref resource, passwordPointer, user, ConnectTemporary);
        }
        finally
        {
            if (passwordPointer != IntPtr.Zero)
                Marshal.ZeroFreeGlobalAllocUnicode(passwordPointer);
        }
    }

    // Sin force: si la conexión está en uso, Windows la conserva en lugar de cerrarla a la fuerza.
    public int Disconnect(string uncPath) => WNetCancelConnection2(uncPath, 0, false);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NetResource
    {
        public int Scope;
        public int Type;
        public int DisplayType;
        public int Usage;
        public string? LocalName;
        public string? RemoteName;
        public string? Comment;
        public string? Provider;
    }

    [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
    private static extern int WNetAddConnection2(ref NetResource netResource, IntPtr password, string? userName, int flags);

    [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
    private static extern int WNetCancelConnection2(string name, int flags, [MarshalAs(UnmanagedType.Bool)] bool force);
}