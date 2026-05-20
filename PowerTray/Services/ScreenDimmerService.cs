using System.Management;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using WinForms = System.Windows.Forms;

namespace PowerTray.Services;

public sealed class ScreenDimmerService : IDisposable
{
    private const double MaximumDimLevel = 90;
    private const double DimStep = 5;
    private const int BrightnessMinimumThreshold = 10;
    private const int WmInput = 0x00FF;
    private const int WmHotKey = 0x0312;
    private const int VkDown = 0x28;
    private const int VkUp = 0x26;
    private const int VkF7 = 0x76;
    private const int VkF8 = 0x77;
    private const int ModAlt = 0x0001;
    private const int ModControl = 0x0002;
    private const int HotKeyDimmerDown = 5103;
    private const int HotKeyDimmerUp = 5104;
    private const int HotKeyDimmerF7Down = 5105;
    private const int HotKeyDimmerF8Up = 5106;
    private const int RidInput = 0x10000003;
    private const int RidevInputSink = 0x00000100;
    private const ushort HidUsagePageConsumer = 0x000C;
    private const ushort HidUsageConsumerControl = 0x0001;
    private const ushort HidUsageBrightnessIncrement = 0x006F;
    private const ushort HidUsageBrightnessDecrement = 0x0070;
    private const int GwlExStyle = -20;
    private const int WsExTransparent = 0x00000020;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExNoActivate = 0x08000000;
    private static readonly nint HwndTopmost = new(-1);
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpShowWindow = 0x0040;

    private readonly SettingsService _settingsService;
    private readonly List<DimmerOverlayWindow> _overlays = [];
    private readonly DispatcherTimer _topmostTimer;
    private HwndSource? _rawInputSource;
    private bool _rawInputRegistered;
    private bool _disposed;

