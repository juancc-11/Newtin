using System.Windows;
using System.Windows.Controls;
using Newtin.Core.Interfaces;
using Newtin.Core.Validation;
using Newtin.Desktop.Controls;
using Newtin.Diagnostics.Connectivity;
using Newtin.Core.Models;

namespace Newtin.Desktop.Views;

public partial class ConnectivityView : UserControl
{
    private readonly ISettingsService _settings;
    private readonly ILogService _log;
    private readonly Action<string> _setStatus;
    private readonly ConnectivityService _service;
    private CancellationTokenSource? _cts;

    public ConnectivityView(ISettingsService settings, ILogService log, Action<string> setStatus)
    {
        InitializeComponent();
        _settings = settings;
        _log = log;
        _setStatus = setStatus;
        _service = new ConnectivityService(new PingProbe(), new TcpPortProbe(), log);

        HostBox.Text = settings.Current.Network.RemoteHost;
        PortBox.Text = "445";
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

        if (!PortValidator.TryParse(PortBox.Text, out var port))
        {
            ShowError("El puerto debe ser un número entre 1 y 65535.");
            return;
        }

        var cts = new CancellationTokenSource();
        _cts = cts;
        SetBusy(true);
        ResultsPanel.Children.Clear();
        _setStatus("Comprobando conectividad...");

        try
        {
            var timeout = TimeSpan.FromSeconds(_settings.Current.Network.ConnectionTimeoutSeconds);
            var report = await _service.CheckAsync(host, port, timeout, cts.Token);

            var sections = ProbeFormatter.ToSections(report);
            foreach (var section in sections)
                ResultsPanel.Children.Add(InfoSectionRenderer.Build(section));

            if (report.Tcp.Status != TcpProbeStatus.Cancelled)
                App.RecordResult("connectivity", sections);
                
            _setStatus("Comprobación terminada.");
        }
        catch (Exception ex)
        {
            _log.Error("Error inesperado en el diagnóstico de conectividad.", ex);
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