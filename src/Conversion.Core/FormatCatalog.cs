namespace LocalConverter.Core;

public static class FormatCatalog
{
    private static readonly IReadOnlyDictionary<string, FormatInfo> Formats =
        new Dictionary<string, FormatInfo>(StringComparer.OrdinalIgnoreCase)
        {
            [".png"] = new("png", "PNG", MediaKind.Image),
            [".jpg"] = new("jpg", "JPG", MediaKind.Image),
            [".jpeg"] = new("jpg", "JPG", MediaKind.Image),
            [".webp"] = new("webp", "WEBP", MediaKind.Image),
            [".avif"] = new("avif", "AVIF", MediaKind.Image),
            [".bmp"] = new("bmp", "BMP", MediaKind.Image),
            [".tif"] = new("tiff", "TIFF", MediaKind.Image),
            [".tiff"] = new("tiff", "TIFF", MediaKind.Image),
            [".mp4"] = new("mp4", "MP4", MediaKind.Video),
            [".mkv"] = new("mkv", "MKV", MediaKind.Video),
            [".mov"] = new("mov", "MOV", MediaKind.Video),
            [".webm"] = new("webm", "WEBM", MediaKind.Video),
            [".avi"] = new("avi", "AVI", MediaKind.Video),
            [".mp3"] = new("mp3", "MP3", MediaKind.Audio),
            [".wav"] = new("wav", "WAV", MediaKind.Audio),
            [".flac"] = new("flac", "FLAC", MediaKind.Audio),
            [".aac"] = new("aac", "AAC", MediaKind.Audio),
            [".m4a"] = new("m4a", "M4A", MediaKind.Audio),
            [".ogg"] = new("ogg", "OGG", MediaKind.Audio),
            [".opus"] = new("opus", "OPUS", MediaKind.Audio)
        };

    public static readonly IReadOnlyList<string> ImageTargets = ["jpg", "png", "webp", "avif", "bmp", "tiff"];
    public static readonly IReadOnlyList<string> VideoTargets = ["mp4", "mkv", "mov", "webm", "mp3"];
    public static readonly IReadOnlyList<string> AudioTargets = ["mp3", "wav", "flac", "aac", "ogg"];

    public static FormatInfo Detect(string path)
    {
        var extension = Path.GetExtension(path);
        return Formats.TryGetValue(extension, out var format)
            ? format
            : new FormatInfo(extension.TrimStart('.').ToLowerInvariant(), extension, MediaKind.Unknown);
    }

    public static bool IsSupportedTarget(MediaKind kind, string extension) =>
        GetTargets(kind).Contains(NormalizeExtension(extension), StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<string> GetTargets(MediaKind kind) => kind switch
    {
        MediaKind.Image => ImageTargets,
        MediaKind.Video => VideoTargets,
        MediaKind.Audio => AudioTargets,
        _ => Array.Empty<string>()
    };

    public static string NormalizeExtension(string extension) =>
        extension.Trim().TrimStart('.').ToLowerInvariant() switch
        {
            "jpeg" => "jpg",
            "tif" => "tiff",
            "m4a" => "aac",
            "opus" => "ogg",
            var value => value
        };
}

