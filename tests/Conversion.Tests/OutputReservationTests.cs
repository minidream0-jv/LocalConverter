using LocalConverter.Core;

namespace LocalConverter.Tests;

public sealed class OutputReservationTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "LocalConverterTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void Create_UsesNumberedSuffixAndNeverOverwrites()
    {
        Directory.CreateDirectory(_directory);
        var source = Path.Combine(_directory, "фото.png");
        File.WriteAllText(source, "source");
        File.WriteAllText(Path.Combine(_directory, "фото.webp"), "existing");

        using var reservation = OutputReservation.Create(source, "webp", null);

        Assert.Equal(Path.Combine(_directory, "фото (1).webp"), reservation.Path);
        Assert.Equal("existing", File.ReadAllText(Path.Combine(_directory, "фото.webp")));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, true);
        }
    }
}

