using System.Text.Json;
using System.Text.Json.Serialization;

namespace LocalConverter.Core;

public sealed record ConversionJob
{
    [JsonPropertyName("targetFormat")]
    public string TargetFormat { get; init; } = string.Empty;

    [JsonPropertyName("files")]
    public IReadOnlyList<string> Files { get; init; } = Array.Empty<string>();

    public static async Task<ConversionJob> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, true);
        var job = await JsonSerializer.DeserializeAsync<ConversionJob>(stream, cancellationToken: cancellationToken);
        if (job is null || job.Files.Count == 0)
        {
            throw new InvalidDataException("Задание конвертации пусто или повреждено.");
        }

        return job with { TargetFormat = FormatCatalog.NormalizeExtension(job.TargetFormat) };
    }
}

