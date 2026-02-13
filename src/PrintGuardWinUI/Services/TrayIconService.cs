using H.NotifyIcon;
using H.NotifyIcon.Core;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;

namespace PrintGuardWinUI.Services;

public enum TrayNotificationLevel
{
    Info,
    Warning,
    Error
}

public sealed class TrayIconService : IDisposable
{
    private readonly TaskbarIcon _taskbarIcon;
    private readonly MenuFlyoutItem _statusItem;
    private readonly MenuFlyoutItem _toggleItem;

    public TrayIconService()
    {
        _statusItem = new MenuFlyoutItem
        {
            Text = "Status: Initializing",
            IsEnabled = false
        };

        _toggleItem = new MenuFlyoutItem
        {
            Text = "Disable protection"
        };
        _toggleItem.Click += (_, _) => ToggleProtectionRequested?.Invoke(this, EventArgs.Empty);

        var unlockSubItem = new MenuFlyoutSubItem
        {
            Text = "Unlock printing for"
        };
        unlockSubItem.Items.Add(CreateUnlockItem(5));
        unlockSubItem.Items.Add(CreateUnlockItem(15));
        unlockSubItem.Items.Add(CreateUnlockItem(60));

        var settingsItem = new MenuFlyoutItem
        {
            Text = "Settings"
        };
        settingsItem.Click += (_, _) => SettingsRequested?.Invoke(this, EventArgs.Empty);

        var logsItem = new MenuFlyoutItem
        {
            Text = "View logs"
        };
        logsItem.Click += (_, _) => LogsRequested?.Invoke(this, EventArgs.Empty);

        var exitItem = new MenuFlyoutItem
        {
            Text = "Exit"
        };
        exitItem.Click += (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty);

        var menu = new MenuFlyout();
        menu.Items.Add(_statusItem);
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(_toggleItem);
        menu.Items.Add(unlockSubItem);
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(settingsItem);
        menu.Items.Add(logsItem);
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(exitItem);

        _taskbarIcon = new TaskbarIcon
        {
            ToolTipText = "PrintGuard",
            IconSource = new BitmapImage(new Uri("ms-appx:///Assets/Square44x44Logo.targetsize-24_altform-unplated.png")),
            ContextFlyout = menu,
            ContextMenuMode = ContextMenuMode.PopupMenu,
            MenuActivation = PopupActivationMode.RightClick,
            LeftClickCommand = new DelegateCommand(() => SettingsRequested?.Invoke(this, EventArgs.Empty)),
            NoLeftClickDelay = true
        };
    }

    public event EventHandler? ToggleProtectionRequested;
    public event EventHandler<int>? UnlockRequested;
    public event EventHandler? SettingsRequested;
    public event EventHandler? LogsRequested;
    public event EventHandler? ExitRequested;

    public void Show()
    {
        _taskbarIcon.ForceCreate();
    }

    public void UpdateState(bool isEnabled, DateTimeOffset? unlockUntilUtc)
    {
        _toggleItem.Text = isEnabled ? "Disable protection" : "Enable protection";

        var status = isEnabled ? "Enabled" : "Disabled";
        if (unlockUntilUtc is { } unlockedUntil && unlockedUntil > DateTimeOffset.UtcNow)
        {
            status = $"Temporarily unlocked until {unlockedUntil.ToLocalTime():t}";
        }

        _statusItem.Text = $"Status: {status}";
    }

    public void ShowBalloon(
        string title,
        string message,
        TrayNotificationLevel level = TrayNotificationLevel.Info)
    {
        var icon = level switch
        {
            TrayNotificationLevel.Warning => NotificationIcon.Warning,
            TrayNotificationLevel.Error => NotificationIcon.Error,
            _ => NotificationIcon.Info
        };

        _taskbarIcon.ShowNotification(
            title,
            message,
            icon,
            customIconHandle: null,
            largeIcon: false,
            sound: true,
            respectQuietTime: false,
            realtime: false,
            timeout: TimeSpan.FromSeconds(10));
    }

    public void Dispose()
    {
        _taskbarIcon.Dispose();
    }

    private MenuFlyoutItem CreateUnlockItem(int minutes)
    {
        var item = new MenuFlyoutItem
        {
            Text = $"{minutes} minute{(minutes == 1 ? string.Empty : "s")}"
        };
        item.Click += (_, _) => UnlockRequested?.Invoke(this, minutes);
        return item;
    }

    private sealed class DelegateCommand : System.Windows.Input.ICommand
    {
        private readonly Action _execute;
        private readonly Func<bool>? _canExecute;

        public DelegateCommand(Action execute, Func<bool>? canExecute = null)
        {
            _execute = execute;
            _canExecute = canExecute;
        }

        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter) => _canExecute?.Invoke() ?? true;

        public void Execute(object? parameter) => _execute();

        public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}
