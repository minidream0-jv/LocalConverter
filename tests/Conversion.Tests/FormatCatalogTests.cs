using LocalConverter.Core;

namespace LocalConverter.Tests;

public sealed class FormatCatalogTests
{
    [Theory]
    [InlineData("photo.JPEG", MediaKind.Image, "jpg")]
    [InlineData("movie.MKV", MediaKind.Video, "mkv")]
    [InlineData("sound.M4A", MediaKind.Audio, "m4a")]
    [InlineData("voice.OPUS", MediaKind.Audio, "opus")]
    public void Detect_RecognizesSupportedExtensions(string path, MediaKind kind, string extension)
    {
        var result = FormatCatalog.Detect(path);
        Assert.Equal(kind, result.Kind);
        Assert.Equal(extension, result.Extension);
    }

    [Fact]
    public void GetTargets_DoesNotMixMediaCategories()
    {
        Assert.DoesNotContain("mp4", FormatCatalog.GetTargets(MediaKind.Image));
        Assert.DoesNotContain("png", FormatCatalog.GetTargets(MediaKind.Audio));
        Assert.Contains("mp3", FormatCatalog.GetTargets(MediaKind.Video));
    }
}

