using System.Drawing;
using Forms = System.Windows.Forms;

namespace LocalConverter.App;

internal static class NotificationService
{
    public static async Task ShowAsync(string title, string message, bool isError = false)
    {
        using var icon = new Forms.NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "Local Converter",
            Visible = true,
            BalloonTipIcon = isError ? Forms.ToolTipIcon.Error : Forms.ToolTipIcon.Info,
            BalloonTipTitle = title,
            BalloonTipText = message
        };

        icon.ShowBalloonTip(4_000);
        await Task.Delay(4_500);
        icon.Visible = false;
    }
}