    public ScreenDimmerService(SettingsService settingsService)
    {
        _settingsService = settingsService;
        _topmostTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(400)
        };
        _topmostTimer.Tick += TopmostTimer_Tick;
        SystemEvents.DisplaySettingsChanged += SystemEvents_DisplaySettingsChanged;
    }

    public event EventHandler? StateChanged;

    public bool IsFeatureEnabled => _settingsService.Current.EnableScreenDimmer;
    public bool ExtendBrightnessKeys => _settingsService.Current.ExtendBrightnessKeysWithDimmer;
    public double DimLevel => Math.Clamp(_settingsService.Current.ScreenDimmerLevel, 0, MaximumDimLevel);
    public bool IsDimming => IsFeatureEnabled && DimLevel > 0.1;
    public string StatusText => IsFeatureEnabled
        ? IsDimming
            ? $"Screen dimmed by {DimLevel:N0}% | Overlay active."
            : "Screen dimmer is ready."
        : "Screen dimmer is disabled in Settings.";

    public void ApplySettings()
    {
        _settingsService.Current.ScreenDimmerLevel = Math.Clamp(_settingsService.Current.ScreenDimmerLevel, 0, MaximumDimLevel);
        UpdateDimming();
        UpdateKeyboardHook();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetDimLevel(double level)
    {
        if (!IsFeatureEnabled)
        {
            return;
        }

        double next = Math.Clamp(Math.Round(level), 0, MaximumDimLevel);
        if (Math.Abs(_settingsService.Current.ScreenDimmerLevel - next) < 0.5)
        {
            return;
        }

        _settingsService.Current.ScreenDimmerLevel = next;
        SaveAndApply();
    }

    public void SetExtendBrightnessKeys(bool value)
    {
        if (_settingsService.Current.ExtendBrightnessKeysWithDimmer == value)
        {
            return;
        }

        _settingsService.Current.ExtendBrightnessKeysWithDimmer = value;
        SaveAndApply();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        SystemEvents.DisplaySettingsChanged -= SystemEvents_DisplaySettingsChanged;
        _topmostTimer.Stop();
        DisposeRawInputSource();
        CloseOverlays();
    }

    private void SaveAndApply()
    {
        _settingsService.Save(_settingsService.Current);
        ApplySettings();
    }

    private void SystemEvents_DisplaySettingsChanged(object? sender, EventArgs e) =>
        System.Windows.Application.Current.Dispatcher.BeginInvoke(UpdateDimming);

    private void UpdateDimming()
    {
        if (_disposed)
        {
            return;
        }

        if (!IsDimming)
        {
            _topmostTimer.Stop();
            CloseOverlays();
            return;
        }

        UpdateOverlay(DimLevel);
    }

    private void UpdateOverlay(double overlayLevel)
    {
        WinForms.Screen[] screens = WinForms.Screen.AllScreens;
        if (_overlays.Count == screens.Length)
        {
            for (int i = 0; i < _overlays.Count; i++)
            {
                _overlays[i].UpdateDimLevel(overlayLevel / 100d);
            }

            StartDimmingMaintenanceTimer();
            return;
        }

        CloseOverlays();
        foreach (WinForms.Screen screen in screens)
        {
            var window = new DimmerOverlayWindow(screen, overlayLevel / 100d);
            _overlays.Add(window);
            window.Show();
        }

        StartDimmingMaintenanceTimer();
    }

    private void CloseOverlays()
    {
        foreach (DimmerOverlayWindow overlay in _overlays.ToArray())
        {
            overlay.Close();
        }

        _overlays.Clear();
    }

    private void StartDimmingMaintenanceTimer()
    {
        MaintainActiveDimming();
        if (!_topmostTimer.IsEnabled)
        {
            _topmostTimer.Start();
        }
    }

    private void TopmostTimer_Tick(object? sender, EventArgs e)
    {
        if (!IsDimming)
        {
            _topmostTimer.Stop();
            return;
        }

        MaintainActiveDimming();
    }

    private void MaintainActiveDimming()
    {
        ReassertOverlayTopmost();
    }

    private void ReassertOverlayTopmost()
    {
        foreach (DimmerOverlayWindow overlay in _overlays)
        {
            overlay.ReassertTopmost();
        }
    }

    private void UpdateKeyboardHook()
    {
        if (IsFeatureEnabled && ExtendBrightnessKeys)
        {
            EnsureRawInputSource();
        }
        else
        {
            DisposeRawInputSource();
        }
    }

    private void EnsureRawInputSource()
    {
        if (_rawInputSource is not null)
        {
            return;
        }

        var parameters = new HwndSourceParameters("PowerTrayScreenDimmerRawInput")
        {
            Width = 0,
            Height = 0,
            WindowStyle = unchecked((int)0x80000000)
        };

        _rawInputSource = new HwndSource(parameters);
        _rawInputSource.AddHook(RawInputWindowProc);
        RegisterRawInput(_rawInputSource.Handle);
        RegisterBrightnessHotKeys(_rawInputSource.Handle);
    }

    private void DisposeRawInputSource()
    {
        if (_rawInputSource is null)
        {
            _rawInputRegistered = false;
            return;
        }

        _rawInputSource.RemoveHook(RawInputWindowProc);
        UnregisterBrightnessHotKeys(_rawInputSource.Handle);
        _rawInputSource.Dispose();
        _rawInputSource = null;
        _rawInputRegistered = false;
    }

    private void RegisterRawInput(nint handle)
    {
        var device = new RawInputDevice
        {
            UsagePage = HidUsagePageConsumer,
            Usage = HidUsageConsumerControl,
            Flags = RidevInputSink,
            Target = handle
        };

        if (RegisterRawInputDevices([device], 1, Marshal.SizeOf<RawInputDevice>()))
        {
            _rawInputRegistered = true;
            return;
        }

        _rawInputRegistered = false;
        LogService.Error(new InvalidOperationException($"RegisterRawInputDevices failed: {Marshal.GetLastWin32Error()}"), "Failed to register raw input for brightness keys.");
    }

    private static void RegisterBrightnessHotKeys(nint handle)
    {
        RegisterBrightnessHotKey(handle, HotKeyDimmerDown, ModAlt | ModControl, VkDown, "Ctrl+Alt+Down");
        RegisterBrightnessHotKey(handle, HotKeyDimmerUp, ModAlt | ModControl, VkUp, "Ctrl+Alt+Up");
        RegisterBrightnessHotKey(handle, HotKeyDimmerF7Down, ModControl, VkF7, "Ctrl+F7");
        RegisterBrightnessHotKey(handle, HotKeyDimmerF8Up, ModControl, VkF8, "Ctrl+F8");
    }

    private static void RegisterBrightnessHotKey(nint handle, int id, int modifiers, int key, string name)
    {
        if (!RegisterHotKey(handle, id, modifiers, key))
        {
            LogService.Error(new InvalidOperationException($"RegisterHotKey failed for {name}: {Marshal.GetLastWin32Error()}"), "Failed to register screen dimmer hotkey.");
        }
    }

    private static void UnregisterBrightnessHotKeys(nint handle)
    {
        UnregisterHotKey(handle, HotKeyDimmerDown);
        UnregisterHotKey(handle, HotKeyDimmerUp);
        UnregisterHotKey(handle, HotKeyDimmerF7Down);
        UnregisterHotKey(handle, HotKeyDimmerF8Up);
    }

    private nint RawInputWindowProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == WmInput && _rawInputRegistered)
        {
            ushort? usage = TryReadConsumerUsage(lParam);
            if (usage == HidUsageBrightnessDecrement && HandleBrightnessDown())
            {
                handled = true;
            }
            else if (usage == HidUsageBrightnessIncrement && HandleBrightnessUp())
            {
                handled = true;
            }
        }
        else if (msg == WmHotKey)
        {
            int hotKeyId = wParam.ToInt32();
            if ((hotKeyId is HotKeyDimmerDown or HotKeyDimmerF7Down) && DimDown())
            {
                handled = true;
            }
            else if ((hotKeyId is HotKeyDimmerUp or HotKeyDimmerF8Up) && DimUp())
            {
                handled = true;
            }
        }

        return 0;
    }

    private static ushort? TryReadConsumerUsage(nint rawInputHandle)
    {
        uint size = 0;
        uint headerSize = (uint)Marshal.SizeOf<RawInputHeader>();
        GetRawInputData(rawInputHandle, RidInput, nint.Zero, ref size, headerSize);
        if (size == 0)
        {
            return null;
        }

        byte[] buffer = new byte[size];
        uint read = GetRawInputData(rawInputHandle, RidInput, buffer, ref size, headerSize);
        if (read == 0 || read == uint.MaxValue || buffer.Length <= Marshal.SizeOf<RawInputHeader>() + 8)
        {
            return null;
        }

        int offset = Marshal.SizeOf<RawInputHeader>() + 8;
        for (int i = offset; i < buffer.Length - 1; i++)
        {
            ushort value = BitConverter.ToUInt16(buffer, i);
            if (value is HidUsageBrightnessIncrement or HidUsageBrightnessDecrement)
            {
                return value;
            }
        }

        return null;
    }

    private bool HandleBrightnessDown()
    {
        if (!IsFeatureEnabled || !ExtendBrightnessKeys)
        {
            return false;
        }

        if (DimLevel > 0.1 || TryGetHardwareBrightness(out int brightness) && brightness <= BrightnessMinimumThreshold)
        {
            return DimDown();
        }

        return false;
    }

    private bool HandleBrightnessUp()
    {
        if (!IsFeatureEnabled || !ExtendBrightnessKeys || DimLevel <= 0.1)
        {
            return false;
        }

        return DimUp();
    }

    private bool DimDown()
    {
        if (!IsFeatureEnabled || !ExtendBrightnessKeys || DimLevel >= MaximumDimLevel - 0.1)
        {
            return false;
        }

        System.Windows.Application.Current.Dispatcher.Invoke(() => SetDimLevel(DimLevel + DimStep));
        return true;
    }

    private bool DimUp()
    {
        if (!IsFeatureEnabled || !ExtendBrightnessKeys || DimLevel <= 0.1)
        {
            return false;
        }

        System.Windows.Application.Current.Dispatcher.Invoke(() => SetDimLevel(DimLevel - DimStep));
        return true;
    }

    private static bool TryGetHardwareBrightness(out int brightness)
    {
        brightness = 100;
        try
        {
            using var searcher = new ManagementObjectSearcher("root\\WMI", "SELECT CurrentBrightness FROM WmiMonitorBrightness");
            foreach (ManagementObject item in searcher.Get().Cast<ManagementObject>())
            {
                brightness = Convert.ToInt32(item["CurrentBrightness"]);
                return true;
            }
        }
        catch (Exception ex)
        {
            LogService.Error(ex, "Failed to read hardware brightness for screen dimmer key extension.");
        }

        return false;
    }

    private sealed class DimmerOverlayWindow : Window
    {
        public DimmerOverlayWindow(WinForms.Screen screen, double opacity)
        {
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            ShowActivated = false;
            Topmost = true;
            Focusable = false;
            IsHitTestVisible = false;
            Background = CreateBrush(opacity);
            Left = screen.Bounds.Left;
            Top = screen.Bounds.Top;
            Width = screen.Bounds.Width;
            Height = screen.Bounds.Height;
            SourceInitialized += OnSourceInitialized;
        }

        public void UpdateDimLevel(double opacity) => Background = CreateBrush(opacity);

        public void ReassertTopmost()
        {
            nint handle = new WindowInteropHelper(this).Handle;
            if (handle != 0)
            {
                SetWindowPos(handle, HwndTopmost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate | SwpShowWindow);
            }
        }

        protected override void OnActivated(EventArgs e)
        {
            base.OnActivated(e);
            Topmost = true;
        }

        private void OnSourceInitialized(object? sender, EventArgs e)
        {
            nint handle = new WindowInteropHelper(this).Handle;
            int style = GetWindowLong(handle, GwlExStyle);
            SetWindowLong(handle, GwlExStyle, style | WsExTransparent | WsExToolWindow | WsExNoActivate);
        }

        private static SolidColorBrush CreateBrush(double opacity) =>
            new(System.Windows.Media.Color.FromArgb((byte)Math.Clamp(opacity * 255, 0, 230), 0, 0, 0));
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RawInputDevice
    {
        public ushort UsagePage;
        public ushort Usage;
        public int Flags;
        public nint Target;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RawInputHeader
    {
        public int Type;
        public int Size;
        public nint Device;
        public nint WParam;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(nint hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(nint hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(nint hWnd, nint hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterRawInputDevices(RawInputDevice[] pRawInputDevices, uint uiNumDevices, int cbSize);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(nint hWnd, int id, int fsModifiers, int vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(nint hWnd, int id);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetRawInputData(nint hRawInput, uint uiCommand, nint pData, ref uint pcbSize, uint cbSizeHeader);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetRawInputData(nint hRawInput, uint uiCommand, byte[] pData, ref uint pcbSize, uint cbSizeHeader);

}
