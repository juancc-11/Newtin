using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using Newtin.Core.Interfaces;
using Newtin.Core.Models;
using Newtin.Desktop.Controls;
using Newtin.Diagnostics.Connectivity;
using Newtin.Diagnostics.Permissions;
using Newtin.Diagnostics.SharedAccess;

namespace Newtin.Desktop.Views;

public partial class SharedAccessView : UserControl
{
    private readonly ISettingsService _settings;
    private readonly ILogService _log;
    private readonly Action<string> _setStatus;
    private readonly SharedFolderService _service;
    private readonly PermissionTester _permissionTester;
    private readonly HashSet<Task> _transferTasks = [];
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private CancellationTokenSource? _cts;
    private ShareConnection? _connection;
    private string? _rootPath;
    private string? _currentPath;
    private bool _uploadQueueRunning;

    public ObservableCollection<TransferItem> Transfers { get; } = [];

    public SharedAccessView(ISettingsService settings, ILogService log, Action<string> setStatus)
    {
        InitializeComponent();
        _settings = settings;
        _log = log;
        _setStatus = setStatus;
        _service = new SharedFolderService(new TcpPortProbe(), new WindowsSmbNative(), log);
        _permissionTester = new PermissionTester(log);
        DataContext = this;

        Form.Load(settings.Current);
        Unloaded += async (_, _) =>
        {
            var pendingUploads = Transfers.Where(transfer => transfer.IsPendingUpload).ToArray();
            _cts?.Cancel();
            _lifetimeCancellation.Cancel();
            CancelTransfers();
            if (_transferTasks.Count > 0)
                await Task.WhenAll(_transferTasks.ToArray());
            foreach (var transfer in pendingUploads)
                transfer.Dispose();
            Disconnect();
            _lifetimeCancellation.Dispose();
        };
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => _cts?.Cancel();

    private async void Connect_Click(object sender, RoutedEventArgs e)
    {
        if (_connection is not null)
        {
            Disconnect();
            _setStatus("Conexión cerrada.");
            return;
        }

        FormError.Visibility = Visibility.Collapsed;
        if (!Form.TryGetRequest(out var request, out var error))
        {
            ShowFormError(error);
            return;
        }

        var cts = new CancellationTokenSource();
        _cts = cts;
        SetBusy(true);
        ResultsPanel.Children.Clear();
        ConnectionStatus.Text = "Conectando...";
        ConnectionStatus.Foreground = (System.Windows.Media.Brush)FindResource("WarningBrush");
        _setStatus("Conectando al equipo remoto...");

        try
        {
            var timeout = TimeSpan.FromSeconds(_settings.Current.Network.ConnectionTimeoutSeconds);
            var connection = await _service.ConnectAsync(request, timeout, cts.Token);
            var section = ShareFormatter.ToSection(connection.Access, request.Mode);
            ResultsPanel.Children.Add(InfoSectionRenderer.Build(section));

            if (connection.Access.Status is not (ShareAccessStatus.Cancelled or ShareAccessStatus.InvalidInput))
                App.RecordResult("shares", [section]);

            if (!connection.Access.IsAccessible)
            {
                connection.Dispose();
                ConnectionStatus.Text = $"Desconectado · {connection.Access.Message}";
                ConnectionStatus.Foreground = (System.Windows.Media.Brush)FindResource("ErrorBrush");
                _setStatus("No se pudo conectar al equipo remoto.");
                return;
            }

            _connection = connection;
            _rootPath = connection.UncPath;
            _currentPath = _rootPath;
            BrowserPanel.Visibility = Visibility.Visible;
            SetConnectButtonState(connected: true);
            ConnectionStatus.Text = $"Conectado · {_rootPath}";
            ConnectionStatus.Foreground = (System.Windows.Media.Brush)FindResource("SuccessBrush");
            try
            {
                await LoadDirectoryAsync(cts.Token);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                ShowBrowserError($"No se pudo explorar la carpeta: {ex.Message}");
                _log.Warning($"No se pudo explorar {_currentPath}: {ex.Message}");
            }
            _setStatus("Conexión establecida.");
        }
        catch (OperationCanceledException)
        {
            ConnectionStatus.Text = "Conexión cancelada.";
            ConnectionStatus.Foreground = (System.Windows.Media.Brush)FindResource("WarningBrush");
            _setStatus("Conexión cancelada.");
        }
        catch (Exception ex)
        {
            _log.Error("Error inesperado al conectar con la carpeta compartida.", ex);
            ShowFormError($"Ocurrió un error inesperado: {ex.Message}");
            ConnectionStatus.Text = "Error de conexión.";
            ConnectionStatus.Foreground = (System.Windows.Media.Brush)FindResource("ErrorBrush");
            _setStatus("La conexión falló.");
        }
        finally
        {
            request.Dispose();
            Form.ClearPassword();
            if (ReferenceEquals(_cts, cts))
            {
                SetBusy(false);
                _cts = null;
            }
            cts.Dispose();
        }
    }

    private void Disconnect()
    {
        if (_transferTasks.Count > 0 || Transfers.Any(transfer => transfer.IsPendingUpload))
        {
            MessageBox.Show(Window.GetWindow(this),
                "Inicia o cancela los archivos pendientes y espera a que terminen las transferencias antes de desconectar.",
                "Transferencias en curso", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        _connection?.Dispose();
        _connection = null;
        _rootPath = null;
        _currentPath = null;
        RemoteItems.ItemsSource = null;
        BrowserPanel.Visibility = Visibility.Collapsed;
        SetConnectButtonState(connected: false);
        ConnectionStatus.Text = "Desconectado";
        ConnectionStatus.Foreground = (System.Windows.Media.Brush)FindResource("TextSecondaryBrush");
        DownloadButton.IsEnabled = false;
        RenameButton.IsEnabled = false;
        DeleteButton.IsEnabled = false;
        ParentButton.IsEnabled = false;
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        if (_connection is null) return;
        try
        {
            await LoadDirectoryAsync(CancellationToken.None);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowBrowserError($"No se pudo actualizar la carpeta: {ex.Message}");
            _log.Warning($"No se pudo actualizar {_currentPath}: {ex.Message}");
        }
    }

    private async void Parent_Click(object sender, RoutedEventArgs e)
    {
        if (_rootPath is null || _currentPath is null || IsAtRoot()) return;

        var parent = Directory.GetParent(_currentPath.TrimEnd(Path.DirectorySeparatorChar));
        if (parent is null || !IsWithinRoot(parent.FullName)) return;
        _currentPath = parent.FullName;
        await LoadDirectorySafelyAsync();
    }

    private async void RemoteItems_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (RemoteItems.SelectedItem is not RemoteEntry { IsDirectory: true } item) return;
        _currentPath = item.FullPath;
        await LoadDirectorySafelyAsync();
    }

    private void RemoteItems_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var selectedFile = RemoteItems.SelectedItem is RemoteEntry { IsDirectory: false };
        var selectedEntry = RemoteItems.SelectedItem is RemoteEntry;
        DownloadButton.IsEnabled = selectedFile;
        RenameButton.IsEnabled = selectedEntry;
        DeleteButton.IsEnabled = selectedEntry;
        if (RemoteItems.SelectedItem is RemoteEntry item)
            RenameNameBox.Text = item.Name;
    }

    private async Task LoadDirectorySafelyAsync()
    {
        try
        {
            await LoadDirectoryAsync(CancellationToken.None);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowBrowserError($"No se pudo abrir la carpeta: {ex.Message}");
            _log.Warning($"No se pudo explorar {_currentPath}: {ex.Message}");
        }
    }

    private async Task LoadDirectoryAsync(CancellationToken cancellationToken)
    {
        if (_currentPath is null || _rootPath is null) return;
        var path = _currentPath;
        var entries = await Task.Run(() =>
        {
            if (IsServerRoot(path))
            {
                return WindowsRemoteShareEnumerator.ListShares(path)
                    .Select(name => new RemoteEntry(name, "Carpeta compartida",
                        Path.Combine(path, name), true))
                    .OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
                    .ToList();
            }

            var result = new List<RemoteEntry>();
            foreach (var fullPath in Directory.EnumerateFileSystemEntries(path))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var attributes = File.GetAttributes(fullPath);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    continue;
                var isDirectory = (attributes & FileAttributes.Directory) != 0;
                result.Add(new RemoteEntry(
                    Path.GetFileName(fullPath.TrimEnd(Path.DirectorySeparatorChar)),
                    isDirectory ? "Carpeta" : "Archivo",
                    fullPath,
                    isDirectory));
            }

            return result.OrderByDescending(item => item.IsDirectory)
                .ThenBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }, cancellationToken);

        CurrentPathBox.Text = path;
        RemoteItems.ItemsSource = entries;
        ParentButton.IsEnabled = !IsAtRoot();
        BrowserError.Visibility = Visibility.Collapsed;
    }

