using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Newtin.Diagnostics.SharedAccess;

[SupportedOSPlatform("windows")]
public static class WindowsRemoteShareEnumerator
{
    private const int ErrorMoreData = 234;
    private const int ErrorAccessDenied = 5;

    public static IReadOnlyList<string> ListShares(string host)
    {
        var server = host.StartsWith(@"\\", StringComparison.Ordinal) ? host : $@"\\{host}";
        var shares = new List<string>();
        var resumeHandle = 0;
        int status;

        do
        {
            status = NetShareEnum(server, 1, out var buffer, -1,
                out var entriesRead, out _, ref resumeHandle);
            try
            {
                if (status is not (0 or ErrorMoreData))
                {
                    if (status == ErrorAccessDenied)
                        throw new UnauthorizedAccessException($"Windows no autorizó enumerar las carpetas compartidas de {server}.");
                    throw new IOException($"No se pudieron enumerar las carpetas compartidas de {server}: {new Win32Exception(status).Message}");
                }

                var entrySize = Marshal.SizeOf<ShareInfo1>();
                for (var index = 0; index < entriesRead; index++)
                {
                    var entry = Marshal.PtrToStructure<ShareInfo1>(IntPtr.Add(buffer, index * entrySize));
                    if (!string.IsNullOrWhiteSpace(entry.Name))
                        shares.Add(entry.Name);
                }
            }
            finally
            {
                if (buffer != IntPtr.Zero)
                    _ = NetApiBufferFree(buffer);
            }
        } while (status == ErrorMoreData);

        return shares;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShareInfo1
    {
        [MarshalAs(UnmanagedType.LPWStr)]
        public string? Name;
        public int Type;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string? Remark;
    }

    [DllImport("netapi32.dll", CharSet = CharSet.Unicode)]
    private static extern int NetShareEnum(
        string serverName,
        int level,
        out IntPtr buffer,
        int preferredMaximumLength,
        out int entriesRead,
        out int totalEntries,
        ref int resumeHandle);

    [DllImport("netapi32.dll")]
    private static extern int NetApiBufferFree(IntPtr buffer);
}
