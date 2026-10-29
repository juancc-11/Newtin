using System.IO;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Newtin.Core.Interfaces;
using Newtin.Core.Models;
using Newtin.Desktop.Controls;
using Newtin.Desktop.Services;
using Newtin.Infrastructure.FileSystem;
using Newtin.Server.Hosting;
using Microsoft.Win32;

namespace Newtin.Desktop.Views;

public partial class WhiteboardView : UserControl
{
    private readonly ServerHostService _server;
    private readonly ISettingsService _settings;
    private readonly Action<string> _setStatus;

    private WebView2? _browser;
    private string? _loadedFor;     // código de acceso con el que se cargó la página incrustada
    private bool _boardVisible;
    private bool _busy;

    public WhiteboardView(ServerHostService server, ISettingsService settings, Action<string> setStatus)
    {
        InitializeComponent();
        _server = server;
        _settings = settings;
        _setStatus = setStatus;

        Loaded += (_, _) =>
        {
            _server.Changed += OnServerChanged;
            Render();
        };
        Unloaded += (_, _) =>
        {
            _server.Changed -= OnServerChanged;
            _browser?.Dispose();   // libera el proceso de WebView2 al salir de este módulo
            _browser = null;
        };
    }

    private void OnServerChanged(object? sender, EventArgs e)
    {
        // Si el servidor se detiene, la pizarra incrustada ya no tiene a qué conectarse.
        if (_server.State == ServerState.Stopped && _boardVisible)
        {
            _loadedFor = null;
            SetBoardVisible(false);
        }

        Render();
    }

    private void Render()
    {
        var running = _server.State == ServerState.Running;

        SectionsPanel.Children.Clear();
        SectionsPanel.Children.Add(InfoSectionRenderer.Build(BuildSection(running)));

        ToggleButton.Content = running ? "Detener servidor" : "Iniciar servidor";
        ToggleButton.IsEnabled = _server.State is ServerState.Running or ServerState.Stopped;
        BoardButton.IsEnabled = !_busy && _server.State is ServerState.Running or ServerState.Stopped;
        OpenButton.IsEnabled = running;
        CopyLinkButton.IsEnabled = running;
        CopyCodeButton.IsEnabled = running;
    }

    private InfoSection BuildSection(bool running)
    {
        var configured = _settings.Current.WhiteboardServer;

        var (stateText, level) = _server.State switch
        {
            ServerState.Running => ("Activo", InfoLevel.Success),
            ServerState.Starting => ("Iniciando...", InfoLevel.Warning),
            ServerState.Stopping => ("Deteniendo...", InfoLevel.Warning),
            _ => ("Detenido", InfoLevel.Normal)
        };

        var items = new List<InfoItem>
        {
            new("Estado", stateText, null, null, level),
            new("Alcance configurado",
                configured.ShareOnLan ? "Red local (otros equipos pueden conectarse)" : "Solo este equipo",
                configured.ShareOnLan ? null : "Para que otro equipo se conecte, actívalo en Configuración."),
            new("Autoguardado", "Activo", App.Files.AutosavePath),
            new("Puerto", configured.Port.ToString())
        };

        if (running)
        {
            items.Add(new InfoItem("Dirección en este equipo", _server.LocalUrl ?? InfoItem.NotAvailable));

            if (_server.LanUrl is not null)
                items.Add(new InfoItem("Dirección para otros equipos", _server.LanUrl,
                    "Escríbela en el navegador del otro equipo."));

            items.Add(new InfoItem("Código de acceso", _server.AccessCode ?? InfoItem.NotAvailable,
                "Se genera cada vez que inicias el servidor y se pide al conectarse."));

            var names = _server.ClientNames;
            items.Add(new InfoItem("Clientes conectados", _server.ClientCount.ToString(),
                names.Count == 0 ? "Ninguno todavía." : string.Join(", ", names)));

            items.Add(new InfoItem("Elementos en la pizarra", _server.ElementCount.ToString()));
        }

        return new InfoSection("Servidor de la pizarra", items);
    }

    // ---------- pizarra incrustada (WebView2) ----------

    private async void Board_Click(object sender, RoutedEventArgs e)
    {
        if (_boardVisible)
        {
            SetBoardVisible(false);
            return;
        }

        await ShowBoardAsync();
    }

