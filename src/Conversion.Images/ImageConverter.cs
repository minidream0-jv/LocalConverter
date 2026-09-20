using ImageMagick;
using LocalConverter.Core;

namespace LocalConverter.Images;

public sealed class ImageConverter : IConversionBackend
{
    public bool CanConvert(MediaKind sourceKind) => sourceKind == MediaKind.Image;

    public Task<string> ConvertAsync(
        string sourcePath,
        ConversionOptions options,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => Convert(sourcePath, options, cancellationToken), cancellationToken);

    private static string Convert(
        string sourcePath,
        ConversionOptions options,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var output = OutputReservation.Create(sourcePath, options.TargetFormat, options.OutputDirectory);
        using var image = new MagickImage(sourcePath);

        image.AutoOrient();
        image.Strip();

        if (options.Width is > 0 || options.Height is > 0)
        {
            var geometry = new MagickGeometry(
                (uint)Math.Max(0, options.Width ?? 0),
                (uint)Math.Max(0, options.Height ?? 0))
            {
                IgnoreAspectRatio = !options.PreserveAspectRatio
            };
            image.Resize(geometry);
        }

        image.Quality = (uint)Math.Clamp(options.Quality, 1, 100);
        image.Format = ToMagickFormat(options.TargetFormat);

        if (!options.PreserveTransparency || image.Format is MagickFormat.Jpeg or MagickFormat.Bmp)
        {
            image.BackgroundColor = MagickColors.White;
            image.Alpha(AlphaOption.Remove);
        }

        cancellationToken.ThrowIfCancellationRequested();
        image.Write(output.Path);
        output.Commit();
        return output.Path;
    }

    private static MagickFormat ToMagickFormat(string extension) =>
        FormatCatalog.NormalizeExtension(extension) switch
        {
            "jpg" => MagickFormat.Jpeg,
            "png" => MagickFormat.Png,
            "webp" => MagickFormat.WebP,
            "avif" => MagickFormat.Avif,
            "bmp" => MagickFormat.Bmp,
            "tiff" => MagickFormat.Tiff,
            _ => throw new NotSupportedException($"Неподдерживаемый формат изображения: {extension}")
        };
}