    private void Upload_Click(object sender, RoutedEventArgs e)
    {
        if (_connection is null || _currentPath is null || _lifetimeCancellation.IsCancellationRequested) return;
        var dialog = new OpenFileDialog { Multiselect = true, CheckFileExists = true };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;

        foreach (var source in dialog.FileNames)
        {
            var fileName = Path.GetFileName(source);
            AddTransfer(fileName, "Subiendo", source, Path.Combine(_currentPath, fileName), selected: true);
        }

        UpdateStartTransfersButton();
        BrowserError.Visibility = Visibility.Collapsed;
        _setStatus($"{dialog.FileNames.Length} archivo(s) listos para transferir. Selecciona cuáles incluir e inicia la transferencia.");
    }

    private void TransferSelection_Changed(object sender, RoutedEventArgs e) =>
        UpdateStartTransfersButton();

    private void TransfersGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        ClearTransfersButton.IsEnabled = TransfersGrid.SelectedItems.Count > 0;

    private void ClearTransfers_Click(object sender, RoutedEventArgs e)
    {
        var selectedTransfers = TransfersGrid.SelectedItems.OfType<TransferItem>().ToArray();
        if (selectedTransfers.Length == 0) return;

        var removed = 0;
        var active = 0;
        foreach (var transfer in selectedTransfers)
        {
            if (transfer.IsInProgress)
            {
                active++;
                continue;
            }

            if (transfer.IsPendingUpload)
                transfer.Dispose();
            Transfers.Remove(transfer);
            removed++;
        }

        if (Transfers.Count == 0)
            TransfersPanel.Visibility = Visibility.Collapsed;
        ClearTransfersButton.IsEnabled = TransfersGrid.SelectedItems.Count > 0;
        _setStatus(active > 0
            ? $"{removed} archivo(s) quitado(s) de la tabla. {active} transferencia(s) activa(s) se conservaron."
            : $"{removed} archivo(s) quitado(s) de la tabla.");
    }

