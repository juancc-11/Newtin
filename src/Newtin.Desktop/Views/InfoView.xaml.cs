using System.Windows;
using System.Windows.Controls;
using Newtin.Core.Interfaces;
using Newtin.Desktop.Controls;

namespace Newtin.Desktop.Views;

public partial class InfoView : UserControl
{
    private readonly IInfoProvider _provider;
    private readonly Action<string> _setStatus;
    private CancellationTokenSource? _cts;
    private readonly string _moduleKey;

    public InfoView(IInfoProvider provider, Action<string> setStatus, string moduleKey)
    {
        InitializeComponent();
        _provider = provider;
        _setStatus = setStatus;
        _moduleKey = moduleKey;

        Loaded += async (_, _) => await RefreshAsync();
        Unloaded += (_, _) => _cts?.Cancel();
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshAsync();

    private void Cancel_Click(object sender, RoutedEventArgs e) => _cts?.Cancel();

    private async Task RefreshAsync()
    {
        _cts?.Cancel();
        var cts = new CancellationTokenSource();
        _cts = cts;

        SetBusy(true);
        ErrorText.Visibility = Visibility.Collapsed;
        _setStatus("Consultando...");

        try
        {
            var sections = await _provider.CollectAsync(cts.Token);

            SectionsPanel.Children.Clear();
            foreach (var section in sections)
                SectionsPanel.Children.Add(InfoSectionRenderer.Build(section));
            App.RecordResult(_moduleKey, sections);

            LastUpdated.Text = $"Última consulta: {DateTime.Now:HH:mm:ss}";
            _setStatus("Consulta completada.");
            App.Log.Info($"Consulta completada: {_provider.GetType().Name}.");
        }
        catch (OperationCanceledException)
        {
            _setStatus("Consulta cancelada.");
        }
        catch (Exception ex)
        {
            ErrorText.Text = $"No se pudo completar la consulta: {ex.Message}";
            ErrorText.Visibility = Visibility.Visible;
            _setStatus("La consulta falló.");
            App.Log.Error($"Falló la consulta de {_provider.GetType().Name}.", ex);
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
        RefreshButton.IsEnabled = !busy;
    }

    
}