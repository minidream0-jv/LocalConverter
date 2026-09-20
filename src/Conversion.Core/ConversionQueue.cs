using System.Collections.Concurrent;

namespace LocalConverter.Core;

public sealed class ConversionQueue
{
    private readonly IReadOnlyList<IConversionBackend> _backends;
    private readonly int _parallelism;

    public ConversionQueue(IEnumerable<IConversionBackend> backends, int? parallelism = null)
    {
        _backends = backends.ToArray();
        _parallelism = Math.Clamp(parallelism ?? Math.Max(1, Environment.ProcessorCount / 2), 1, 3);
    }

    public async Task<IReadOnlyList<ConversionResult>> RunAsync(
        IEnumerable<string> paths,
        ConversionOptions options,
        CancellationToken cancellationToken = default)
    {
        var files = paths.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var results = new ConcurrentDictionary<int, ConversionResult>();

        await Parallel.ForEachAsync(
            Enumerable.Range(0, files.Length),
            new ParallelOptions { MaxDegreeOfParallelism = _parallelism, CancellationToken = cancellationToken },
            async (index, token) =>
            {
                var source = files[index];
                try
                {
                    if (!File.Exists(source))
                    {
                        throw new FileNotFoundException("Исходный файл не найден.", source);
                    }

                    var kind = FormatCatalog.Detect(source).Kind;
                    var backend = _backends.FirstOrDefault(item => item.CanConvert(kind))
                        ?? throw new NotSupportedException($"Формат '{Path.GetExtension(source)}' не поддерживается.");

                    if (!FormatCatalog.IsSupportedTarget(kind, options.TargetFormat))
                    {
                        throw new NotSupportedException($"Нельзя преобразовать {kind} в {options.TargetFormat}.");
                    }

                    var output = await backend.ConvertAsync(source, options, token);
                    results[index] = new ConversionResult(source, output, null);
                }
                catch (Exception exception)
                {
                    results[index] = new ConversionResult(source, null, exception);
                }
            });

        return Enumerable.Range(0, files.Length).Select(index => results[index]).ToArray();
    }
}

