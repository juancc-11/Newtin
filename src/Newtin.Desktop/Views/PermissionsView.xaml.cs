using System.IO;
using System.Windows;
using System.Windows.Controls;
using Newtin.Core.Interfaces;
using Newtin.Core.Models;
using Newtin.Core.Validation;
using Newtin.Desktop.Controls;
using Newtin.Diagnostics.Connectivity;
using Newtin.Diagnostics.Permissions;
using Newtin.Diagnostics.SharedAccess;
using Newtin.Infrastructure.FileSystem;

namespace Newtin.Desktop.Views;

public partial class PermissionsView : UserControl
{
    private readonly ISettingsService _settings;
    private readonly ILogService _log;
    private readonly Action<string> _setStatus;
    private readonly SharedFolderService _shares;
    private readonly PermissionTester _tester;
    private CancellationTokenSource? _cts;

    public PermissionsView(ISettingsService settings, ILogService log, Action<string> setStatus)
    {
        InitializeComponent();
        _settings = settings;
        _log = log;
        _setStatus = setStatus;
        _shares = new SharedFolderService(new TcpPortProbe(), new WindowsSmbNative(), log);
        _tester = new PermissionTester(log);

        Form.Load(settings.Current);
        Unloaded += (_, _) => _cts?.Cancel();
    }

    private void Target_Changed(object sender, RoutedEventArgs e)
    {
        if (FormHost is null) return;
        FormHost.Visibility = RemoteRadio.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => _cts?.Cancel();

    private async void Run_Click(object sender, RoutedEventArgs e)
    {
        FormError.Visibility = Visibility.Collapsed;
        ShareRequest? request = null;
        CancellationTokenSource? cts = null;

        try
        {
            string display;
            if (RemoteRadio.IsChecked == true)
            {
                if (!Form.TryGetRequest(out request, out var error))
                {
                    ShowError(error);
                    return;
                }

                UncPathBuilder.TryBuild(request.Host, request.Share, out display, out _);
            }
            else
            {
                display = AppPaths.LocalTests;
            }

            if (!Confirm(display)) return;

            cts = new CancellationTokenSource();
            _cts = cts;
            SetBusy(true);
            ResultsPanel.Children.Clear();
            _setStatus("Ejecutando prueba de permisos...");

            if (request is null)
            {
                Directory.CreateDirectory(AppPaths.LocalTests);
                var report = await _tester.RunAsync(AppPaths.LocalTests, cts.Token);
                ShowReport(report);
            }
            else
            {
                var timeout = TimeSpan.FromSeconds(_settings.Current.Network.ConnectionTimeoutSeconds);

                // Al terminar este bloque se cierra la conexión temporal que Newtin haya creado.
                using var connection = await _shares.ConnectAsync(request, timeout, cts.Token);
                ResultsPanel.Children.Add(InfoSectionRenderer.Build(ShareFormatter.ToSection(connection.Access, request.Mode)));

                if (connection.Access.Status is ShareAccessStatus.Accessible or ShareAccessStatus.InsufficientPermissions)
                {
                    var report = await _tester.RunAsync(connection.UncPath, cts.Token);
                    ShowReport(report);
                }
            }

            _setStatus("Prueba terminada.");
        }
        catch (OperationCanceledException)
        {
            _setStatus("Prueba cancelada.");
        }
        catch (Exception ex)
        {
            _log.Error("Error inesperado en la prueba de permisos.", ex);
            ShowError($"Ocurrió un error inesperado: {ex.Message}");
            _setStatus("La prueba falló.");
        }
        finally
        {
            request?.Dispose();
            Form.ClearPassword();
            if (cts is not null && ReferenceEquals(_cts, cts)) SetBusy(false);
        }
    }

    private bool Confirm(string location)
    {
        var message =
            $"Newtin creará una carpeta temporal llamada NewtinTest_… dentro de:\n\n{location}\n\n" +
            "Creará un archivo de prueba, lo leerá, lo modificará y luego eliminará ambos. " +
            "No toca ningún otro archivo.\n\n¿Autorizas esta prueba?";

        var owner = Window.GetWindow(this) ?? Application.Current.MainWindow;
        return MessageBox.Show(owner, message, "Autorizar prueba de permisos",
            MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
    }

    private void ShowReport(PermissionTestReport report)
    {
        var sections = PermissionFormatter.ToSections(report);
        foreach (var section in sections)
            ResultsPanel.Children.Add(InfoSectionRenderer.Build(section));

        App.RecordResult("permissions", sections);
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