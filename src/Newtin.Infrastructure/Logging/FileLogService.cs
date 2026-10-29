using Newtin.Core.Interfaces;

namespace Newtin.Infrastructure.Logging;

public sealed class FileLogService : ILogService
{
    private readonly string _folder;
    private readonly object _gate = new();

    public FileLogService(string folder) => _folder = folder;

    public void Info(string message) => Write("Information", message, null);
    public void Warning(string message) => Write("Warning", message, null);
    public void Error(string message, Exception? exception = null) => Write("Error", message, exception);

    private void Write(string level, string message, Exception? exception)
    {
        try
        {
            lock (_gate)
            {
                Directory.CreateDirectory(_folder);
                var file = Path.Combine(_folder, $"newtin-{DateTime.Now:yyyyMMdd}.log");
                var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] {message.ReplaceLineEndings(" ")}";
                if (exception is not null)
                    line += Environment.NewLine + exception;
                File.AppendAllText(file, line + Environment.NewLine);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Un fallo del registro nunca debe tumbar la aplicación.
        }
    }
}