    private async void StartTransfers_Click(object sender, RoutedEventArgs e)
    {
        if (_uploadQueueRunning) return;
        var queue = Transfers
            .Where(transfer => transfer.IsPendingUpload && transfer.IsSelected)
            .ToList();
        if (queue.Count == 0)
        {
            ShowBrowserError("Selecciona al menos un archivo pendiente para iniciar la transferencia.");
            return;
        }

        _uploadQueueRunning = true;
        foreach (var transfer in queue)
            transfer.IsCancelable = false;
        UpdateStartTransfersButton();
        try
        {
            var remoteEntries = await Task.Run(() => queue
                .Select(transfer => Path.GetDirectoryName(transfer.DestinationPath)!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    folder => folder,
                    folder => Directory.EnumerateFileSystemEntries(folder)
                        .Select(path => (
                            Name: Path.GetFileName(path),
                            IsDirectory: (File.GetAttributes(path) & FileAttributes.Directory) != 0))
                        .ToArray(),
                    StringComparer.OrdinalIgnoreCase));
            var directoryConflicts = queue.Where(transfer =>
                {
                    var folder = Path.GetDirectoryName(transfer.DestinationPath)!;
                    return remoteEntries[folder].Any(entry => entry.IsDirectory &&
                        string.Equals(entry.Name, transfer.FileName, StringComparison.OrdinalIgnoreCase));
                })
                .ToList();
            foreach (var transfer in directoryConflicts)
                MarkTransferSkipped(transfer, "Ya existe una carpeta con este nombre en el destino.");

            var skippedFiles = directoryConflicts.Count;
            queue = queue.Except(directoryConflicts).ToList();
            if (queue.Count == 0)
            {
                _setStatus("No se transfirieron los archivos porque ya existen carpetas con esos nombres.");
                return;
            }

            var existingFiles = queue.Where(transfer =>
                {
                    var folder = Path.GetDirectoryName(transfer.DestinationPath)!;
                    return remoteEntries[folder].Any(entry => !entry.IsDirectory &&
                        string.Equals(entry.Name, transfer.FileName, StringComparison.OrdinalIgnoreCase));
                })
                .ToList();
            var overwriteExisting = false;
            if (existingFiles.Count > 0)
            {
                var fileNames = string.Join(Environment.NewLine,
                    existingFiles.Take(5).Select(transfer => transfer.FileName));
                var moreFiles = existingFiles.Count > 5
                    ? $"{Environment.NewLine}y {existingFiles.Count - 5} archivo(s) más"
                    : "";
                var result = MessageBox.Show(Window.GetWindow(this),
                    existingFiles.Count == 1
                        ? $"El archivo \"{existingFiles[0].FileName}\" ya existe en la carpeta de destino.{Environment.NewLine}{Environment.NewLine}" +
                          "¿Quieres sobrescribirlo?"
                        : $"Estos archivos ya existen en la carpeta de destino:{Environment.NewLine}{Environment.NewLine}" +
                          $"{fileNames}{moreFiles}{Environment.NewLine}{Environment.NewLine}" +
                          "¿Quieres sobrescribirlos todos?",
                    "Confirmar sobrescritura", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
                if (result == MessageBoxResult.Cancel)
                    return;
                overwriteExisting = result == MessageBoxResult.Yes;
                if (!overwriteExisting)
                {
                    foreach (var transfer in existingFiles)
                        MarkTransferSkipped(transfer, "Omitido: ya existe un archivo con este nombre en el destino.");
                    skippedFiles += existingFiles.Count;
                    queue = queue.Except(existingFiles).ToList();
                }
            }

            if (queue.Count == 0)
            {
                _setStatus("No había archivos nuevos para transferir.");
                return;
            }

            var uploadTask = RunUploadQueueAsync(queue, overwriteExisting, skippedFiles > 0);
            _transferTasks.Add(uploadTask);
            try
            {
                await uploadTask;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                ShowBrowserError($"No se pudieron transferir los archivos: {ex.Message}");
                _log.Error("Error al subir archivos a la carpeta compartida.", ex);
                _setStatus("La transferencia falló.");
            }
            finally
            {
                _transferTasks.Remove(uploadTask);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowBrowserError($"No se pudo revisar la carpeta de destino: {ex.Message}");
            _log.Error("No se pudo revisar los archivos existentes antes de transferir.", ex);
            _setStatus("No se pudo preparar la transferencia.");
        }
        finally
        {
            _uploadQueueRunning = false;
            foreach (var transfer in Transfers.Where(transfer => transfer.IsPendingUpload && transfer.IsSelected))
                transfer.IsCancelable = true;
            UpdateStartTransfersButton();
        }
    }

    private async void Download_Click(object sender, RoutedEventArgs e)
    {
        if (_connection is null || _lifetimeCancellation.IsCancellationRequested ||
            RemoteItems.SelectedItem is not RemoteEntry { IsDirectory: false } item) return;

        var dialog = new SaveFileDialog
        {
            FileName = item.Name,
            OverwritePrompt = true,
            AddExtension = false
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;

        try
        {
            var transfer = AddTransfer(item.Name, "Descargando");
            transfer.DestinationPath = dialog.FileName;
            await RunTransferAsync(transfer, item.FullPath, dialog.FileName, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowBrowserError($"No se pudo descargar el archivo: {ex.Message}");
            _log.Error($"Error al descargar {item.FullPath}.", ex);
            _setStatus("La descarga falló.");
        }
    }

    private void CancelTransfer_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: TransferItem transfer })
        {
            if (transfer.IsPendingUpload)
            {
                transfer.StatusText = "Cancelada";
                transfer.IsSelected = false;
                transfer.IsCancelable = false;
                transfer.Dispose();
                UpdateStartTransfersButton();
            }
            else
            {
                transfer.Cancel();
            }
        }
    }

    private async void CreateFolder_Click(object sender, RoutedEventArgs e)
    {
        if (_connection is null || _currentPath is null) return;
        var name = NewFolderNameBox.Text.Trim();
        if (!IsValidLeafName(name))
        {
            ShowBrowserError("Escribe un nombre válido para la carpeta, sin rutas ni caracteres no permitidos.");
            return;
        }

        var path = Path.Combine(_currentPath, name);
        try
        {
            if (Directory.Exists(path) || File.Exists(path))
            {
                ShowBrowserError("Ya existe un archivo o carpeta con ese nombre.");
                return;
            }

            await Task.Run(() => Directory.CreateDirectory(path));
            NewFolderNameBox.Clear();
            await LoadDirectoryAsync(CancellationToken.None);
            _setStatus("Carpeta creada.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowBrowserError($"No se pudo crear la carpeta: {ex.Message}");
            _log.Error($"Error al crear la carpeta compartida {path}.", ex);
        }
    }

    private async void Rename_Click(object sender, RoutedEventArgs e)
    {
        if (_connection is null || RemoteItems.SelectedItem is not RemoteEntry item)
            return;
        var name = RenameNameBox.Text.Trim();
        if (!IsValidLeafName(name))
        {
            ShowBrowserError("Escribe un nombre válido para el archivo o carpeta, sin rutas ni caracteres no permitidos.");
            return;
        }

        var itemExists = item.IsDirectory ? Directory.Exists(item.FullPath) : File.Exists(item.FullPath);
        if (!IsWithinRoot(item.FullPath) || !itemExists || IsRootShareEntry(item))
        {
            ShowBrowserError("El elemento seleccionado no está disponible o no se puede renombrar dentro de la carpeta autorizada.");
            return;
        }

        var destination = Path.Combine(Path.GetDirectoryName(item.FullPath)!, name);
        if (string.Equals(item.FullPath, destination, StringComparison.OrdinalIgnoreCase))
            return;

        if (File.Exists(destination) || Directory.Exists(destination))
        {
            ShowBrowserError("Ya existe un archivo o carpeta con ese nombre.");
            return;
        }

        try
        {
            if (item.IsDirectory)
                Directory.Move(item.FullPath, destination);
            else
                File.Move(item.FullPath, destination);
            await LoadDirectoryAsync(CancellationToken.None);
            _setStatus(item.IsDirectory ? "Carpeta renombrada." : "Archivo renombrado.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowBrowserError($"No se pudo renombrar el elemento: {ex.Message}");
            _log.Error($"Error al renombrar el elemento compartido {item.FullPath}.", ex);
        }
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (_connection is null || RemoteItems.SelectedItem is not RemoteEntry item)
            return;
        var itemExists = item.IsDirectory ? Directory.Exists(item.FullPath) : File.Exists(item.FullPath);
        if (!IsWithinRoot(item.FullPath) || !itemExists || IsRootShareEntry(item))
        {
            ShowBrowserError("El elemento seleccionado no está disponible o no se puede eliminar dentro de la carpeta autorizada.");
            return;
        }

        if (MessageBox.Show(Window.GetWindow(this),
                item.IsDirectory
                    ? $"¿Eliminar esta carpeta y todo su contenido de forma permanente?\n\n{item.FullPath}"
                    : $"¿Eliminar este archivo de la carpeta compartida?\n\n{item.FullPath}",
                "Confirmar eliminación", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        try
        {
            if (item.IsDirectory)
                Directory.Delete(item.FullPath, recursive: true);
            else
                File.Delete(item.FullPath);
            await LoadDirectoryAsync(CancellationToken.None);
            _setStatus(item.IsDirectory ? "Carpeta y su contenido eliminados." : "Archivo eliminado.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowBrowserError($"No se pudo eliminar el elemento: {ex.Message}");
            _log.Error($"Error al eliminar el elemento compartido {item.FullPath}.", ex);
        }
    }

    private async void Permissions_Click(object sender, RoutedEventArgs e)
    {
        if (_connection is null || _currentPath is null) return;

        var message =
            $"Newtin creará una carpeta temporal llamada NewtinTest_… dentro de:\n\n{_currentPath}\n\n" +
            "Leerá, escribirá, modificará y eliminará únicamente los elementos temporales que cree. " +
            "¿Autorizas esta prueba?";
        var owner = Window.GetWindow(this) ?? Application.Current.MainWindow;
        if (MessageBox.Show(owner, message, "Autorizar prueba de permisos",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        var cts = new CancellationTokenSource();
        _cts = cts;
        SetBusy(true);
        ResultsPanel.Children.Clear();
        _setStatus("Verificando permisos...");
        try
        {
            var report = await _permissionTester.RunAsync(_currentPath, cts.Token);
            var sections = PermissionFormatter.ToSections(report);
            foreach (var section in sections)
                ResultsPanel.Children.Add(InfoSectionRenderer.Build(section));
            App.RecordResult("permissions", sections);
            _setStatus("Verificación de permisos terminada.");
        }
        catch (OperationCanceledException)
        {
            _setStatus("Verificación de permisos cancelada.");
        }
        catch (Exception ex)
        {
            _log.Error("Error inesperado al verificar los permisos de la carpeta.", ex);
            ShowBrowserError($"No se pudieron verificar los permisos: {ex.Message}");
            _setStatus("La verificación de permisos falló.");
        }
        finally
        {
            BrowserPanel.IsEnabled = true;
            if (ReferenceEquals(_cts, cts))
            {
                SetBusy(false);
                _cts = null;
            }
            cts.Dispose();
        }
    }

    private TransferItem AddTransfer(
        string fileName,
        string direction,
        string? sourcePath = null,
        string? destinationPath = null,
        bool selected = false)
    {
        var transfer = new TransferItem(fileName, direction, sourcePath, destinationPath, selected,
            _lifetimeCancellation.Token);
        Transfers.Insert(0, transfer);
        TransfersPanel.Visibility = Visibility.Visible;
        return transfer;
    }

    private async Task RunUploadQueueAsync(
        IReadOnlyList<TransferItem> queue,
        bool overwriteExisting,
        bool skippedFiles)
    {
        foreach (var transfer in queue)
        {
            if (_lifetimeCancellation.IsCancellationRequested) break;
            if (transfer.SourcePath is not { } source || transfer.DestinationPath is not { } destination)
                continue;
            transfer.IsSelected = false;
            transfer.IsCancelable = true;
            await RunTransferCoreAsync(transfer, source, destination, overwriteExisting);
        }

        if (!_lifetimeCancellation.IsCancellationRequested)
        {
            await LoadDirectoryAsync(CancellationToken.None);
            _setStatus(queue.All(item => item.StatusText is "Completada" or "Omitida")
                ? skippedFiles ? "Transferencia terminada; algunos archivos se omitieron." : "Archivos transferidos."
                : "La cola terminó; revisa el estado de cada transferencia.");
        }
        UpdateStartTransfersButton();
    }

    private async Task RunTransferAsync(
        TransferItem transfer,
        string source,
        string destination,
        bool overwrite)
    {
        var transferTask = RunTransferCoreAsync(transfer, source, destination, overwrite);
        _transferTasks.Add(transferTask);
        try
        {
            await transferTask;
        }
        finally
        {
            _transferTasks.Remove(transferTask);
        }
    }

    private async Task RunTransferCoreAsync(
        TransferItem transfer,
        string source,
        string destination,
        bool overwrite)
    {
        var cancellationToken = transfer.CancellationToken;
        var temporaryPath = destination + $".newtin-{Guid.NewGuid():N}.tmp";
        transfer.StatusText = "En cola";
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var totalBytes = new FileInfo(source).Length;
            var stopwatch = Stopwatch.StartNew();
            transfer.StatusText = "En curso";
            transfer.UpdateProgress(0, totalBytes, TimeSpan.Zero);

            await using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read,
                             128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
            await using (var output = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                             128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                var buffer = new byte[128 * 1024];
                long copied = 0;
                int bytesRead;
                while ((bytesRead = await input.ReadAsync(buffer.AsMemory(), cancellationToken)) > 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                    copied += bytesRead;
                    transfer.UpdateProgress(copied, totalBytes, stopwatch.Elapsed);
                }

                await output.FlushAsync(cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();
            transfer.IsFinalizing = true;
            transfer.ProgressText = "Guardando en destino...";
            try
            {
                await Task.Run(() => File.Copy(temporaryPath, destination, overwrite), cancellationToken);
            }
            catch (IOException ex) when (!overwrite && IsAlreadyExistsError(ex) &&
                                         transfer.Direction == "Subiendo")
            {
                var result = MessageBox.Show(Window.GetWindow(this),
                    $"El archivo \"{transfer.FileName}\" ya existe en la carpeta de destino.{Environment.NewLine}{Environment.NewLine}" +
                    "¿Quieres sobrescribirlo?",
                    "Confirmar sobrescritura", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
                if (result == MessageBoxResult.Yes)
                    await Task.Run(() => File.Copy(temporaryPath, destination, overwrite: true), cancellationToken);
                else
                {
                    transfer.StatusText = result == MessageBoxResult.No ? "Omitida" : "Cancelada";
                    transfer.ProgressText = result == MessageBoxResult.No
                        ? "Omitida: elegiste no sobrescribir el archivo."
                        : "Transferencia cancelada.";
                    return;
                }
            }
            File.Delete(temporaryPath);
            transfer.IsFinalizing = false;
            transfer.UpdateProgress(totalBytes, totalBytes, stopwatch.Elapsed);
            transfer.StatusText = "Completada";
            transfer.SpeedText = "—";
            _setStatus($"{transfer.Direction}: {transfer.FileName} completada.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            transfer.StatusText = "Cancelada";
            transfer.SpeedText = "—";
            _setStatus($"{transfer.Direction}: {transfer.FileName} cancelada.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            transfer.StatusText = "Error";
            transfer.SpeedText = "—";
            transfer.ProgressText = ex.Message;
            _log.Error($"Error en la transferencia de {transfer.FileName}.", ex);
            _setStatus($"Falló la transferencia de {transfer.FileName}.");
        }
        finally
        {
            try
            {
                if (File.Exists(temporaryPath))
                    File.Delete(temporaryPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                transfer.StatusText = "Error al limpiar";
                transfer.ProgressText = ex.Message;
                _log.Error($"No se pudo limpiar el archivo temporal de la transferencia {transfer.FileName}.", ex);
            }
            finally
            {
                transfer.IsFinalizing = false;
                transfer.IsCancelable = false;
                transfer.Dispose();
            }
        }
    }

    private void CancelTransfers()
    {
        foreach (var transfer in Transfers)
            transfer.Cancel();
    }

    private void MarkTransferSkipped(TransferItem transfer, string reason)
    {
        transfer.StatusText = "Omitida";
        transfer.ProgressText = reason;
        transfer.IsSelected = false;
        transfer.IsCancelable = false;
        transfer.Dispose();
    }

    private static bool IsValidLeafName(string name) =>
        !string.IsNullOrWhiteSpace(name) &&
        name is not ("." or "..") &&
        Path.GetFileName(name) == name &&
        !name.EndsWith('.') &&
        !name.EndsWith(' ') &&
        name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 &&
        name.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]) < 0;

    private static bool IsAlreadyExistsError(IOException exception) =>
        (exception.HResult & 0xFFFF) is 80 or 183;

    private bool IsAtRoot() =>
        _currentPath is not null && _rootPath is not null &&
        string.Equals(_currentPath.TrimEnd(Path.DirectorySeparatorChar),
            _rootPath.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);

    private static bool IsServerRoot(string path)
    {
        var trimmedPath = path.TrimEnd('\\');
        return trimmedPath.StartsWith(@"\\", StringComparison.Ordinal) &&
               trimmedPath.IndexOf('\\', 2) < 0;
    }

    private bool IsWithinRoot(string path) =>
        _rootPath is not null &&
        (string.Equals(path.TrimEnd(Path.DirectorySeparatorChar),
             _rootPath.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase) ||
         path.StartsWith(_rootPath.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
             StringComparison.OrdinalIgnoreCase));

    private bool IsRootShareEntry(RemoteEntry item) =>
        IsServerRoot(_rootPath ?? "") &&
        string.Equals(Path.GetDirectoryName(item.FullPath)?.TrimEnd(Path.DirectorySeparatorChar),
            _rootPath?.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);

    private void SetBusy(bool busy)
    {
        LoadingBar.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        CancelButton.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        ConnectButton.IsEnabled = !busy;
        if (BrowserPanel is not null)
            BrowserPanel.IsEnabled = !busy;
    }

    private void SetConnectButtonState(bool connected)
    {
        ConnectButton.Content = new TextBlock
        {
            FontFamily = new System.Windows.Media.FontFamily("Segoe MDL2 Assets"),
            FontSize = 20,
            Text = "\uE7E8"
        };
        var description = connected ? "Desconectar del equipo remoto" : "Conectar al equipo remoto";
        ConnectButton.ToolTip = description;
        AutomationProperties.SetName(ConnectButton, description);
    }

    private void ShowFormError(string text)
    {
        FormError.Text = text;
        FormError.Visibility = Visibility.Visible;
    }

    private void ShowBrowserError(string text)
    {
        BrowserError.Text = text;
        BrowserError.Visibility = Visibility.Visible;
    }

    private void UpdateStartTransfersButton() =>
        StartTransfersButton.IsEnabled = !_uploadQueueRunning &&
            Transfers.Any(transfer => transfer.CanSelect && transfer.IsSelected);

    private sealed record RemoteEntry(string Name, string Type, string FullPath, bool IsDirectory);

    public sealed class TransferItem : INotifyPropertyChanged, IDisposable
    {
        private readonly CancellationTokenSource _cancellation;
        private double _progressPercent;
        private bool _isFinalizing;
        private string _progressText = "Pendiente";
        private string _speedText = "—";
        private string _statusText = "En cola";
        private bool _isCancelable = true;
        private bool _isSelected;
        private string? _destinationPath;

        public TransferItem(
            string fileName,
            string direction,
            string? sourcePath,
            string? destinationPath,
            bool selected,
            CancellationToken lifetimeToken)
        {
            FileName = fileName;
            Direction = direction;
            SourcePath = sourcePath;
            _destinationPath = destinationPath;
            _isSelected = selected;
            _cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetimeToken);
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        public string FileName { get; }
        public string Direction { get; }
        public string? SourcePath { get; }
        public string DestinationPath
        {
            get => _destinationPath ?? "—";
            set => Set(ref _destinationPath, value);
        }
        public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value); }
        public bool IsPendingUpload => SourcePath is not null && StatusText == "En cola";
        public bool IsInProgress => StatusText is "En curso" or "Cancelando";
        public bool CanSelect => IsPendingUpload && IsCancelable;
        public double ProgressPercent { get => _progressPercent; private set => Set(ref _progressPercent, value); }
        public bool IsFinalizing { get => _isFinalizing; set => Set(ref _isFinalizing, value); }
        public string ProgressText { get => _progressText; set => Set(ref _progressText, value); }
        public string SpeedText { get => _speedText; set => Set(ref _speedText, value); }
        public string StatusText { get => _statusText; set => Set(ref _statusText, value); }
        public bool IsCancelable
        {
            get => _isCancelable;
            set
            {
                if (_isCancelable == value) return;
                _isCancelable = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsCancelable)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanSelect)));
            }
        }
        public Visibility ProgressVisibility => StatusText == "En cola" ? Visibility.Collapsed : Visibility.Visible;
        public CancellationToken CancellationToken => _cancellation.Token;

        public void Cancel()
        {
            if (!_cancellation.IsCancellationRequested && IsCancelable)
            {
                StatusText = "Cancelando";
                _cancellation.Cancel();
            }
        }

        public void UpdateProgress(long transferred, long total, TimeSpan elapsed)
        {
            ProgressPercent = total == 0 ? 100 : Math.Min(100, transferred * 100d / total);
            ProgressText = $"{FormatBytes(transferred)} / {FormatBytes(total)} ({ProgressPercent:0}%)";
            SpeedText = elapsed.TotalSeconds <= 0
                ? "Calculando..."
                : $"{FormatBytes((long)(transferred / elapsed.TotalSeconds))}/s";
        }

        public void Dispose() => _cancellation.Dispose();

        private static string FormatBytes(long bytes)
        {
            string[] units = ["B", "KB", "MB", "GB", "TB"];
            double size = bytes;
            var unit = 0;
            while (size >= 1024 && unit < units.Length - 1)
            {
                size /= 1024;
                unit++;
            }

            return $"{size:0.##} {units[unit]}";
        }

        private void Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return;
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            if (propertyName == nameof(StatusText))
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ProgressVisibility)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsPendingUpload)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanSelect)));
            }
        }
    }
}
