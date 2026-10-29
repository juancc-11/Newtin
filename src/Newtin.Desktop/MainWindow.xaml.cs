using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using Newtin.Desktop.Services;
using Newtin.Desktop.Views;
using Newtin.Diagnostics.Hardware;
using Newtin.Diagnostics.Network;
using Newtin.Diagnostics.Storage;
using Newtin.Diagnostics.SystemInfo;
using System.Windows.Media;
using Newtin.Server.Hosting;

namespace Newtin.Desktop;

public partial class MainWindow : Window
{
    private const string HomeKey = "home";
    private bool _allowClose;

    public MainWindow()
    {
        InitializeComponent();
        VersionText.Text = $"Versión {GetVersion()}";
        BuildNavigation();
        App.Server.Changed += OnServerChanged;
        Closed += (_, _) => App.Server.Changed -= OnServerChanged;
        UpdateServerIndicator();
        Loaded += OnLoaded;
    }

    private static string GetVersion()
    {
        var v = Assembly.GetExecutingAssembly().GetName().Version;
        return v is null ? "0.1.0" : $"{v.Major}.{v.Minor}.{v.Build}";
    }

    private void BuildNavigation()
    {
        AddNavButton(HomeKey, "Inicio");
        foreach (var module in ModuleCatalog.All)
            AddNavButton(module.Key, $"{module.Number}. {module.Title}");

        NavigateTo(HomeKey);
    }

    private void AddNavButton(string key, string text)
    {
        var button = new RadioButton
        {
            Content = text,
            Tag = key,
            GroupName = "Nav",
            Style = (Style)FindResource("NavButtonStyle")
        };
        button.Checked += Nav_Checked;
        NavPanel.Children.Add(button);
    }

    public void NavigateTo(string key)
    {
        var target = NavPanel.Children.OfType<RadioButton>()
            .Append(AboutNav)
            .Append(SettingsNav)
            .FirstOrDefault(r => (string?)r.Tag == key);

        if (target is not null)
            target.IsChecked = true;
    }

    private void Nav_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Tag: string key })
            ShowPage(key);
    }

    private void ShowPage(string key)
    {
        if (key == HomeKey)
        {
            PageTitle.Text = "Inicio";
            PageSubtitle.Text = "Elige una herramienta para comenzar.";
            ContentHost.Content = new DashboardView(ModuleCatalog.All, NavigateTo);
            StatusMessage.Text = "Listo.";
            return;
        }

        var module = ModuleCatalog.Find(key);
        if (module is null) return;

        PageTitle.Text = module.Number > 0 ? $"{module.Number}. {module.Title}" : module.Title;
        PageSubtitle.Text = module.Description;
        StatusMessage.Text = "Listo.";
        ContentHost.Content = CreateContent(module);
    }

private UIElement CreateContent(ModuleInfo module) => module.Key switch
    {
        "system" => new InfoView(new SystemInfoProvider(), SetStatus, module.Key),
        "hardware" => new InfoView(new HardwareInfoProvider(), SetStatus, module.Key),
        "battery" => new InfoView(new BatteryInfoProvider(), SetStatus, module.Key),
        "storage" => new InfoView(new StorageInfoProvider(), SetStatus, module.Key),
        "network" => new InfoView(new NetworkInfoProvider(), SetStatus, module.Key),
        "reports" => new ReportsView(App.Settings, App.Log, SetStatus),
        "connectivity" => new ConnectivityView(App.Settings, App.Log, SetStatus),
        "ports" => new PortsView(App.Settings, App.Log, SetStatus),
        "settings" => new SettingsView(App.Settings, SetStatus),
        "about" => new AboutView(),
        "shares" => new SharedAccessView(App.Settings, App.Log, SetStatus),
        "whiteboard" => new WhiteboardView(App.Server, App.Settings, SetStatus),
        "events" => new RecentEventsView(App.Log, SetStatus),
        _ => new ModulePlaceholderView(module, () => NavigateTo(HomeKey))
    };

private void SetStatus(string text) => StatusMessage.Text = text;

    // Pantalla de bienvenida breve. En fases posteriores esperará la carga real de la configuración.
    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await Task.Delay(TimeSpan.FromSeconds(2));
        LoadingOverlay.Visibility = Visibility.Collapsed;
        if (App.Settings.LoadWarning is { } warning)
            StatusMessage.Text = warning;
    }

    private void Exit_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_allowClose) return;

        var result = MessageBox.Show(this,
            "¿Estás seguro de que deseas salir de Newtin?",
            "Confirmar salida",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes)
        {
            e.Cancel = true;
            return;
        }

        _allowClose = true;
    }

        private void OnServerChanged(object? sender, EventArgs e) => UpdateServerIndicator();

    private void UpdateServerIndicator()
    {
        var server = App.Server;
        var (text, brush, button, enabled) = server.State switch
        {
            ServerState.Running => ($"Servidor activo · {server.ClientCount} cliente(s)", "SuccessBrush", "Detener servidor", true),
            ServerState.Starting => ("Iniciando servidor...", "WarningBrush", "Iniciar servidor", false),
            ServerState.Stopping => ("Deteniendo servidor...", "WarningBrush", "Detener servidor", false),
            _ => ("Servidor detenido", "TextSecondaryBrush", "Iniciar servidor", true)
        };

        ServerStatusText.Text = text;
        ServerDot.Fill = (Brush)FindResource(brush);
        ServerToggleButton.Content = button;
        ServerToggleButton.IsEnabled = enabled;
    }

    private async void ServerToggle_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var (_, message) = await App.Server.ToggleAsync();
            StatusMessage.Text = message;
        }
        catch (Exception ex)
        {
            App.Log.Error("Error al cambiar el estado del servidor.", ex);
            StatusMessage.Text = $"No se pudo cambiar el estado del servidor: {ex.Message}";
        }
    }
}