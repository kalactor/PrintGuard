using System.Drawing;
using WinForms = System.Windows.Forms;

namespace PrintGuard.App.Services;

public sealed class TrayManager : IDisposable
{
    private readonly WinForms.NotifyIcon _notifyIcon;
    private readonly WinForms.ToolStripMenuItem _statusMenuItem;
    private readonly WinForms.ToolStripMenuItem _toggleMenuItem;

    public TrayManager()
    {
        _notifyIcon = new WinForms.NotifyIcon
        {
            Icon = SystemIcons.Shield,
            Text = "Print Guard",
            Visible = false
        };

        _statusMenuItem = new WinForms.ToolStripMenuItem("Status: Initializing")
        {
            Enabled = false
        };

        _toggleMenuItem = new WinForms.ToolStripMenuItem("Disable protection");
        _toggleMenuItem.Click += (_, _) => ToggleProtectionRequested?.Invoke(this, EventArgs.Empty);

        var unlockMenu = new WinForms.ToolStripMenuItem("Unlock printing for");
        unlockMenu.DropDownItems.Add(CreateUnlockItem(5));
        unlockMenu.DropDownItems.Add(CreateUnlockItem(15));
        unlockMenu.DropDownItems.Add(CreateUnlockItem(60));

        var settingsItem = new WinForms.ToolStripMenuItem("Settings");
        settingsItem.Click += (_, _) => SettingsRequested?.Invoke(this, EventArgs.Empty);

        var logsItem = new WinForms.ToolStripMenuItem("View logs");
        logsItem.Click += (_, _) => LogsRequested?.Invoke(this, EventArgs.Empty);

        var aboutItem = new WinForms.ToolStripMenuItem("About");
        aboutItem.Click += (_, _) => AboutRequested?.Invoke(this, EventArgs.Empty);

        var exitItem = new WinForms.ToolStripMenuItem("Exit");
        exitItem.Click += (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty);

        _notifyIcon.ContextMenuStrip = new WinForms.ContextMenuStrip();
        _notifyIcon.ContextMenuStrip.Items.Add(_statusMenuItem);
        _notifyIcon.ContextMenuStrip.Items.Add(new WinForms.ToolStripSeparator());
        _notifyIcon.ContextMenuStrip.Items.Add(_toggleMenuItem);
        _notifyIcon.ContextMenuStrip.Items.Add(unlockMenu);
        _notifyIcon.ContextMenuStrip.Items.Add(new WinForms.ToolStripSeparator());
        _notifyIcon.ContextMenuStrip.Items.Add(settingsItem);
        _notifyIcon.ContextMenuStrip.Items.Add(logsItem);
        _notifyIcon.ContextMenuStrip.Items.Add(aboutItem);
        _notifyIcon.ContextMenuStrip.Items.Add(new WinForms.ToolStripSeparator());
        _notifyIcon.ContextMenuStrip.Items.Add(exitItem);

        _notifyIcon.DoubleClick += (_, _) => SettingsRequested?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? ToggleProtectionRequested;
    public event EventHandler<int>? UnlockRequested;
    public event EventHandler? SettingsRequested;
    public event EventHandler? LogsRequested;
    public event EventHandler? AboutRequested;
    public event EventHandler? ExitRequested;

    public void Show()
    {
        _notifyIcon.Visible = true;
    }

    public void UpdateState(bool isEnabled, DateTimeOffset? unlockUntilUtc)
    {
        _toggleMenuItem.Text = isEnabled ? "Disable protection" : "Enable protection";

        var status = isEnabled ? "Enabled" : "Disabled";

        if (unlockUntilUtc is { } unlockedUntil && unlockedUntil > DateTimeOffset.UtcNow)
        {
            status = $"Temporarily unlocked until {unlockedUntil.ToLocalTime():t}";
        }

        _statusMenuItem.Text = $"Status: {status}";
    }

    public void ShowBalloon(string title, string message, WinForms.ToolTipIcon icon = WinForms.ToolTipIcon.Info)
    {
        _notifyIcon.BalloonTipTitle = title;
        _notifyIcon.BalloonTipText = message;
        _notifyIcon.BalloonTipIcon = icon;
        _notifyIcon.ShowBalloonTip(4000);
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
    }

    private WinForms.ToolStripMenuItem CreateUnlockItem(int minutes)
    {
        var suffix = minutes == 1 ? string.Empty : "s";
        var item = new WinForms.ToolStripMenuItem($"{minutes} minute{suffix}");
        item.Click += (_, _) => UnlockRequested?.Invoke(this, minutes);
        return item;
    }
}
