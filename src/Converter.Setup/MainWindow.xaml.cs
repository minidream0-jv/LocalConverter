using System.IO;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using System.Windows;
using Windows.Management.Deployment;

namespace LocalConverter.Setup;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private async void Install_OnClick(object sender, RoutedEventArgs e)
    {
        InstallButton.IsEnabled = false;
        CancelButton.IsEnabled = false;
        Progress.Visibility = Visibility.Visible;
        StatusText.Text = "Установка…";
        var temporaryDirectory = Path.Combine(Path.GetTempPath(), "LocalConverterSetup", Guid.NewGuid().ToString("N"));

        try
        {
            Directory.CreateDirectory(temporaryDirectory);
            var packagePath = Path.Combine(temporaryDirectory, "LocalConverter.msix");
            var certificatePath = Path.Combine(temporaryDirectory, "LocalConverter.cer");
            ExtractResource("LocalConverter.msix", packagePath);
            ExtractResource("LocalConverter.cer", certificatePath);
            InstallCertificate(certificatePath);
            await InstallPackageAsync(packagePath);
            ShowUsageInstructions();
        }
        catch (Exception exception)
        {
            Progress.Visibility = Visibility.Collapsed;
            StatusText.Text = "Не удалось установить Local Converter. " + exception.Message;
            InstallButton.IsEnabled = true;
            CancelButton.IsEnabled = true;
        }
        finally
        {
            try
            {
                if (Directory.Exists(temporaryDirectory))
                {
                    Directory.Delete(temporaryDirectory, true);
                }
            }
            catch
            {
                // The OS can clean a locked temporary file later.
            }
        }
    }

    private void ShowUsageInstructions()
    {
        Progress.Visibility = Visibility.Collapsed;
        InstallPanel.Visibility = Visibility.Collapsed;
        UsagePanel.Visibility = Visibility.Visible;
        Title = "Как пользоваться Local Converter";
        DoneButton.Focus();
    }

    private static void ExtractResource(string resourceName, string destination)
    {
        using var source = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Установочный ресурс {resourceName} отсутствует.");
        using var target = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        source.CopyTo(target);
    }

    private static void InstallCertificate(string path)
    {
        using var certificate = X509CertificateLoader.LoadCertificateFromFile(path);
        using var store = new X509Store(StoreName.TrustedPeople, StoreLocation.LocalMachine);
        store.Open(OpenFlags.ReadWrite);
        if (!store.Certificates.Any(item => item.Thumbprint.Equals(certificate.Thumbprint, StringComparison.OrdinalIgnoreCase)))
        {
            store.Add(certificate);
        }
    }

    private static async Task InstallPackageAsync(string packagePath)
    {
        var packageManager = new PackageManager();
        var options = new AddPackageOptions
        {
            ForceUpdateFromAnyVersion = true
        };
        var result = await packageManager.AddPackageByUriAsync(new Uri(packagePath), options);
        if (result.ExtendedErrorCode is { } error)
        {
            throw new InvalidOperationException(result.ErrorText ?? $"Ошибка установки 0x{error.HResult:X8}.");
        }
    }

    private void Cancel_OnClick(object sender, RoutedEventArgs e) => Close();

    private void Done_OnClick(object sender, RoutedEventArgs e) => Close();
}
