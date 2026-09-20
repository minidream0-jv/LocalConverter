namespace LocalConverter.Core;

public sealed record ConversionOptions
{
    public string TargetFormat { get; init; } = string.Empty;
    public string? OutputDirectory { get; init; }
    public int Quality { get; init; } = 85;
    public int? Width { get; init; }
    public int? Height { get; init; }
    public bool PreserveAspectRatio { get; init; } = true;
    public bool PreserveTransparency { get; init; } = true;
    public int? FramesPerSecond { get; init; }
    public string? VideoCodec { get; init; }
    public string? VideoBitrate { get; init; }
    public string? AudioCodec { get; init; }
    public string? AudioBitrate { get; init; }
    public int? SampleRate { get; init; }
    public int? Channels { get; init; }
}

