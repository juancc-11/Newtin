using System.Security;

namespace Newtin.Core.Models;

public enum AuthMode { CurrentSession, Credentials }

public enum ShareAccessStatus
{
    Accessible,
    InvalidInput,
    HostUnreachable,
    ShareNotFound,
    AccessDenied,
    BadCredentials,
    AccountProblem,
    SessionConflict,
    InsufficientPermissions,
    Cancelled,
    Error
}

public sealed record ShareAccessResult(string UncPath, ShareAccessStatus Status, string Message)
{
    public bool IsAccessible => Status == ShareAccessStatus.Accessible;
}

/// <summary>Datos de una comprobación. La contraseña se conserva solo como SecureString y se libera con Dispose.</summary>
public sealed class ShareRequest : IDisposable
{
    public ShareRequest(string host, string share, AuthMode mode, string? user = null, SecureString? password = null)
    {
        Host = host;
        Share = share;
        Mode = mode;
        User = user;
        Password = password;
    }

    public string Host { get; }
    public string Share { get; }
    public AuthMode Mode { get; }
    public string? User { get; }
    public SecureString? Password { get; }

    public void Dispose() => Password?.Dispose();
}