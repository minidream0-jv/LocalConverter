using System.IO;
using System.Windows;
using LocalConverter.Core;
using LocalConverter.FFmpeg;
using LocalConverter.Images;

namespace LocalConverter.App;

public partial class App : System.Windows.Application
{
    private readonly FileLogger _logger = new();

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var jobPath = GetValue(e.Args, "--job");
        if (string.IsNullOrWhiteSpace(jobPath))
        {
            await NotificationService.ShowAsync("Local Converter", "Задание конвертации не передано.", true);
            Shutdown(2);
            return;
        }

        try
        {
            var job = await ConversionJob.LoadAsync(jobPath);
            if (e.Args.Contains("--custom", StringComparer.OrdinalIgnoreCase) ||
                job.TargetFormat.Equals("custom", StringComparison.OrdinalIgnoreCase))
            {
                var window = new ConversionWindow(job, CreateQueue(), _logger);
                MainWindow = window;
                ShutdownMode = ShutdownMode.OnMainWindowClose;
                window.Show();
                return;
            }

            await RunQuickConversionAsync(job);
            Shutdown();
        }
        catch (Exception exception)
        {
            await _logger.LogAsync("Не удалось обработать задание конвертации.", exception);
            await NotificationService.ShowAsync("Local Converter", exception.Message, true);
            Shutdown(1);
        }
        finally
        {
            TryDeleteJob(jobPath);
        }
    }

    private async Task RunQuickConversionAsync(ConversionJob job)
    {
        var options = new ConversionOptions { TargetFormat = job.TargetFormat, Quality = 85 };
        var results = await CreateQueue().RunAsync(job.Files, options);
        foreach (var failed in results.Where(result => !result.Success))
        {
            await _logger.LogAsync($"Ошибка конвертации: {failed.SourcePath}", failed.Error);
        }

        var successCount = results.Count(result => result.Success);
        var failedResult = results.FirstOrDefault(result => !result.Success);
        if (failedResult is not null)
        {
            await NotificationService.ShowAsync(
                "Local Converter",
                $"Не удалось конвертировать {Path.GetFileName(failedResult.SourcePath)}",
                true);
        }
        else
        {
            var message = successCount == 1
                ? "Конвертация завершена"
                : $"Конвертировано файлов: {successCount}";
            await NotificationService.ShowAsync("Local Converter", message);
        }
    }

    private static ConversionQueue CreateQueue() =>
        new IConversionBackend[] { new LocalConverter.Images.ImageConverter(), new FfmpegConverter() }
            .Pipe(backends => new ConversionQueue(backends));

    private static string? GetValue(IReadOnlyList<string> args, string key)
    {
        for (var index = 0; index < args.Count - 1; index++)
        {
            if (args[index].Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                return args[index + 1];
            }
        }

        return null;
    }

    private static void TryDeleteJob(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch
        {
            // Temporary jobs are also cleaned up by Windows eventually.
        }
    }
}

internal static class FunctionalExtensions
{
    public static TResult Pipe<T, TResult>(this T value, Func<T, TResult> function) => function(value);
}
