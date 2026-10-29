using System.Security;

namespace Newtin.Core.Interfaces;

/// <summary>Conexiones SMB con credenciales explícitas. Ambos métodos devuelven el código de error de Windows (0 = correcto).</summary>
public interface ISmbNative
{
    int Connect(string uncPath, string user, SecureString password);
    int Disconnect(string uncPath);
}