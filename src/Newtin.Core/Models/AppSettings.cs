namespace Newtin.Core.Models;

public sealed class AppSettings
{
    public NetworkSettings Network { get; set; } = new();
    public ServerSettings WhiteboardServer { get; set; } = new();

    public AppSettings Clone() => new()
    {
        Network = new NetworkSettings
        {
            RemoteHost = Network.RemoteHost,
            RemoteShare = Network.RemoteShare,
            RemoteUser = Network.RemoteUser,
            ConnectionTimeoutSeconds = Network.ConnectionTimeoutSeconds
        },
        WhiteboardServer = new ServerSettings
        {
            Port = WhiteboardServer.Port,
            Enabled = WhiteboardServer.Enabled,
            ShareOnLan = WhiteboardServer.ShareOnLan,
            LanAddress = WhiteboardServer.LanAddress,
            RestoreLastBoard = WhiteboardServer.RestoreLastBoard
        }
    };
}

public sealed class NetworkSettings
{
    public string RemoteHost { get; set; } = "";
    public string RemoteShare { get; set; } = "";
    public string RemoteUser { get; set; } = "";   // El usuario no es un secreto; la contraseña NUNCA se guarda.
    public int ConnectionTimeoutSeconds { get; set; } = 5;
}

public sealed class ServerSettings
{
    public int Port { get; set; } = 5000;
    public bool Enabled { get; set; }            // Reservado: el servidor siempre se inicia manualmente.
    public bool ShareOnLan { get; set; }         // false = solo este equipo
    public string LanAddress { get; set; } = ""; // vacío = elegir automáticamente
    public bool RestoreLastBoard { get; set; } = true;   // restaurar la última pizarra al abrir Newtin
}