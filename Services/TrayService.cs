using System.Drawing;
using System.Windows.Forms;

namespace EkipVisual.Services;

public sealed class TrayService : IDisposable
{
    private readonly NotifyIcon _icon;

    public event Action? OpenRequested;
    public event Action? ExitRequested;

    public TrayService()
    {
        var exePath = Environment.ProcessPath;
        var icon = !string.IsNullOrEmpty(exePath) ? Icon.ExtractAssociatedIcon(exePath) : SystemIcons.Application;

        var menu = new ContextMenuStrip();
        menu.Items.Add("Ouvrir EkipVisual", null, (_, _) => OpenRequested?.Invoke());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Quitter", null, (_, _) => ExitRequested?.Invoke());

        _icon = new NotifyIcon
        {
            Icon = icon,
            Text = "EkipVisual",
            Visible = true,
            ContextMenuStrip = menu,
        };
        _icon.DoubleClick += (_, _) => OpenRequested?.Invoke();
    }

    public void ShowBalloon(string title, string text) => _icon.ShowBalloonTip(3000, title, text, ToolTipIcon.Info);

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
