using System.IO;
using System.Windows;
using System.Windows.Controls;
using LocalConverter.Core;
using Forms = System.Windows.Forms;

namespace LocalConverter.App;

public partial class ConversionWindow : Window
{
    private readonly ConversionJob _job;
    private readonly ConversionQueue _queue;
    private readonly FileLogger _logger;
    private readonly MediaKind _kind;
    private CancellationTokenSource? _cancellation;

    public ConversionWindow(ConversionJob job, ConversionQueue queue, FileLogger logger)
    {
        InitializeComponent();
        _job = job;
        _queue = queue;
        _logger = logger;
        _kind = DetectCommonKind(job.Files);

        SourceSummary.Text = job.Files.Count == 1
            ? Path.GetFileName(job.Files[0])
            : $"Выбрано файлов: {job.Files.Count}";
        OutputFolderBox.Text = Path.GetDirectoryName(job.Files[0]) ?? string.Empty;
        FormatBox.ItemsSource = FormatCatalog.GetTargets(_kind).Select(value => value.ToUpperInvariant()).ToArray();
        FormatBox.SelectedIndex = 0;
        VideoCodecBox.SelectedIndex = 0;
        VideoAudioCodecBox.SelectedIndex = 0;
        SampleRateBox.SelectedIndex = 0;
        ChannelsBox.SelectedIndex = 0;

        ImageOptions.Visibility = _kind == MediaKind.Image ? Visibility.Visible : Visibility.Collapsed;
        VideoOptions.Visibility = _kind == MediaKind.Video ? Visibility.Visible : Visibility.Collapsed;
        AudioOptions.Visibility = _kind == MediaKind.Audio ? Visibility.Visible : Visibility.Collapsed;
    }

    private static MediaKind DetectCommonKind(IEnumerable<string> files)
    {
        var kinds = files.Select(path => FormatCatalog.Detect(path).Kind).Distinct().ToArray();
        if (kinds.Length != 1 || kinds[0] == MediaKind.Unknown)
        {
            throw new NotSupportedException("Для расширенной конвертации выберите файлы одного поддерживаемого типа.");
        }

        return kinds[0];
    }

    private void Browse_OnClick(object sender, RoutedEventArgs e)
    {
        using var dialog = new Forms.FolderBrowserDialog
        {
            Description = "Папка для конвертированных файлов",
            SelectedPath = OutputFolderBox.Text,
            UseDescriptionForTitle = true
        };
        if (dialog.ShowDialog() == Forms.DialogResult.OK)
        {
            OutputFolderBox.Text = dialog.SelectedPath;
        }
    }

    private async void Convert_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var options = BuildOptions();
            ConvertButton.IsEnabled = false;
            StatusText.Text = "Конвертация…";
            _cancellation = new CancellationTokenSource();
            var results = await _queue.RunAsync(_job.Files, options, _cancellation.Token);
            foreach (var failed in results.Where(result => !result.Success))
            {
                await _logger.LogAsync($"Ошибка конвертации: {failed.SourcePath}", failed.Error);
            }

            var failure = results.FirstOrDefault(result => !result.Success);
            if (failure is not null)
            {
                StatusText.Text = $"Ошибка: {Path.GetFileName(failure.SourcePath)}";
                await NotificationService.ShowAsync("Local Converter", $"Не удалось конвертировать {Path.GetFileName(failure.SourcePath)}", true);
                ConvertButton.IsEnabled = true;
                return;
            }

            await NotificationService.ShowAsync(
                "Local Converter",
                results.Count == 1 ? "Конвертация завершена" : $"Конвертировано файлов: {results.Count}");
            Close();
        }
        catch (OperationCanceledException)
        {
            Close();
        }
        catch (Exception exception)
        {
            await _logger.LogAsync("Ошибка расширенной конвертации.", exception);
            StatusText.Text = exception.Message;
            ConvertButton.IsEnabled = true;
        }
    }

    private ConversionOptions BuildOptions()
    {
        var selected = FormatBox.SelectedItem as string
            ?? throw new InvalidOperationException("Выберите выходной формат.");
        var outputDirectory = OutputFolderBox.Text.Trim();
        if (outputDirectory.Length == 0)
        {
            throw new InvalidOperationException("Выберите папку сохранения.");
        }

        return new ConversionOptions
        {
            TargetFormat = selected,
            OutputDirectory = outputDirectory,
            Quality = (int)QualitySlider.Value,
            Width = ParseNullableInt(_kind == MediaKind.Video ? VideoWidthBox.Text : ImageWidthBox.Text, "ширина"),
            Height = ParseNullableInt(_kind == MediaKind.Video ? VideoHeightBox.Text : ImageHeightBox.Text, "высота"),
            PreserveAspectRatio = PreserveAspectBox.IsChecked == true,
            PreserveTransparency = PreserveTransparencyBox.IsChecked == true,
            FramesPerSecond = ParseNullableInt(FpsBox.Text, "FPS"),
            VideoCodec = ReadCombo(VideoCodecBox),
            VideoBitrate = EmptyToNull(VideoBitrateBox.Text),
            AudioCodec = ReadCombo(VideoAudioCodecBox),
            AudioBitrate = _kind == MediaKind.Video ? EmptyToNull(VideoAudioBitrateBox.Text) : EmptyToNull(AudioBitrateBox.Text),
            SampleRate = ParseComboInt(SampleRateBox, "частота дискретизации"),
            Channels = ParseComboInt(ChannelsBox, "число каналов")
        };
    }

    private static int? ParseNullableInt(string text, string field)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        if (!int.TryParse(text, out var result) || result <= 0)
        {
            throw new InvalidOperationException($"Некорректное значение поля «{field}».");
        }

        return result;
    }

    private static int? ParseComboInt(System.Windows.Controls.ComboBox comboBox, string field) =>
        ParseNullableInt(ReadCombo(comboBox) ?? string.Empty, field);

    private static string? ReadCombo(System.Windows.Controls.ComboBox comboBox)
    {
        var value = comboBox.Text.Trim();
        return value.Length == 0 || value.Equals("Авто", StringComparison.OrdinalIgnoreCase) ? null : value;
    }

    private static string? EmptyToNull(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private void Cancel_OnClick(object sender, RoutedEventArgs e)
    {
        _cancellation?.Cancel();
        if (_cancellation is null)
        {
            Close();
        }
    }

    private void FormatBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (FormatBox.SelectedItem is string target)
        {
            PreserveTransparencyBox.IsEnabled = target is "PNG" or "WEBP" or "AVIF" or "TIFF";
        }
    }
}
