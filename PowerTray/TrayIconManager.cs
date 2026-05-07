using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Resources;
using XPSBatteryTray.Models;
using XPSBatteryTray.Services;
using XPSBatteryTray.ViewModels;
using XPSBatteryTray.Views;
using Application = System.Windows.Application;

namespace XPSBatteryTray;

public sealed class TrayIconManager : IDisposable
{
    private const string AppDisplayName = "PowerTray";

    private readonly MainViewModel _viewModel;
    private readonly Func<SettingsWindow> _settingsWindowFactory;
    private readonly NotifyIcon _notifyIcon;
    private Icon? _dynamicIcon;
    private DashboardWindow? _dashboardWindow;
    private SettingsWindow? _settingsWindow;
    private bool _disposed;

    public TrayIconManager(MainViewModel viewModel, Func<SettingsWindow> settingsWindowFactory)
    {
        _viewModel = viewModel;
        _settingsWindowFactory = settingsWindowFactory;
        _viewModel.OpenSettingsRequested += (_, _) => ShowSettings();
        _viewModel.PropertyChanged += ViewModel_PropertyChanged;
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
        _dynamicIcon?.Dispose();
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
        contextMenu.Items.Add("Settings", null, (_, _) => ShowSettings());
        contextMenu.Items.Add(new ToolStripSeparator());
        contextMenu.Items.Add("Exit", null, (_, _) => ExitApplication());

        var icon = new NotifyIcon
        {
            Icon = LoadTrayIcon(),
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
            Icon icon = CreateStateIcon(state);
            Icon? oldIcon = _dynamicIcon;
            _dynamicIcon = icon;
            _notifyIcon.Icon = icon;
            oldIcon?.Dispose();
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
            return new TrayState("!", Color.FromArgb(142, 148, 154), "Unavailable");
        }

        if (battery.IsCritical || battery.Percentage <= 10 || _viewModel.CpuTemperatureCelsius is >= 90)
        {
            return new TrayState("!", Color.FromArgb(214, 92, 83), "Critical");
        }

        if (battery.IsPowerSave || battery.Percentage <= 20 || _viewModel.CpuTemperatureCelsius is >= 80)
        {
            return new TrayState("S", Color.FromArgb(237, 184, 72), "Power save");
        }

        if (_viewModel.BatteryPowerWatts is > 0.5)
        {
            return new TrayState("+", Color.FromArgb(154, 215, 108), "Charging");
        }

        if (battery.IsPluggedIn)
        {
            return new TrayState("II", Color.FromArgb(84, 214, 198), "Hold");
        }

        return new TrayState("-", Color.FromArgb(88, 166, 255), "On battery");
    }

    private string BuildTrayTooltip(TrayState state)
    {
        string watts = _viewModel.BatteryPowerWatts is { } batteryWatts ? $"{batteryWatts:N1}W" : "--W";
        string temp = _viewModel.CpuTemperatureCelsius is { } cpuTemp ? $"{cpuTemp:N0}C" : "--C";
        return $"{AppDisplayName} { _viewModel.BatteryPercentText } {state.Label} | {watts} | CPU {temp} | {_viewModel.PowerModeText}";
    }

    private static string TruncateTooltip(string text, int maxLength) =>
        text.Length <= maxLength ? text : text[..Math.Max(0, maxLength - 1)] + "…";

    private static Icon CreateStateIcon(TrayState state)
    {
        using var bitmap = new Bitmap(32, 32);
        using Graphics graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(Color.Transparent);

        using var outlinePen = new Pen(Color.FromArgb(235, 242, 246, 252), 2.2f)
        {
            LineJoin = LineJoin.Round
        };
        using var fillBrush = new SolidBrush(state.Color);
        using var glyphBrush = new SolidBrush(Color.White);

        var body = new RectangleF(7, 7, 17, 20);
        graphics.DrawRoundedRectangle(outlinePen, body, 4);
        graphics.FillRectangle(fillBrush, 10, 18, 11, 6);
        graphics.FillRoundedRectangle(fillBrush, new RectangleF(24, 13, 4, 8), 2);

        using var font = new Font("Segoe UI", state.Glyph.Length > 1 ? 8.5f : 13f, System.Drawing.FontStyle.Bold, GraphicsUnit.Pixel);
        using var format = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center
        };
        graphics.DrawString(state.Glyph, font, glyphBrush, new RectangleF(7, 7, 17, 13), format);

        IntPtr handle = bitmap.GetHicon();
        try
        {
            using Icon temporary = Icon.FromHandle(handle);
            return (Icon)temporary.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

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

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    private sealed record TrayState(string Glyph, Color Color, string Label);
}

internal static class GraphicsExtensions
{
    public static void DrawRoundedRectangle(this Graphics graphics, Pen pen, RectangleF bounds, float radius)
    {
        using GraphicsPath path = CreateRoundedRectangle(bounds, radius);
        graphics.DrawPath(pen, path);
    }

    public static void FillRoundedRectangle(this Graphics graphics, Brush brush, RectangleF bounds, float radius)
    {
        using GraphicsPath path = CreateRoundedRectangle(bounds, radius);
        graphics.FillPath(brush, path);
    }

    private static GraphicsPath CreateRoundedRectangle(RectangleF bounds, float radius)
    {
        float diameter = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}
