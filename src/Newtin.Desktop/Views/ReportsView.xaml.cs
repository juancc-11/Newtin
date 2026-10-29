using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Newtin.Core.Interfaces;
using Newtin.Desktop.Services;
using Newtin.Diagnostics.Events;
using Newtin.Diagnostics.Hardware;
using Newtin.Diagnostics.Network;
using Newtin.Diagnostics.Reports;
using Newtin.Diagnostics.Storage;
using Newtin.Diagnostics.SystemInfo;
using Newtin.Core.Models;

namespace Newtin.Desktop.Views;

public partial class ReportsView : UserControl
{
    private readonly ILogService _log;
    private readonly Action<string> _setStatus;
    private readonly ReportFilesService _files;
    private readonly Dictionary<string, CheckBox> _checks = new();
    private CancellationTokenSource? _cts;

    public ReportsView(ISettingsService settings, ILogService log, Action<string> setStatus)
    {
        InitializeComponent();
        _log = log;
        _setStatus = setStatus;
        _files = new ReportFilesService(settings, log);

        Loaded += (_, _) =>
        {
            App.Reports.Changed += OnReportsChanged;
            Refresh();
        };
        Unloaded += (_, _) =>
        {
            App.Reports.Changed -= OnReportsChanged;
            _cts?.Cancel();
        };
    }

    private Brush Res(string key) => (Brush)FindResource(key);

    private void OnReportsChanged() => Dispatcher.BeginInvoke(new Action(Refresh));

    private static string StatusText(string status) => status switch
    {
        "ok" => "correcto",
        "warning" => "con advertencias",
        "error" => "con errores",
        _ => "informativo"
    };

    private void Refresh()
    {
        var results = App.Reports.All();
        var previous = _checks.ToDictionary(pair => pair.Key, pair => pair.Value.IsChecked == true);

        ResultsList.Children.Clear();
        _checks.Clear();
        EmptyText.Visibility = results.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        foreach (var result in results)
        {
            var status = ReportBuilder.StatusOf(result.Sections);
            var check = new CheckBox
            {
                IsChecked = previous.GetValueOrDefault(result.Key, true),
                Margin = new Thickness(0, 4, 0, 4),
                Content = $"{result.Title} — {result.Time:HH:mm:ss} — {StatusText(status)}",
                Foreground = Res(status switch
                {
                    "error" => "ErrorBrush",
                    "warning" => "WarningBrush",
                    "ok" => "SuccessBrush",
                    _ => "TextPrimaryBrush"
                })
            };
            _checks[result.Key] = check;
            ResultsList.Children.Add(check);
        }

        var missing = MissingTitles(results);
        NotRunText.Text = missing.Count == 0 ? "" : "Sin ejecutar en esta sesión: " + string.Join(", ", missing);
        RefreshRecent();
    }

    private static List<string> MissingTitles(IReadOnlyList<ModuleResult> results) =>
        ReportModules.Keys
            .Where(key => results.All(r => r.Key != key))
            .Select(key => ModuleCatalog.Find(key)?.Title ?? key)
            .ToList();

    private void RefreshRecent()
    {
        var recent = _files.Recent();

        RecentList.Children.Clear();
        NoRecentText.Visibility = recent.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        foreach (var file in recent)
        {
            var path = file.Path;
            var button = new Button
            {
                Style = (Style)FindResource("SecondaryButtonStyle"),
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 0, 0, 8),
                Content = $"{file.Name}  ·  {file.Modified:dd/MM/yyyy HH:mm}  ·  {Math.Max(1, file.Size / 1024)} KB"
            };
            button.Click += (_, _) =>
            {
                var error = _files.Open(path);
                if (error is not null) Show($"No se pudo abrir el informe: {error}", ok: false);
            };
            RecentList.Children.Add(button);
        }
    }

    private void Generate_Click(object sender, RoutedEventArgs e)
    {
        var all = App.Reports.All();
        var selected = all.Where(r => _checks.TryGetValue(r.Key, out var check) && check.IsChecked == true).ToList();

        var formats = FormatBoth.IsChecked == true ? new[] { ReportFormat.Txt, ReportFormat.Json }
            : FormatHtml.IsChecked == true ? new[] { ReportFormat.Html }
            : FormatJson.IsChecked == true ? new[] { ReportFormat.Json }
            : new[] { ReportFormat.Txt };

        var outcome = _files.Generate(selected, MissingTitles(all), formats, RedactMacCheck.IsChecked == true);
        Show(outcome.Ok ? $"{outcome.Message}\n" + string.Join("\n", outcome.Paths) : outcome.Message, outcome.Ok);
        if (outcome.Ok && formats.Contains(ReportFormat.Html) && outcome.Paths.FirstOrDefault() is { } visualReport)
        {
            var error = _files.Open(visualReport);
            if (error is not null)
                Show($"El informe se generó, pero no se pudo abrir: {error}\n{visualReport}", ok: false);
        }
        _setStatus(outcome.Message);
        RefreshRecent();
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        var error = _files.OpenFolder();
        if (error is not null) Show($"No se pudo abrir la carpeta: {error}", ok: false);
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => _cts?.Cancel();

    private async void RunLocal_Click(object sender, RoutedEventArgs e)
    {
        _cts?.Cancel();
        var cts = new CancellationTokenSource();
        _cts = cts;

        RunLocalButton.IsEnabled = false;
        CancelButton.Visibility = Visibility.Visible;
        LoadingBar.Visibility = Visibility.Visible;
        ErrorText.Visibility = Visibility.Collapsed;

        var jobs = new (string Key, Func<IInfoProvider> Create)[]
        {
            ("system", () => new SystemInfoProvider()),
            ("hardware", () => new HardwareInfoProvider()),
            ("battery", () => new BatteryInfoProvider()),
            ("storage", () => new StorageInfoProvider()),
            ("network", () => new NetworkInfoProvider()),
            ("events", () => new RecentEventsProvider(new WindowsEventLogReader(),
                new EventQueryOptions(TimeSpan.FromHours(24), true)))
        };

        try
        {
            for (var i = 0; i < jobs.Length; i++)
            {
                cts.Token.ThrowIfCancellationRequested();
                var title = ModuleCatalog.Find(jobs[i].Key)?.Title ?? jobs[i].Key;
                _setStatus($"Consultando {title} ({i + 1}/{jobs.Length})...");

                var sections = await jobs[i].Create().CollectAsync(cts.Token);
                App.RecordResult(jobs[i].Key, sections);
            }

            _setStatus("Diagnósticos locales completados.");
        }
        catch (OperationCanceledException)
        {
            _setStatus("Consulta cancelada.");
        }
        catch (Exception ex)
        {
            _log.Error("Error inesperado al ejecutar los diagnósticos locales.", ex);
            ErrorText.Text = $"No se pudieron completar los diagnósticos: {ex.Message}";
            ErrorText.Visibility = Visibility.Visible;
            _setStatus("Los diagnósticos fallaron.");
        }
        finally
        {
            if (ReferenceEquals(_cts, cts))
            {
                RunLocalButton.IsEnabled = true;
                CancelButton.Visibility = Visibility.Collapsed;
                LoadingBar.Visibility = Visibility.Collapsed;
            }
        }
    }

    private void Show(string text, bool ok)
    {
        MessageText.Text = text;
        MessageText.Foreground = Res(ok ? "SuccessBrush" : "ErrorBrush");
        MessageText.Visibility = Visibility.Visible;
    }
}