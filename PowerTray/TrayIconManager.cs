using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Resources;
using PowerTray.Models;
using PowerTray.Services;
using PowerTray.ViewModels;
using PowerTray.Views;
using Application = System.Windows.Application;

namespace PowerTray;

public sealed class TrayIconManager : IDisposable
{
    private const string AppDisplayName = "PowerTray";

    private readonly MainViewModel _viewModel;
    private readonly Func<SettingsWindow> _settingsWindowFactory;
    private readonly Func<DimmerWindow> _dimmerWindowFactory;
    private readonly NotifyIcon _notifyIcon;
    private readonly Icon _appIcon;
    private DashboardWindow? _dashboardWindow;
    private SettingsWindow? _settingsWindow;
    private DimmerWindow? _dimmerWindow;
    private bool _disposed;

    public TrayIconManager(MainViewModel viewModel, Func<SettingsWindow> settingsWindowFactory, Func<DimmerWindow> dimmerWindowFactory)
    {
        _viewModel = viewModel;
        _settingsWindowFactory = settingsWindowFactory;
        _dimmerWindowFactory = dimmerWindowFactory;
        _viewModel.OpenSettingsRequested += (_, _) => ShowSettings();
        _viewModel.OpenDimmerRequested += (_, _) => ShowDimmer();
        _viewModel.PropertyChanged += ViewModel_PropertyChanged;
        _appIcon = LoadTrayIcon();
        _notifyIcon = CreateNotifyIcon();
        UpdateTrayStatus();
    }

    public void Show() => _notifyIcon.Visible = true;

    public void ShowDashboard()
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            _dashboardWindow ??= CreateDashboardWindow();
            if (_dashboardWindow.WindowState == WindowState.Minimized)
            {
                _dashboardWindow.WindowState = WindowState.Normal;
            }

            if (!_dashboardWindow.IsVisible)
            {
                PositionDashboardNearTray(_dashboardWindow);
                _dashboardWindow.Show();
            }

            _dashboardWindow.Activate();
        });
    }

    private DashboardWindow CreateDashboardWindow()
    {
        var window = new DashboardWindow(_viewModel);
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_dashboardWindow, window))
            {
                _dashboardWindow = null;
            }

            _viewModel.IsDashboardVisible = false;
            GC.Collect(2, GCCollectionMode.Optimized, blocking: false, compacting: false);
        };

        return window;
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

    public void ShowDimmer()
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            if (!_viewModel.IsScreenDimmerEnabled)
            {
                ShowSettings();
                return;
            }

            if (_dimmerWindow is { IsVisible: true })
            {
                _dimmerWindow.Activate();
                return;
            }

            var window = _dimmerWindowFactory();
            _dimmerWindow = window;
            window.Owner = _dashboardWindow;
            window.Closed += (_, _) =>
            {
                if (ReferenceEquals(_dimmerWindow, window))
                {
                    _dimmerWindow = null;
                }
            };
            window.Show();
            window.Activate();
        });
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _appIcon.Dispose();
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
        var powerModeMenu = new ToolStripMenuItem("Windows Power Mode");
        powerModeMenu.DropDownItems.Add("Power efficiency", null, (_, _) => ApplyWindowsPowerMode(WindowsPowerMode.BestPowerEfficiency));
        powerModeMenu.DropDownItems.Add("Balanced", null, (_, _) => ApplyWindowsPowerMode(WindowsPowerMode.Balanced));
        powerModeMenu.DropDownItems.Add("Performance", null, (_, _) => ApplyWindowsPowerMode(WindowsPowerMode.BestPerformance));
        contextMenu.Items.Add(powerModeMenu);
        contextMenu.Items.Add(new ToolStripSeparator());
        contextMenu.Items.Add("Show Current Dell Charge Setting", null, async (_, _) => await _viewModel.RefreshDellChargeAsync());
        contextMenu.Items.Add("Screen Dimmer", null, (_, _) => ShowDimmer());
        contextMenu.Items.Add("Settings", null, (_, _) => ShowSettings());
        contextMenu.Items.Add(new ToolStripSeparator());
        contextMenu.Items.Add("Exit", null, (_, _) => ExitApplication());

        var icon = new NotifyIcon
        {
            Icon = _appIcon,
            Text = AppDisplayName,
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

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        if (e.PropertyName is nameof(MainViewModel.Battery)
            or nameof(MainViewModel.BatteryPowerWatts)
            or nameof(MainViewModel.CpuTemperatureCelsius)
            or nameof(MainViewModel.PowerModeText)
            or nameof(MainViewModel.HwinfoChipText))
        {
            UpdateTrayStatus();
        }
    }

    private void UpdateTrayStatus()
    {
        try
        {
            TrayState state = GetTrayState();
            _notifyIcon.Icon = _appIcon;
            _notifyIcon.Text = TruncateTooltip(BuildTrayTooltip(state), 63);
        }
        catch (Exception ex)
        {
            LogService.Error(ex, "Failed to update tray icon state.");
        }
    }

    private TrayState GetTrayState()
    {
        BatteryStatus battery = _viewModel.Battery;
        if (battery.HealthStatus is "Unavailable" or "Unknown" || battery.Percentage <= 0)
        {
            return new TrayState("Unavailable");
        }

        if (battery.IsCritical || battery.Percentage <= 10 || _viewModel.CpuTemperatureCelsius is >= 90)
        {
            return new TrayState("Critical");
        }

        if (battery.IsPowerSave || battery.Percentage <= 20 || _viewModel.CpuTemperatureCelsius is >= 80)
        {
            return new TrayState("Power save");
        }

        if (_viewModel.BatteryPowerWatts is > 0.5)
        {
            return new TrayState("Charging");
        }

        if (battery.IsPluggedIn)
        {
            return new TrayState("Hold");
        }

        return new TrayState("On battery");
    }

    private string BuildTrayTooltip(TrayState state)
    {
        string watts = _viewModel.BatteryPowerWatts is { } batteryWatts ? $"{batteryWatts:N1}W" : "--W";
        string temp = _viewModel.CpuTemperatureCelsius is { } cpuTemp ? $"{cpuTemp:N0}C" : "--C";
        return $"{AppDisplayName} { _viewModel.BatteryPercentText } {state.Label} | {watts} | CPU {temp} | {_viewModel.PowerModeText}";
    }

    private static string TruncateTooltip(string text, int maxLength) =>
        text.Length <= maxLength ? text : text[..Math.Max(0, maxLength - 1)] + "…";

    private static Icon LoadTrayIcon()
    {
        try
        {
            StreamResourceInfo? resource = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/AppIcon.ico", UriKind.Absolute));
            if (resource?.Stream is null)
            {
                return SystemIcons.Application;
            }

            using Stream stream = resource.Stream;
            return new Icon(stream);
        }
        catch (Exception ex)
        {
            LogService.Error(ex, "Failed to load tray icon.");
            return SystemIcons.Application;
        }
    }

    private void ApplyPreset(BatteryPreset preset)
    {
        Application.Current.Dispatcher.Invoke(() => _viewModel.ApplyBatteryPresetCommand.Execute(preset));
        _notifyIcon.ShowBalloonTip(2500, AppDisplayName, "Battery mode command started.", ToolTipIcon.Info);
    }

    private void ApplyWindowsPowerMode(WindowsPowerMode mode)
    {
        Application.Current.Dispatcher.Invoke(() => _viewModel.ApplyWindowsPowerModeCommand.Execute(mode));
        _notifyIcon.ShowBalloonTip(2000, AppDisplayName, "Windows power mode updated.", ToolTipIcon.Info);
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

    private sealed record TrayState(string Label);
}
