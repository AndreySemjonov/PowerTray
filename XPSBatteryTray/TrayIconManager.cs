using System.Drawing;
using System.Windows;
using System.Windows.Forms;
using XPSBatteryTray.Models;
using XPSBatteryTray.ViewModels;
using XPSBatteryTray.Views;
using Application = System.Windows.Application;

namespace XPSBatteryTray;

public sealed class TrayIconManager : IDisposable
{
    private readonly MainViewModel _viewModel;
    private readonly Func<SettingsWindow> _settingsWindowFactory;
    private readonly NotifyIcon _notifyIcon;
    private DashboardWindow? _dashboardWindow;
    private SettingsWindow? _settingsWindow;
    private BatteryModesWindow? _batteryModesWindow;
    private bool _disposed;

    public TrayIconManager(MainViewModel viewModel, Func<SettingsWindow> settingsWindowFactory)
    {
        _viewModel = viewModel;
        _settingsWindowFactory = settingsWindowFactory;
        _viewModel.OpenSettingsRequested += (_, _) => ShowSettings();
        _viewModel.OpenBatteryModesRequested += (_, _) => ShowBatteryModes();
        _notifyIcon = CreateNotifyIcon();
    }

    public void Show() => _notifyIcon.Visible = true;

    public void ShowDashboard()
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            _dashboardWindow ??= new DashboardWindow(_viewModel);
            if (!_dashboardWindow.IsVisible)
            {
                PositionDashboardNearTray(_dashboardWindow);
                _dashboardWindow.Show();
            }

            _dashboardWindow.Activate();
        });
    }

    public void ShowSettings()
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            if (_settingsWindow is { IsVisible: true })
            {
                _settingsWindow.Activate();
                return;
            }

            _settingsWindow = _settingsWindowFactory();
            _settingsWindow.Owner = _dashboardWindow;
            _settingsWindow.Show();
            _settingsWindow.Activate();
        });
    }

    public void ShowBatteryModes()
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            if (_batteryModesWindow is { IsVisible: true })
            {
                _batteryModesWindow.Activate();
                return;
            }

            _batteryModesWindow = new BatteryModesWindow(_viewModel)
            {
                Owner = _dashboardWindow
            };
            _batteryModesWindow.Show();
            _batteryModesWindow.Activate();
        });
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
    }

    private NotifyIcon CreateNotifyIcon()
    {
        var contextMenu = new ContextMenuStrip();
        contextMenu.Items.Add("Open Dashboard", null, (_, _) => ShowDashboard());
        contextMenu.Items.Add(new ToolStripSeparator());
        contextMenu.Items.Add("Battery Health Mode: Custom 50-80", null, (_, _) => ApplyPreset(BatteryPreset.Health));
        contextMenu.Items.Add("Balanced Mode: Custom 70-90", null, (_, _) => ApplyPreset(BatteryPreset.Balanced));
        contextMenu.Items.Add("Charge to Full: Standard", null, (_, _) => ApplyPreset(BatteryPreset.Standard));
        contextMenu.Items.Add("Primarily AC Use", null, (_, _) => ApplyPreset(BatteryPreset.PrimarilyAcUse));
        contextMenu.Items.Add("Adaptive", null, (_, _) => ApplyPreset(BatteryPreset.Adaptive));
        contextMenu.Items.Add(new ToolStripSeparator());
        contextMenu.Items.Add("Show Current Dell Charge Setting", null, async (_, _) => await _viewModel.RefreshDellChargeAsync());
        contextMenu.Items.Add("Settings", null, (_, _) => ShowSettings());
        contextMenu.Items.Add(new ToolStripSeparator());
        contextMenu.Items.Add("Exit", null, (_, _) => ExitApplication());

        var icon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "XPS Battery Tray",
            ContextMenuStrip = contextMenu,
            Visible = false
        };

        icon.MouseClick += (_, args) =>
        {
            if (args.Button == MouseButtons.Left)
            {
                ShowDashboard();
            }
        };

        return icon;
    }

    private void ApplyPreset(BatteryPreset preset)
    {
        Application.Current.Dispatcher.Invoke(() => _viewModel.ApplyBatteryPresetCommand.Execute(preset));
        _notifyIcon.ShowBalloonTip(2500, "XPS Battery Tray", "Battery mode command started.", ToolTipIcon.Info);
    }

    private static void PositionDashboardNearTray(Window window)
    {
        Rect workArea = SystemParameters.WorkArea;
        window.Left = Math.Max(workArea.Left, workArea.Right - window.Width - 12);
        window.Top = Math.Max(workArea.Top, workArea.Bottom - window.Height - 12);
    }

    private void ExitApplication()
    {
        Dispose();
        Application.Current.Shutdown();
    }
}
