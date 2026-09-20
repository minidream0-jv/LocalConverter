using System.Text;

namespace LocalConverter.Core;

public sealed class FileLogger
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _logDirectory;

    public FileLogger()
    {
        _logDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LocalConverter",
            "Logs");
    }

    public async Task LogAsync(string message, Exception? error = null)
    {
        try
        {
            Directory.CreateDirectory(_logDirectory);
            var path = Path.Combine(_logDirectory, $"converter-{DateTime.UtcNow:yyyyMMdd}.log");
            var builder = new StringBuilder()
                .Append(DateTimeOffset.Now.ToString("O"))
                .Append(' ')
                .AppendLine(message);
            if (error is not null)
            {
                builder.AppendLine(error.ToString());
            }

            await _gate.WaitAsync();
            try
            {
                await File.AppendAllTextAsync(path, builder.ToString(), Encoding.UTF8);
            }
            finally
            {
                _gate.Release();
            }
        }
        catch
        {
            // Logging must never make conversion fail.
        }
    }
}
