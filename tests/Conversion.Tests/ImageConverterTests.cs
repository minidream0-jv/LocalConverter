using ImageMagick;
using LocalConverter.Core;
using LocalConverter.Images;

namespace LocalConverter.Tests;

public sealed class ImageConverterTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "LocalConverterTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task ConvertAsync_PngToWebp_CreatesReadableWebp()
    {
        Directory.CreateDirectory(_directory);
        var source = Path.Combine(_directory, "тестовое изображение.png");
        using (var image = new MagickImage(MagickColors.CornflowerBlue, 32, 24))
        {
            image.Write(source);
        }

        var converter = new ImageConverter();
        var output = await converter.ConvertAsync(source, new ConversionOptions { TargetFormat = "webp", Quality = 85 });

        Assert.True(File.Exists(output));
        using var converted = new MagickImage(output);
        Assert.Equal(MagickFormat.WebP, converted.Format);
        Assert.Equal(32u, converted.Width);
        Assert.Equal(24u, converted.Height);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, true);
        }
    }
}
