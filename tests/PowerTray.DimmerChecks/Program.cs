using System.Collections;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using PowerTray.Services;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args is ["--inspect-process", var processId])
        {
            var expected = GetMonitorBounds();
            var actual = new List<NativeRect>();
            EnumWindows((hwnd, _) =>
            {
                GetWindowThreadProcessId(hwnd, out uint pid);
                if (pid == uint.Parse(processId) && IsWindowVisible(hwnd) &&
                    (GetWindowLong(hwnd, -20) & 0x20) != 0)
                {
                    Marshal.ThrowExceptionForHR(DwmGetWindowAttribute(hwnd, 9, out NativeRect rect, Marshal.SizeOf<NativeRect>()));
                    actual.Add(rect);
                    Console.WriteLine($"Live overlay: {rect}; DPI={GetDpiForWindow(hwnd)}");
                }
                return true;
            }, 0);
            if (actual.Count == 0)
            {
                Console.WriteLine("No visible dimmer overlays in this process; enable dimming before inspection.");
                return 2;
            }
            foreach (var rect in expected)
                Console.WriteLine($"{(actual.Contains(rect) ? "PASS" : "FAIL")} live monitor {rect}: exact overlay coverage");
            return expected.All(actual.Contains) && expected.Count == actual.Count ? 0 : 1;
        }
        if (args is ["--per-monitor"])
            SetThreadDpiAwarenessContext(new nint(-4));
        // Exercise the production service without loading or saving user settings.
        var app = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        if (args is ["--per-monitor"])
            SetThreadDpiAwarenessContext(new nint(-4));
        var settings = new SettingsService();
        settings.Current.EnableScreenDimmer = true;
        settings.Current.ScreenDimmerLevel = 1;
        settings.Current.ExtendBrightnessKeysWithDimmer = false;
        using var service = new ScreenDimmerService(settings);
        try
        {
            service.ApplySettings();
            Pump();
            var expected = GetMonitorBounds();
            var overlays = ((IEnumerable)typeof(ScreenDimmerService)
                .GetField("_overlays", BindingFlags.NonPublic | BindingFlags.Instance)!
                .GetValue(service)!).Cast<Window>().ToArray();
            var actual = overlays.Select(GetBounds).ToArray();
            foreach (var overlay in overlays)
                Console.WriteLine($"WPF size={overlay.Width}x{overlay.Height}; DPI={GetDpiForWindow(new WindowInteropHelper(overlay).Handle)}");
            int failures = 0;
            foreach (var rect in expected)
            {
                bool covered = actual.Contains(rect);
                Console.WriteLine($"{(covered ? "PASS" : "FAIL")} monitor {rect}: exact overlay coverage");
                if (!covered) failures++;
            }
            foreach (var rect in actual) Console.WriteLine($"Actual overlay: {rect}");
            if (overlays.Length != expected.Count) failures++;

            // A DPI transition can resize an existing WPF window while the monitor
            // count remains unchanged. Drive the real display-change handler after
            // reproducing that stale rectangle; it must restore full coverage.
            overlays[0].Width *= 0.75;
            Pump();
            typeof(ScreenDimmerService).GetMethod("SystemEvents_DisplaySettingsChanged",
                BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(service, [null, EventArgs.Empty]);
            Pump();
            actual = overlays.Select(GetBounds).ToArray();
            bool restored = expected.All(actual.Contains);
            Console.WriteLine($"{(restored ? "PASS" : "FAIL")} display change with unchanged monitor count restores coverage");
            if (!restored) failures++;

            overlays[0].Width *= 0.75;
            Pump();
            typeof(ScreenDimmerService).GetMethod("TopmostTimer_Tick",
                BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(service, [null, EventArgs.Empty]);
            Pump();
            restored = expected.All(overlays.Select(GetBounds).Contains);
            Console.WriteLine($"{(restored ? "PASS" : "FAIL")} maintenance restores coverage after a delayed resize");
            if (!restored) failures++;

            settings.Current.ScreenDimmerLevel = 5;
            service.ApplySettings();
            Pump();
            bool behaviorPreserved = overlays.All(window =>
                (GetWindowLong(new WindowInteropHelper(window).Handle, -20) & 0x080000A0) == 0x080000A0 &&
                ((System.Windows.Media.SolidColorBrush)window.Background).Color.A == (byte)(0.05 * 255));
            Console.WriteLine($"{(behaviorPreserved ? "PASS" : "FAIL")} opacity updates preserve click-through and non-activation styles");
            if (!behaviorPreserved) failures++;

            settings.Current.ScreenDimmerLevel = 0;
            service.ApplySettings();
            bool closed = overlays.All(window => !window.IsVisible);
            Console.WriteLine($"{(closed ? "PASS" : "FAIL")} turning dimming off closes all overlays");
            if (!closed) failures++;
            return failures == 0 ? 0 : 1;
        }
        finally
        {
            service.Dispose();
            app.Shutdown();
        }
    }

    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

    private static List<NativeRect> GetMonitorBounds()
    {
        var result = new List<NativeRect>();
        nint previous = SetThreadDpiAwarenessContext(new nint(-4));
        try
        {
            EnumDisplayMonitors(0, 0, (nint monitor, nint dc, ref NativeRect rect, nint data) =>
            {
                result.Add(rect);
                return true;
            }, 0);
        }
        finally { SetThreadDpiAwarenessContext(previous); }
        return result;
    }

    private static NativeRect GetBounds(Window window)
    {
        int error = DwmGetWindowAttribute(new WindowInteropHelper(window).Handle, 9,
            out NativeRect rect, Marshal.SizeOf<NativeRect>());
        Marshal.ThrowExceptionForHR(error);
        return rect;
    }

    [StructLayout(LayoutKind.Sequential)]
    private record struct NativeRect(int Left, int Top, int Right, int Bottom);
    private delegate bool MonitorCallback(nint monitor, nint dc, ref NativeRect rect, nint data);
    private delegate bool WindowCallback(nint hwnd, nint data);
    [DllImport("user32.dll")]
    private static extern bool EnumWindows(WindowCallback callback, nint data);
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint hwnd, out uint processId);
    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32.dll")]
    private static extern int GetWindowLong(nint hwnd, int index);
    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint hwnd);
    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(nint dc, nint clip, MonitorCallback callback, nint data);
    [DllImport("user32.dll")]
    private static extern nint SetThreadDpiAwarenessContext(nint context);
    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(nint hwnd, int attribute, out NativeRect value, int size);
}
