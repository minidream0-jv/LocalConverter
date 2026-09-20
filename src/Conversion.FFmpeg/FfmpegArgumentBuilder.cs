using LocalConverter.Core;

namespace LocalConverter.FFmpeg;

public static class FfmpegArgumentBuilder
{
    public static IReadOnlyList<string> Build(
        string input,
        string output,
        MediaKind sourceKind,
        ConversionOptions options)
    {
        var target = FormatCatalog.NormalizeExtension(options.TargetFormat);
        var arguments = new List<string>
        {
            "-hide_banner", "-nostdin", "-loglevel", "error", "-y", "-i", input
        };

        if (sourceKind == MediaKind.Video && target == "mp3")
        {
            arguments.AddRange(["-vn", "-c:a", "libmp3lame", "-b:a", options.AudioBitrate ?? "192k"]);
        }
        else if (sourceKind == MediaKind.Video)
        {
            AddVideoArguments(arguments, target, options);
        }
        else
        {
            AddAudioArguments(arguments, target, options);
        }

        arguments.Add(output);
        return arguments;
    }

    private static void AddVideoArguments(List<string> arguments, string target, ConversionOptions options)
    {
        if (options.Width is > 0 || options.Height is > 0)
        {
            var width = options.Width?.ToString() ?? "-2";
            var height = options.Height?.ToString() ?? "-2";
            arguments.AddRange(["-vf", $"scale={width}:{height}"]);
        }

        if (options.FramesPerSecond is > 0)
        {
            arguments.AddRange(["-r", options.FramesPerSecond.Value.ToString()]);
        }

        var videoCodec = options.VideoCodec;
        var audioCodec = options.AudioCodec;

        switch (target)
        {
            case "webm":
                arguments.AddRange(["-c:v", videoCodec ?? "libvpx-vp9", "-crf", QualityToVp9Crf(options.Quality), "-b:v", options.VideoBitrate ?? "0"]);
                arguments.AddRange(["-c:a", audioCodec ?? "libopus", "-b:a", options.AudioBitrate ?? "160k"]);
                break;
            case "mp4":
                arguments.AddRange(["-c:v", videoCodec ?? "libx264", "-preset", "medium", "-crf", QualityToCrf(options.Quality)]);
                arguments.AddRange(["-c:a", audioCodec ?? "aac", "-b:a", options.AudioBitrate ?? "192k", "-movflags", "+faststart"]);
                break;
            case "mov":
                arguments.AddRange(["-c:v", videoCodec ?? "libx264", "-preset", "medium", "-crf", QualityToCrf(options.Quality)]);
                arguments.AddRange(["-c:a", audioCodec ?? "aac", "-b:a", options.AudioBitrate ?? "192k"]);
                break;
            case "mkv":
                arguments.AddRange(["-c:v", videoCodec ?? "libx264", "-preset", "medium", "-crf", QualityToCrf(options.Quality)]);
                arguments.AddRange(["-c:a", audioCodec ?? "aac", "-b:a", options.AudioBitrate ?? "192k"]);
                break;
            default:
                throw new NotSupportedException($"Неподдерживаемый формат видео: {target}");
        }
    }

    private static void AddAudioArguments(List<string> arguments, string target, ConversionOptions options)
    {
        arguments.Add("-vn");
        var codec = target switch
        {
            "mp3" => options.AudioCodec ?? "libmp3lame",
            "wav" => options.AudioCodec ?? "pcm_s16le",
            "flac" => options.AudioCodec ?? "flac",
            "aac" => options.AudioCodec ?? "aac",
            "ogg" => options.AudioCodec ?? "libvorbis",
            _ => throw new NotSupportedException($"Неподдерживаемый формат аудио: {target}")
        };
        arguments.AddRange(["-c:a", codec]);

        if (target is not ("wav" or "flac"))
        {
            arguments.AddRange(["-b:a", options.AudioBitrate ?? (target == "aac" ? "192k" : "192k")]);
        }

        if (options.SampleRate is > 0)
        {
            arguments.AddRange(["-ar", options.SampleRate.Value.ToString()]);
        }

        if (options.Channels is > 0)
        {
            arguments.AddRange(["-ac", options.Channels.Value.ToString()]);
        }
    }

    private static string QualityToCrf(int quality) =>
        Math.Clamp(40 - (int)Math.Round(Math.Clamp(quality, 1, 100) * 0.3), 10, 40).ToString();

    private static string QualityToVp9Crf(int quality) =>
        Math.Clamp(55 - (int)Math.Round(Math.Clamp(quality, 1, 100) * 0.4), 15, 50).ToString();
}

