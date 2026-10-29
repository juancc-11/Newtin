using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Newtin.Core.Interfaces;
using Newtin.Core.Validation;
using Newtin.Diagnostics.Network;
using Newtin.Server.Hosting;

namespace Newtin.Desktop.Views;

public partial class SettingsView : UserControl
{
    private readonly ISettingsService _settings;
    private readonly Action<string> _setStatus;

    public SettingsView(ISettingsService settings, Action<string> setStatus)
    {
        InitializeComponent();
        _settings = settings;
        _setStatus = setStatus;
        LoadFields();
    }

    private void LoadFields()
    {
        var current = _settings.Current;
        HostBox.Text = current.Network.RemoteHost;
        ShareBox.Text = current.Network.RemoteShare;
        UserBox.Text = current.Network.RemoteUser;
        TimeoutBox.Text = current.Network.ConnectionTimeoutSeconds.ToString();
        PortBox.Text = current.WhiteboardServer.Port.ToString();
        LanCheck.IsChecked = current.WhiteboardServer.ShareOnLan;
        LanAddressBox.Text = current.WhiteboardServer.LanAddress;
        RestoreCheck.IsChecked = current.WhiteboardServer.RestoreLastBoard;

        var detected = LanAddressFinder.GetCandidates();
        DetectedText.Text = detected.Count == 0
            ? "No se detectaron adaptadores de red activos."
            : "Direcciones detectadas: " + string.Join(", ", detected.Select(c => $"{c.Address} ({c.AdapterName})"))
              + ". Déjalo vacío para elegir automáticamente la que está en la red del equipo remoto.";
        MessageText.Visibility = Visibility.Collapsed;
    }

    private void Discard_Click(object sender, RoutedEventArgs e) => LoadFields();

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(TimeoutBox.Text.Trim(), out var timeout))
        {
            Show("El tiempo de espera debe ser un número entero.", isError: true);
            return;
        }

        if (!int.TryParse(PortBox.Text.Trim(), out var port))
        {
            Show("El puerto debe ser un número entero.", isError: true);
            return;
        }

        var host = HostBox.Text.Trim();
        if (host.Length > 0 && HostValidator.TryNormalize(host, out var normalized, out _))
            host = normalized;

        var updated = _settings.Current.Clone();
        updated.Network.RemoteHost = host;
        updated.Network.RemoteShare = ShareBox.Text.Trim();
        updated.Network.RemoteUser = UserBox.Text.Trim();
        updated.Network.ConnectionTimeoutSeconds = timeout;
        updated.WhiteboardServer.Port = port;
        updated.WhiteboardServer.ShareOnLan = LanCheck.IsChecked == true;
        updated.WhiteboardServer.LanAddress = LanAddressBox.Text.Trim();
        updated.WhiteboardServer.RestoreLastBoard = RestoreCheck.IsChecked == true;

        if (_settings.TrySave(updated, out var error))
        {
            LoadFields();

            var note = App.Server.State == ServerState.Running
                ? " Los cambios del servidor se aplicarán la próxima vez que lo inicies."
                : "";
            Show("Configuración guardada." + note, isError: false);
            _setStatus("Configuración guardada.");
        }
        else
        {
            Show(error, isError: true);
        }
    }

    private void Show(string text, bool isError)
    {
        MessageText.Text = text;
        MessageText.Foreground = (Brush)FindResource(isError ? "ErrorBrush" : "SuccessBrush");
        MessageText.Visibility = Visibility.Visible;
    }
}