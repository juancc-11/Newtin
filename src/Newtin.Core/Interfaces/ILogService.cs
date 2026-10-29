namespace Newtin.Core.Interfaces;

public interface ILogService
{
    void Info(string message);
    void Warning(string message);
    void Error(string message, Exception? exception = null);
}