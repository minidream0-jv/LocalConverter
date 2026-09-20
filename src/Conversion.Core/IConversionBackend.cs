namespace LocalConverter.Core;

public interface IConversionBackend
{
    bool CanConvert(MediaKind sourceKind);

    Task<string> ConvertAsync(
        string sourcePath,
        ConversionOptions options,
        CancellationToken cancellationToken = default);
}