    private async Task ShowBoardAsync()
    {
        if (_busy) return;
        _busy = true;
        BoardButton.IsEnabled = false;

        try
        {
            if (_server.State != ServerState.Running)
            {
                var (ok, message) = await _server.ToggleAsync();
                Show(message, ok);
                if (!ok || _server.State != ServerState.Running) return;
            }

            if (!IsRuntimeAvailable())
            {
                Show("WebView2 no está instalado en este equipo. Instala el «WebView2 Runtime» de Microsoft " +
                     "o usa «Abrir en el navegador».", ok: false);
                return;
            }

            // Debe estar visible para que WebView2 pueda inicializarse.
            SetBoardVisible(true);

            if (_browser is null) await CreateBrowserAsync();

            if (_browser?.CoreWebView2 is not null && _loadedFor != _server.AccessCode)
            {
                _loadedFor = _server.AccessCode;
                _browser.CoreWebView2.Navigate(EmbeddedUrl());
            }
        }
        catch (Exception ex)
        {
            App.Log.Error("No se pudo mostrar la pizarra incrustada.", ex);
            SetBoardVisible(false);
            Show($"No se pudo mostrar la pizarra aquí: {ex.Message} Puedes usar «Abrir en el navegador».", ok: false);
        }
        finally
        {
            _busy = false;
            Render();
        }
    }

    private async Task CreateBrowserAsync()
    {
        // Los datos del navegador incrustado viven en el perfil del usuario, no junto al ejecutable.
        var environment = await CoreWebView2Environment.CreateAsync(
            userDataFolder: Path.Combine(AppPaths.Root, "webview2"));

        var browser = new WebView2();
        BoardHost.Children.Add(browser);
        await browser.EnsureCoreWebView2Async(environment);

        var settings = browser.CoreWebView2.Settings;
        settings.AreDevToolsEnabled = false;
        settings.AreDefaultContextMenusEnabled = false;
        settings.IsStatusBarEnabled = false;
        settings.IsPasswordAutosaveEnabled = false;
        settings.IsGeneralAutofillEnabled = false;

        settings.IsZoomControlEnabled = false;
        settings.IsPinchZoomEnabled = false;

        browser.CoreWebView2.NavigationStarting += OnNavigationStarting;
        browser.CoreWebView2.NewWindowRequested += (_, args) => args.Handled = true;
        browser.CoreWebView2.ProcessFailed += (_, args) =>
            App.Log.Warning($"Falló un proceso de WebView2: {args.ProcessFailedKind}.");

        _browser = browser;
    }

