using System.Net;
using System.Net.Sockets;

namespace Newtin.Diagnostics.Network;

public static class PortAvailability
{
    public static bool IsFree(IPAddress address, int port)
    {
        try
        {
            var listener = new TcpListener(address, port);
            listener.Start();
            listener.Stop();
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }
}