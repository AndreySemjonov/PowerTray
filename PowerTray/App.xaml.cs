using System.Windows;
using PowerTray.Services;
using PowerTray.ViewModels;
using PowerTray.Views;

namespace PowerTray;

public partial class App : System.Windows.Application
{
    private const string SingleInstanceMutexName = @"Local\PowerTray.SingleInstance";
    private const string ShowDashboardEventName = @"Local\PowerTray.ShowDashboard";

    private TrayIconManager? _trayIconManager;
    private MainViewModel? _mainViewModel;
    private ScreenDimmerService? _screenDimmerService;
    private WindowsThemeAutomationService? _windowsThemeAutomationService;
    private Mutex? _singleInstanceMutex;
    private EventWaitHandle? _showDashboardEvent;
    private RegisteredWaitHandle? _showDashboardWaitHandle;

    protected override async void OnStartup(StartupEventArgs e)
    {
        if (e.Args.FirstOrDefault() == "--run-cctk")
        {
            int exitCode = await CctkService.RunElevatedCommandChildAsync(e.Args);
            Shutdown(exitCode);
            return;
        }

        bool forceMinimized = e.Args.Contains("--minimized", StringComparer.OrdinalIgnoreCase);
        if (!TryAcquireSingleInstance())
        {
            if (!forceMinimized)
            {
                SignalExistingInstance();
            }

            _singleInstanceMutex?.Dispose();
            _singleInstanceMutex = null;
            Shutdown();
            return;
        }

        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        base.OnStartup(e);

        var settingsService = new SettingsService();
        settingsService.Load();
        ThemeService.Apply(settingsService.Current.Theme);
        var startupService = new StartupService();
        RefreshStartupRegistration(startupService);
        var cctkService = new CctkService(settingsService);
        var batteryService = new BatteryService();
        var processStatsService = new ProcessStatsService();
        var sensorService = new SensorService(settingsService);
        var windowsPowerModeService = new WindowsPowerModeService();
        var batteryUsageService = new BatteryUsageService();
        var windowsBatteryUsageService = new ElevatedWindowsBatteryUsageService();
        _screenDimmerService = new ScreenDimmerService(settingsService);
        _screenDimmerService.ApplySettings();
        _windowsThemeAutomationService = new WindowsThemeAutomationService(settingsService);

        _mainViewModel = new MainViewModel(settingsService, cctkService, batteryService, sensorService, processStatsService, windowsPowerModeService, batteryUsageService, windowsBatteryUsageService, _screenDimmerService, _windowsThemeAutomationService);
        _trayIconManager = new TrayIconManager(_mainViewModel, () =>
        {
            var settingsViewModel = new SettingsViewModel(settingsService, startupService);
            var window = new SettingsWindow(settingsViewModel);
            settingsViewModel.Saved += (_, _) =>
            {
                ThemeService.Apply(settingsService.Current.Theme);
                _screenDimmerService.ApplySettings();
                _mainViewModel.ReloadSettings();
            };
            return window;
        }, () => new DimmerWindow(new DimmerViewModel(_screenDimmerService)), () =>
        {
            var viewModel = new WindowsThemeSettingsViewModel(settingsService, _windowsThemeAutomationService!);
            var window = new WindowsThemeSettingsWindow(viewModel);
            viewModel.Saved += (_, _) =>
            {
                ThemeService.Apply(settingsService.Current.Theme);
                _mainViewModel.ReloadSettings();
            };
            return window;
        });

        _trayIconManager.Show();
        _windowsThemeAutomationService.Start();
        RegisterSingleInstanceSignal();

        bool isBackgroundLaunch = forceMinimized || settingsService.Current.StartMinimized;
        _mainViewModel.Start(allowStartupElevation: !isBackgroundLaunch);

        if (!settingsService.Current.StartMinimized && !forceMinimized)
        {
            _trayIconManager.ShowDashboard();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _showDashboardWaitHandle?.Unregister(null);
        _showDashboardEvent?.Dispose();
        _trayIconManager?.Dispose();
        _screenDimmerService?.Dispose();
        _windowsThemeAutomationService?.Dispose();
        _singleInstanceMutex?.ReleaseMutex();
        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }

    private bool TryAcquireSingleInstance()
    {
        _singleInstanceMutex = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out bool createdNew);
        return createdNew;
    }

    private void RegisterSingleInstanceSignal()
    {
        _showDashboardEvent = new EventWaitHandle(initialState: false, EventResetMode.AutoReset, ShowDashboardEventName);
        _showDashboardWaitHandle = ThreadPool.RegisterWaitForSingleObject(
            _showDashboardEvent,
            (_, _) => Dispatcher.BeginInvoke(() => _trayIconManager?.ShowDashboard()),
            state: null,
            millisecondsTimeOutInterval: -1,
            executeOnlyOnce: false);
    }

    private static void SignalExistingInstance()
    {
        try
        {
            using EventWaitHandle showDashboardEvent = EventWaitHandle.OpenExisting(ShowDashboardEventName);
            showDashboardEvent.Set();
        }
        catch (WaitHandleCannotBeOpenedException)
        {
        }
        catch (Exception ex)
        {
            LogService.Error(ex, "Failed to signal existing PowerTray instance.");
        }
    }

    private static void RefreshStartupRegistration(StartupService startupService)
    {
        try
        {
            startupService.RefreshEnabledRegistration();
        }
        catch (Exception ex)
        {
            LogService.Error(ex, "Failed to refresh startup registration.");
        }
    }
}
