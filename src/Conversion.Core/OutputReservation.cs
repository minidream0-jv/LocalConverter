namespace LocalConverter.Core;

public sealed class OutputReservation : IDisposable
{
    private bool _committed;

    private OutputReservation(string path)
    {
        Path = path;
    }

    public string Path { get; }

    public static OutputReservation Create(string sourcePath, string targetExtension, string? outputDirectory)
    {
        var directory = string.IsNullOrWhiteSpace(outputDirectory)
            ? System.IO.Path.GetDirectoryName(sourcePath)
            : outputDirectory;

        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new DirectoryNotFoundException("Не удалось определить папку сохранения.");
        }

        Directory.CreateDirectory(directory);
        var stem = System.IO.Path.GetFileNameWithoutExtension(sourcePath);
        var extension = "." + FormatCatalog.NormalizeExtension(targetExtension);

        for (var index = 0; index < 10_000; index++)
        {
            var suffix = index == 0 ? string.Empty : $" ({index})";
            var candidate = System.IO.Path.Combine(directory, stem + suffix + extension);
            try
            {
                using var reservation = new FileStream(candidate, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                return new OutputReservation(candidate);
            }
            catch (IOException) when (File.Exists(candidate))
            {
                // A pre-existing file or another conversion owns this name. Try the next suffix.
            }
        }

        throw new IOException("Не удалось подобрать свободное имя выходного файла.");
    }

    public void Commit() => _committed = true;

    public void Dispose()
    {
        if (!_committed)
        {
            try
            {
                File.Delete(Path);
            }
            catch
            {
                // Preserve the original conversion failure; cleanup is best-effort.
            }
        }
    }
}

