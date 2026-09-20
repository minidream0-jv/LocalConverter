using LocalConverter.Core;
using LocalConverter.FFmpeg;

namespace LocalConverter.Tests;

public sealed class FfmpegArgumentBuilderTests
{
    [Fact]
    public void Build_VideoToMp3_DisablesVideoAndUsesGoodDefaultBitrate()
    {
        var args = FfmpegArgumentBuilder.Build(
            @"C:\Видео\мой файл.mp4",
            @"C:\Видео\мой файл.mp3",
            MediaKind.Video,
            new ConversionOptions { TargetFormat = "mp3" });

        Assert.Contains("-vn", args);
        Assert.Contains("libmp3lame", args);
        Assert.Contains("192k", args);
        Assert.Equal(@"C:\Видео\мой файл.mp4", args[6]);
    }

    [Fact]
    public void Build_Webm_UsesVp9AndOpus()
    {
        var args = FfmpegArgumentBuilder.Build(
            "input.mov",
            "output.webm",
            MediaKind.Video,
            new ConversionOptions { TargetFormat = "webm", Quality = 85 });

        Assert.Contains("libvpx-vp9", args);
        Assert.Contains("libopus", args);
    }
}

