using System.Diagnostics;
using LocalConverter.Core;

namespace LocalConverter.FFmpeg;

public sealed class FfmpegConverter : IConversionBackend
{
    private readonly string _ffmpegPath;

    public FfmpegConverter(string? ffmpegPath = null)
    {
        _ffmpegPath = ffmpegPath ?? Path.Combine(AppContext.BaseDirectory, "tools", "ffmpeg.exe");
    }

    public bool CanConvert(MediaKind sourceKind) => sourceKind is MediaKind.Video or MediaKind.Audio;

    public async Task<string> ConvertAsync(
        string sourcePath,
        ConversionOptions options,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_ffmpegPath))
        {
            throw new FileNotFoundException("FFmpeg не найден в составе приложения.", _ffmpegPath);
        }

        var sourceKind = FormatCatalog.Detect(sourcePath).Kind;
        using var output = OutputReservation.Create(sourcePath, options.TargetFormat, options.OutputDirectory);
        var startInfo = new ProcessStartInfo
        {
            FileName = _ffmpegPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };

        foreach (var argument in FfmpegArgumentBuilder.Build(sourcePath, output.Path, sourceKind, options))
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        process.Start();
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);

        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw;
        }

        var standardError = await errorTask;
        _ = await outputTask;
        if (process.ExitCode != 0)
        {
            var details = standardError.Length > 8_000 ? standardError[^8_000..] : standardError;
            throw new InvalidOperationException($"FFmpeg завершился с кодом {process.ExitCode}: {details.Trim()}");
        }

        output.Commit();
        return output.Path;
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(true);
            }
        }
        catch
        {
            // Process may have exited between the check and Kill.
        }
    }
}
