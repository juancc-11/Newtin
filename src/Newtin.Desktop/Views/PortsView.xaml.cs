using System.Windows;
using System.Windows.Controls;
using Newtin.Core.Interfaces;
using Newtin.Core.Models;
using Newtin.Core.Validation;
using Newtin.Desktop.Controls;
using Newtin.Diagnostics.Connectivity;


namespace Newtin.Desktop.Views;

public partial class PortsView : UserControl
{
    private readonly ISettingsService _settings;
    private readonly ILogService _log;
    private readonly Action<string> _setStatus;
    private readonly ConnectivityService _service;
    private CancellationTokenSource? _cts;

    public PortsView(ISettingsService settings, ILogService log, Action<string> setStatus)
    {
        InitializeComponent();
        _settings = settings;
        _log = log;
        _setStatus = setStatus;
        _service = new ConnectivityService(new PingProbe(), new TcpPortProbe(), log);

        HostBox.Text = settings.Current.Network.RemoteHost;
        ServerCheck.Content = $"{settings.Current.WhiteboardServer.Port} · Servidor Newtin (pizarra)";
        Unloaded += (_, _) => _cts?.Cancel();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => _cts?.Cancel();

    private async void Run_Click(object sender, RoutedEventArgs e)
    {
        FormError.Visibility = Visibility.Collapsed;

        if (!HostValidator.TryNormalize(HostBox.Text, out var host, out var hostError))
        {
            ShowError(hostError);
            return;
        }

        var targets = new List<PortTarget>();
        void Add(PortTarget target)
        {
            if (targets.All(t => t.Port != target.Port)) targets.Add(target);
        }

        if (SmbCheck.IsChecked == true) Add(new PortTarget(445, "SMB"));
        if (HttpCheck.IsChecked == true) Add(new PortTarget(80, "HTTP"));
        if (HttpsCheck.IsChecked == true) Add(new PortTarget(443, "HTTPS"));
        if (ServerCheck.IsChecked == true)
            Add(new PortTarget(_settings.Current.WhiteboardServer.Port, "Servidor Newtin"));

        var custom = CustomPortBox.Text.Trim();
        if (custom.Length > 0)
        {
            if (!PortValidator.TryParse(custom, out var customPort))
            {
                ShowError("El puerto adicional debe ser un número entre 1 y 65535.");
                return;
            }
            Add(new PortTarget(customPort, "Puerto adicional"));
        }

        if (targets.Count == 0)
        {
            ShowError("Selecciona al menos un puerto para comprobar.");
            return;
        }

        var cts = new CancellationTokenSource();
        _cts = cts;
        SetBusy(true);
        ResultsPanel.Children.Clear();
        _setStatus("Comprobando puertos...");

        try
        {
            var timeout = TimeSpan.FromSeconds(_settings.Current.Network.ConnectionTimeoutSeconds);
            var results = await _service.CheckPortsAsync(host, targets, timeout, cts.Token);
            var section = ProbeFormatter.ToPortSection(host, results);
            ResultsPanel.Children.Add(InfoSectionRenderer.Build(section));

            if (results.Any(r => r.Result.Status != TcpProbeStatus.Cancelled))
                App.RecordResult("ports", [section]);
            _setStatus("Comprobación terminada.");
        }
        catch (Exception ex)
        {
            _log.Error("Error inesperado en el diagnóstico de puertos.", ex);
            ShowError($"Ocurrió un error inesperado: {ex.Message}");
            _setStatus("La comprobación falló.");
        }
        finally
        {
            if (ReferenceEquals(_cts, cts)) SetBusy(false);
        }
    }

    private void SetBusy(bool busy)
    {
        LoadingBar.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        CancelButton.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        RunButton.IsEnabled = !busy;
    }

    private void ShowError(string text)
    {
        FormError.Text = text;
        FormError.Visibility = Visibility.Visible;
    }
}