using System.Windows;
using System.Windows.Controls;
using Newtin.Core.Interfaces;
using Newtin.Core.Models;
using Newtin.Desktop.Controls;
using Newtin.Diagnostics.Events;

namespace Newtin.Desktop.Views;

public partial class RecentEventsView : UserControl
{
    private readonly ILogService _log;
    private readonly Action<string> _setStatus;
    private CancellationTokenSource? _cts;
    private bool _started;

    public RecentEventsView(ILogService log, Action<string> setStatus)
    {
        InitializeComponent();
        _log = log;
        _setStatus = setStatus;

        // Al abrir el módulo se hace una primera consulta con las opciones por defecto.
        Loaded += async (_, _) =>
        {
            if (_started) return;
            _started = true;
            await RunAsync();
        };
        Unloaded += (_, _) => _cts?.Cancel();
    }

    private TimeSpan SelectedWindow =>
        Period30.IsChecked == true ? TimeSpan.FromDays(30)
        : Period7.IsChecked == true ? TimeSpan.FromDays(7)
        : TimeSpan.FromHours(24);

    private async void Run_Click(object sender, RoutedEventArgs e) => await RunAsync();

    private void Cancel_Click(object sender, RoutedEventArgs e) => _cts?.Cancel();

    private async Task RunAsync()
    {
        _cts?.Cancel();
        var cts = new CancellationTokenSource();
        _cts = cts;

        SetBusy(true);
        ErrorText.Visibility = Visibility.Collapsed;
        ResultsPanel.Children.Clear();
        _setStatus("Leyendo el registro de eventos...");

        try
        {
            var options = new EventQueryOptions(SelectedWindow, WarningsCheck.IsChecked == true);
            var provider = new RecentEventsProvider(new WindowsEventLogReader(), options);

            var sections = await provider.CollectAsync(cts.Token);
            foreach (var section in sections)
                ResultsPanel.Children.Add(InfoSectionRenderer.Build(section));

            App.RecordResult("events", sections);

            _setStatus("Consulta completada.");
            _log.Info($"Consulta de eventos del sistema completada ({EventFormatter.PeriodLabel(options.Window)}).");
        }
        catch (OperationCanceledException)
        {
            _setStatus("Consulta cancelada.");
        }
        catch (Exception ex)
        {
            _log.Error("Error inesperado al consultar los eventos del sistema.", ex);
            ErrorText.Text = $"No se pudo completar la consulta: {ex.Message}";
            ErrorText.Visibility = Visibility.Visible;
            _setStatus("La consulta falló.");
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
}