using System.Windows;
using Newtin.Core.Interfaces;
using Newtin.Desktop.Services;
using Newtin.Infrastructure.Configuration;
using Newtin.Infrastructure.FileSystem;
using Newtin.Infrastructure.Logging;
using Newtin.Core.Models;
using Newtin.Diagnostics.Reports;

namespace Newtin.Desktop;

public partial class App : Application
{
    // Acceso simple a los servicios compartidos. Si la aplicación crece, se pasa a inyección de dependencias.
    public static ILogService Log { get; } = new FileLogService(AppPaths.Logs);
    public static ISettingsService Settings { get; private set; } = null!;
    public static ServerHostService Server { get; private set; } = null!;
    public static BoardFilesService Files { get; private set; } = null!;

    public App()
    {
        DispatcherUnhandledException += (_, e) =>
        {
            Log.Error("Error no controlado en la interfaz.", e.Exception);
            MessageBox.Show(e.Exception.ToString(), "Newtin - error",
                MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
            Shutdown(1);
        };
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        var settings = new JsonSettingsService(AppPaths.SettingsFile, Log);
        settings.Load();
        Settings = settings;
        Server = new ServerHostService(settings, Log);
        Files = new BoardFilesService(Server.Host, Log);
        if (settings.Current.WhiteboardServer.RestoreLastBoard) Files.RestoreLast();

        Log.Info("Newtin iniciado.");
        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // Se cierra el servidor y se liberan los puertos antes de salir (sin bloquear el hilo de la interfaz).
        Task.Run(() => Server.StopAsync()).Wait(TimeSpan.FromSeconds(8));
        
        Files.FlushAutosave();   // guarda el último estado antes de cerrar
        Files.Dispose();

        Log.Info("Newtin cerrado.");
        base.OnExit(e);
    }
     /// <summary>Resultados de los diagnósticos de esta sesión, para el informe.</summary>
    public static ReportSession Reports { get; } = new(ReportModules.Keys);
    public static void RecordResult(string moduleKey, IReadOnlyList<InfoSection> sections)
    {
        var title = ModuleCatalog.Find(moduleKey)?.Title ?? moduleKey;
        Reports.Record(new ModuleResult(moduleKey, title, DateTimeOffset.Now, sections));
    }
}

