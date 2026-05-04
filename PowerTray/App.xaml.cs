using System.Windows;
using XPSBatteryTray.Services;
using XPSBatteryTray.ViewModels;
using XPSBatteryTray.Views;

namespace XPSBatteryTray;

public partial class App : System.Windows.Application
{
    private TrayIconManager? _trayIconManager;
    private MainViewModel? _mainViewModel;

    protected override async void OnStartup(StartupEventArgs e)
    {
        if (e.Args.FirstOrDefault() == "--run-cctk")
        {
            int exitCode = await CctkService.RunElevatedCommandChildAsync(e.Args);
            Shutdown(exitCode);
            return;
        }

        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        base.OnStartup(e);

        var settingsService = new SettingsService();
        settingsService.Load();
        var startupService = new StartupService();
        var cctkService = new CctkService(settingsService);
        var batteryService = new BatteryService();
        var processStatsService = new ProcessStatsService();
        var sensorService = new SensorService(settingsService);
        var windowsPowerModeService = new WindowsPowerModeService();
        var batteryUsageService = new BatteryUsageService();

        _mainViewModel = new MainViewModel(settingsService, cctkService, batteryService, sensorService, processStatsService, windowsPowerModeService, batteryUsageService);
        _trayIconManager = new TrayIconManager(_mainViewModel, () =>
        {
            var settingsViewModel = new SettingsViewModel(settingsService, startupService);
            var window = new SettingsWindow(settingsViewModel);
            settingsViewModel.Saved += (_, _) => _mainViewModel.ReloadSettings();
            return window;
        });

        _trayIconManager.Show();
        _mainViewModel.Start();

        bool forceMinimized = e.Args.Contains("--minimized", StringComparer.OrdinalIgnoreCase);
        if (!settingsService.Current.StartMinimized && !forceMinimized)
        {
            _trayIconManager.ShowDashboard();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _trayIconManager?.Dispose();
        base.OnExit(e);
    }
}
