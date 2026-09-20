namespace LocalConverter.Core;

public sealed record ConversionResult(string SourcePath, string? OutputPath, Exception? Error)
{
    public bool Success => Error is null;
}