    /// <summary>La pizarra incrustada solo puede ir al servidor local de Newtin.</summary>
    private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (!Uri.TryCreate(e.Uri, UriKind.Absolute, out var target) || !IsOwnOrigin(target))
            e.Cancel = true;
    }

    private bool IsOwnOrigin(Uri target) =>
        _server.LocalUrl is { } local
        && Uri.TryCreate(local, UriKind.Absolute, out var own)
        && target.Scheme == own.Scheme
        && target.Host == own.Host
        && target.Port == own.Port;

    private string EmbeddedUrl() =>
        $"{_server.LocalUrl}/#code={_server.AccessCode}&name={Uri.EscapeDataString(EmbeddedName())}&auto=1";

    /// <summary>Nombre que verán los demás: el del equipo, solo con caracteres que el servidor acepta.</summary>
    private static string EmbeddedName()
    {
        var cleaned = new string(Environment.MachineName
            .Where(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.')
            .ToArray());

        if (cleaned.Length == 0) cleaned = "Newtin";
        return cleaned.Length > 30 ? cleaned[..30] : cleaned;
    }

    private static bool IsRuntimeAvailable()
    {
        try
        {
            return !string.IsNullOrEmpty(CoreWebView2Environment.GetAvailableBrowserVersionString());
        }
        catch (Exception ex) when (ex is WebView2RuntimeNotFoundException or COMException or ArgumentException)
        {
            return false;
        }
    }

    private void SetBoardVisible(bool visible)
    {
        _boardVisible = visible;
        BoardHost.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        InfoScroll.Visibility = visible ? Visibility.Collapsed : Visibility.Visible;
        BoardButton.Content = visible ? "Ver panel del servidor" : "Ver pizarra aquí";
    }

    // ---------- servidor y portapapeles ----------

    private async void Toggle_Click(object sender, RoutedEventArgs e)
    {
        ToggleButton.IsEnabled = false;
        try
        {
            var (ok, message) = await _server.ToggleAsync();
            Show(message, ok);
            _setStatus(message);
        }
        catch (Exception ex)
        {
            App.Log.Error("Error al cambiar el estado del servidor.", ex);
            Show($"No se pudo cambiar el estado del servidor: {ex.Message}", ok: false);
        }
        finally
        {
            Render();
        }
    }

    private string? LinkWithCode()
    {
        var baseUrl = _server.LanUrl ?? _server.LocalUrl;
        return baseUrl is null || _server.AccessCode is null ? null : $"{baseUrl}/#code={_server.AccessCode}";
    }

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        var link = _server.LocalUrl is null || _server.AccessCode is null
            ? null
            : $"{_server.LocalUrl}/#code={_server.AccessCode}";
        if (link is null) return;

        try
        {
            Process.Start(new ProcessStartInfo(link) { UseShellExecute = true });
        }
        catch (Win32Exception ex)
        {
            Show($"No se pudo abrir el navegador: {ex.Message}", ok: false);
        }
    }

    private void CopyLink_Click(object sender, RoutedEventArgs e) =>
        Copy(LinkWithCode(), "Enlace copiado. Contiene el código de acceso: compártelo solo con quien quieras.");

    private void CopyCode_Click(object sender, RoutedEventArgs e) =>
        Copy(_server.AccessCode, "Código copiado.");

    private void Copy(string? text, string successMessage)
    {
        if (text is null) return;

        try
        {
            Clipboard.SetText(text);
            Show(successMessage, ok: true);
        }
        catch (COMException)
        {
            Show("No se pudo copiar: otro programa está usando el portapapeles. Inténtalo de nuevo.", ok: false);
        }
    }

    private void Show(string text, bool ok)
    {
        MessageText.Text = text;
        MessageText.Foreground = (Brush)FindResource(ok ? "SuccessBrush" : "ErrorBrush");
        MessageText.Visibility = Visibility.Visible;
    }

        // ---------- archivos ----------

    private static Window? Owner => Application.Current.MainWindow;

    private static string? PickSavePath(string title, string filter, string defaultExtension)
    {
        var dialog = new SaveFileDialog
        {
            Title = title,
            Filter = filter,
            DefaultExt = defaultExtension,
            AddExtension = true,
            OverwritePrompt = true,
            FileName = $"Pizarra-{DateTime.Now:yyyyMMdd-HHmm}",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        };
        return dialog.ShowDialog(Owner) == true ? dialog.FileName : null;
    }

    private static string? PickOpenPath(string title, string filter)
    {
        var dialog = new OpenFileDialog
        {
            Title = title,
            Filter = filter,
            CheckFileExists = true,
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        };
        return dialog.ShowDialog(Owner) == true ? dialog.FileName : null;
    }

    private static bool Confirm(string question) =>
        MessageBox.Show(Owner, question, "Newtin", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;

    private void Report(FileOpResult result)
    {
        var text = result.Message;
        if (result.Warnings.Count > 0)
            text += "\n" + string.Join("\n", result.Warnings.Take(5).Select(w => "• " + w));

        Show(text, result.Ok);
        _setStatus(result.Message);
        Render();
    }

    private async Task RunFileActionAsync(Func<Task<FileOpResult>> action)
    {
        try
        {
            Report(await action());
        }
        catch (Exception ex)
        {
            App.Log.Error("Error inesperado en una operación de archivo de la pizarra.", ex);
            Show($"Ocurrió un error inesperado: {ex.Message}", ok: false);
        }
    }

    private async void FileSaveAs_Click(object sender, RoutedEventArgs e)
    {
        var path = PickSavePath("Guardar pizarra", "Pizarra de Newtin (*.newtin)|*.newtin", ".newtin");
        if (path is not null) await RunFileActionAsync(() => Task.FromResult(App.Files.Save(path)));
    }

    private async void FileExport_Click(object sender, RoutedEventArgs e)
    {
        const string filter = "Imagen PNG (*.png)|*.png|Imagen vectorial SVG (*.svg)|*.svg|" +
                              "Markdown (*.md)|*.md|CSV (*.csv)|*.csv|Pizarra de Newtin (*.newtin)|*.newtin";
        var path = PickSavePath("Exportar pizarra", filter, ".png");
        if (path is not null) await RunFileActionAsync(() => Task.FromResult(App.Files.Export(path)));
    }

    private async void FileOpen_Click(object sender, RoutedEventArgs e)
    {
        var path = PickOpenPath("Abrir pizarra", "Pizarra de Newtin (*.newtin;*.json)|*.newtin;*.json");
        if (path is null) return;

        if (!App.Files.IsEmpty && !Confirm("Abrir una pizarra reemplaza la actual para todos los participantes. ¿Quieres continuar?"))
            return;

        await RunFileActionAsync(() => App.Files.OpenAsync(path));
    }

    private async void FileImport_Click(object sender, RoutedEventArgs e)
    {
        var path = PickOpenPath("Importar a la pizarra",
            "Pizarra, Markdown, CSV o texto|*.newtin;*.json;*.md;*.markdown;*.csv;*.txt|Todos los archivos|*.*");
        if (path is not null) await RunFileActionAsync(() => App.Files.ImportAsync(path));
    }
    private async void FileNew_Click(object sender, RoutedEventArgs e)
    {
        if (!App.Files.IsEmpty && !Confirm("Se vaciará la pizarra para todos los participantes. Guarda antes una copia si la necesitas. ¿Continuar?"))
            return;

        await RunFileActionAsync(async () =>
        {
            await App.Files.NewBoardAsync();
            return new FileOpResult(true, "Pizarra nueva.", []);
        });
    }
}