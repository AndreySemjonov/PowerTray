using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Windows.Threading;
using XPSBatteryTray.Models;
using XPSBatteryTray.Services;

namespace XPSBatteryTray.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly SettingsService _settingsService;
    private readonly CctkService _cctkService;
    private readonly BatteryService _batteryService;
    private readonly SensorService _sensorService;
    private readonly ProcessStatsService _processStatsService;
    private readonly WindowsPowerModeService _windowsPowerModeService;
    private readonly BatteryUsageService _batteryUsageService;
    private readonly DispatcherTimer _timer = new();
    private readonly List<SensorSample> _samples = [];
    private bool _isRefreshing;

    private BatteryStatus _battery = new();
    private string _dellChargeSetting = "Unknown";
    private string _statusMessage = "Ready";
    private string _hwinfoStatus = "Checking sensors...";
    private double _cpuUsagePercent;
    private double? _cpuTemperatureCelsius;
    private double? _cpuPackagePowerWatts;
    private double? _batteryPowerWatts;
    private SystemMemoryInfo _memory = new();
    private IReadOnlyList<double?> _cpuGraphValues = [];
    private IReadOnlyList<double?> _temperatureGraphValues = [];
    private IReadOnlyList<double?> _batteryWattsGraphValues = [];
    private IReadOnlyList<double?> _cpuPowerGraphValues = [];
    private IReadOnlyList<double?> _fanGraphValues = [];
    private string _cpuGraphSummary = "Cur -- | Avg -- | Min -- | Max --";
    private string _batteryWattsGraphSummary = "Cur -- | Avg -- | Min -- | Max --";
    private string _temperatureGraphSummary = "Cur -- | Avg -- | Min -- | Max --";
    private string _cpuPowerGraphSummary = "Cur -- | Avg -- | Min -- | Max --";
    private string _energyImpactTitle = "Energy Since Charge";
    private string _energyImpactColumnHeader = "est. mWh";
    private WindowsPowerMode? _currentWindowsPowerMode;
    private BatteryUsageSnapshot _batteryUsage = new();

    public MainViewModel(SettingsService settingsService, CctkService cctkService, BatteryService batteryService, SensorService sensorService, ProcessStatsService processStatsService, WindowsPowerModeService windowsPowerModeService, BatteryUsageService batteryUsageService)
    {
        _settingsService = settingsService;
        _cctkService = cctkService;
        _batteryService = batteryService;
        _sensorService = sensorService;
        _processStatsService = processStatsService;
        _windowsPowerModeService = windowsPowerModeService;
        _batteryUsageService = batteryUsageService;

        TopCpuProcesses = new ObservableCollection<ProcessUsageInfo>();
        TopMemoryProcesses = new ObservableCollection<ProcessUsageInfo>();
        EnergyImpactProcesses = new ObservableCollection<ProcessUsageInfo>();
        FanReadings = new ObservableCollection<string>();

        RefreshDellChargeCommand = new RelayCommand(async () => await RefreshDellChargeAsync());
        ApplyBatteryPresetCommand = new RelayCommand(async parameter => await ApplyBatteryPresetAsync(parameter));
        ApplyWindowsPowerModeCommand = new RelayCommand(parameter => ApplyWindowsPowerMode(parameter));
        OpenSettingsCommand = new RelayCommand(() => OpenSettingsRequested?.Invoke(this, EventArgs.Empty));

        ConfigureTimer();
    }

    public event EventHandler? OpenSettingsRequested;

    public BatteryStatus Battery
    {
        get => _battery;
        private set
        {
            if (SetProperty(ref _battery, value))
            {
                OnPropertyChanged(nameof(BatterySummary));
                OnPropertyChanged(nameof(BatteryPercentText));
                OnPropertyChanged(nameof(BatteryLevelPercent));
                OnPropertyChanged(nameof(PowerStateText));
                OnPropertyChanged(nameof(PowerStateChipText));
                OnPropertyChanged(nameof(BatteryTimeText));
                OnPropertyChanged(nameof(BatteryPowerText));
                OnPropertyChanged(nameof(PowerModeTargetText));
                OnPropertyChanged(nameof(PowerModeButtonToolTip));
                OnPropertyChanged(nameof(FooterStatusText));
            }
        }
    }

    public string DellChargeSetting
    {
        get => _dellChargeSetting;
        private set
        {
            if (SetProperty(ref _dellChargeSetting, value))
            {
                OnPropertyChanged(nameof(FriendlyChargeMode));
                OnPropertyChanged(nameof(ModeChipText));
            }
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public string HwinfoStatus
    {
        get => _hwinfoStatus;
        private set
        {
            if (SetProperty(ref _hwinfoStatus, value))
            {
                OnPropertyChanged(nameof(HwinfoChipText));
                OnPropertyChanged(nameof(FooterStatusText));
            }
        }
    }

    public double CpuUsagePercent
    {
        get => _cpuUsagePercent;
        private set
        {
            if (SetProperty(ref _cpuUsagePercent, value))
            {
                OnPropertyChanged(nameof(CpuUsageText));
                OnPropertyChanged(nameof(CpuGaugeValue));
            }
        }
    }

    public double? CpuTemperatureCelsius
    {
        get => _cpuTemperatureCelsius;
        private set
        {
            if (SetProperty(ref _cpuTemperatureCelsius, value))
            {
                OnPropertyChanged(nameof(CpuTemperatureText));
                OnPropertyChanged(nameof(CpuTemperatureDisplay));
            }
        }
    }

    public double? CpuPackagePowerWatts
    {
        get => _cpuPackagePowerWatts;
        private set
        {
            if (SetProperty(ref _cpuPackagePowerWatts, value))
            {
                OnPropertyChanged(nameof(CpuPowerText));
                OnPropertyChanged(nameof(CpuPackagePowerDisplay));
            }
        }
    }

    public double? BatteryPowerWatts
    {
        get => _batteryPowerWatts;
        private set
        {
            if (SetProperty(ref _batteryPowerWatts, value))
            {
                OnPropertyChanged(nameof(BatteryPowerText));
            }
        }
    }

    public SystemMemoryInfo Memory
    {
        get => _memory;
        private set
        {
            if (SetProperty(ref _memory, value))
            {
                OnPropertyChanged(nameof(MemoryText));
            }
        }
    }

    public IReadOnlyList<double?> CpuGraphValues
    {
        get => _cpuGraphValues;
        private set => SetProperty(ref _cpuGraphValues, value);
    }

    public IReadOnlyList<double?> TemperatureGraphValues
    {
        get => _temperatureGraphValues;
        private set => SetProperty(ref _temperatureGraphValues, value);
    }

    public IReadOnlyList<double?> BatteryWattsGraphValues
    {
        get => _batteryWattsGraphValues;
        private set => SetProperty(ref _batteryWattsGraphValues, value);
    }

    public IReadOnlyList<double?> CpuPowerGraphValues
    {
        get => _cpuPowerGraphValues;
        private set => SetProperty(ref _cpuPowerGraphValues, value);
    }

    public IReadOnlyList<double?> FanGraphValues
    {
        get => _fanGraphValues;
        private set => SetProperty(ref _fanGraphValues, value);
    }

    public string CpuGraphSummary
    {
        get => _cpuGraphSummary;
        private set => SetProperty(ref _cpuGraphSummary, value);
    }

    public string BatteryWattsGraphSummary
    {
        get => _batteryWattsGraphSummary;
        private set => SetProperty(ref _batteryWattsGraphSummary, value);
    }

    public string CpuPowerGraphSummary
    {
        get => _cpuPowerGraphSummary;
        private set => SetProperty(ref _cpuPowerGraphSummary, value);
    }

    public string TemperatureGraphSummary
    {
        get => _temperatureGraphSummary;
        private set => SetProperty(ref _temperatureGraphSummary, value);
    }

    public string EnergyImpactTitle
    {
        get => _energyImpactTitle;
        private set => SetProperty(ref _energyImpactTitle, value);
    }

    public string EnergyImpactColumnHeader
    {
        get => _energyImpactColumnHeader;
        private set => SetProperty(ref _energyImpactColumnHeader, value);
    }

    public WindowsPowerMode? CurrentWindowsPowerMode
    {
        get => _currentWindowsPowerMode;
        private set
        {
            if (SetProperty(ref _currentWindowsPowerMode, value))
            {
                OnPropertyChanged(nameof(PowerModeText));
                OnPropertyChanged(nameof(PowerModeButtonText));
                OnPropertyChanged(nameof(PowerModeButtonToolTip));
                OnPropertyChanged(nameof(PowerEfficiencyMenuText));
                OnPropertyChanged(nameof(BalancedPowerModeMenuText));
                OnPropertyChanged(nameof(PerformancePowerModeMenuText));
                OnPropertyChanged(nameof(FooterStatusText));
            }
        }
    }

    public BatteryUsageSnapshot BatteryUsage
    {
        get => _batteryUsage;
        private set
        {
            if (SetProperty(ref _batteryUsage, value))
            {
                OnPropertyChanged(nameof(BatteryUsageTitle));
                OnPropertyChanged(nameof(BatteryUsageSessionText));
                OnPropertyChanged(nameof(BatteryUsageActiveText));
                OnPropertyChanged(nameof(BatteryUsageIdleText));
                OnPropertyChanged(nameof(BatteryUsageEstimatedDrainText));
                OnPropertyChanged(nameof(BatteryUsageBuckets));
            }
        }
    }

    public ObservableCollection<ProcessUsageInfo> TopCpuProcesses { get; }
    public ObservableCollection<ProcessUsageInfo> TopMemoryProcesses { get; }
    public ObservableCollection<ProcessUsageInfo> EnergyImpactProcesses { get; }
    public ObservableCollection<string> FanReadings { get; }

    public ICommand RefreshDellChargeCommand { get; }
    public ICommand ApplyBatteryPresetCommand { get; }
    public ICommand ApplyWindowsPowerModeCommand { get; }
    public ICommand OpenSettingsCommand { get; }

    public string BatterySummary => Battery.Percentage > 0
        ? $"{Battery.Percentage}% - {(Battery.IsPluggedIn ? "Plugged in" : "On battery")}"
        : Battery.IsPluggedIn ? "Plugged in" : "Battery status unavailable";

    public string BatteryPercentText => Battery.Percentage > 0 ? $"{Battery.Percentage}%" : "--%";
    public double BatteryLevelPercent => Math.Clamp(Battery.Percentage, 0, 100);
    public string PowerStateText => Battery.IsPluggedIn ? "Plugged in" : "On battery";
    public string PowerStateChipText => PowerStateText;
    public string ModeChipText => FriendlyChargeMode.Replace("Mode: ", string.Empty);
    public string HwinfoChipText
    {
        get
        {
            if (HwinfoStatus.Contains("LibreHardwareMonitor", StringComparison.OrdinalIgnoreCase))
            {
                if (HwinfoStatus.Contains("active", StringComparison.OrdinalIgnoreCase))
                {
                    return "LHM OK";
                }

                return HwinfoStatus.Contains("partial", StringComparison.OrdinalIgnoreCase) ? "LHM partial" : "LHM unavailable";
            }

            if (HwinfoStatus.Contains("Windows fallback", StringComparison.OrdinalIgnoreCase))
            {
                return "Windows fallback";
            }

            return HwinfoStatus.Contains("detected", StringComparison.OrdinalIgnoreCase) ? "HWiNFO OK" : "HWiNFO unavailable";
        }
    }
    public string AdminChipText => IsAdministrator ? "Admin" : "User";
    public string CctkStatusText => _cctkService.IsConfigured ? "OK" : "missing";
    public string SampleIntervalText => $"Sample {_settingsService.Current.SensorSampleIntervalSeconds}s";
    public string PowerModeText => CurrentWindowsPowerMode is { } mode
        ? WindowsPowerModeService.ToDisplayName(mode)
        : "Unavailable";
    public string PowerModeTargetText => Battery.IsPluggedIn ? "plugged in" : "on battery";
    public string PowerModeButtonText => $"Power: {PowerModeText}";
    public string PowerModeButtonToolTip => $"Windows power mode for {PowerModeTargetText}: {PowerModeText}";
    public string PowerEfficiencyMenuText => FormatPowerModeMenuText(WindowsPowerMode.BestPowerEfficiency);
    public string BalancedPowerModeMenuText => FormatPowerModeMenuText(WindowsPowerMode.Balanced);
    public string PerformancePowerModeMenuText => FormatPowerModeMenuText(WindowsPowerMode.BestPerformance);
    public string FooterStatusText => $"{AdminChipText} | {HwinfoChipText} | {SampleIntervalText} | Window 10 min | power: {PowerModeText} | cctk: {CctkStatusText}";
    public string FriendlyChargeMode => FormatFriendlyChargeMode(DellChargeSetting);
    public string BatteryUsageTitle => BatteryUsage.Title;
    public string BatteryUsageSessionText => BatteryUsage.SessionText;
    public string BatteryUsageActiveText => BatteryUsage.ActiveText;
    public string BatteryUsageIdleText => BatteryUsage.IdleText;
    public string BatteryUsageEstimatedDrainText => BatteryUsage.EstimatedDrainText;
    public IReadOnlyList<BatteryUsageBucket> BatteryUsageBuckets => BatteryUsage.Buckets;
    public string TopAppUsageEmptyText => EnergyImpactProcesses.Count == 0 ? "No app usage data yet" : string.Empty;

    public string BatteryTimeText => Battery.EstimatedTimeRemaining is { } remaining
        ? $"{remaining.Hours + remaining.Days * 24}h {remaining.Minutes}m remaining"
        : "Time remaining unavailable";

    public string BatteryPowerText => BatteryPowerWatts is { } watts
        ? $"{watts:N1} W"
        : "Watts unavailable";

    public string CpuUsageText => $"{CpuUsagePercent:N1}%";
    public double CpuGaugeValue => Math.Clamp(CpuUsagePercent, 0, 100);
    public string CpuTemperatureText => CpuTemperatureCelsius is { } value ? $"{value:N0} C" : "Temp unavailable";
    public string CpuTemperatureDisplay => CpuTemperatureCelsius is { } value ? $"{value:N0} °C" : "-- °C";
    public string CpuPowerText => CpuPackagePowerWatts is { } value ? $"{value:N1} W" : "Power unavailable";
    public string CpuPackagePowerDisplay => CpuPackagePowerWatts is { } value ? $"{value:N1} W pkg" : "-- W pkg";
    public string TopCpuProcessText => TopCpuProcesses.FirstOrDefault() is { } process
        ? $"Top proc: {process.Name} {process.CpuPercent:N1}%"
        : "Top proc: --";
    public string MemoryText => $"{Memory.UsedText} / {Memory.TotalText} ({Memory.UsedPercent:N0}%)";
    public bool IsAdministrator => CctkService.IsAdministrator();

    public void Start()
    {
        _ = RefreshDellChargeAsync();
        _ = RefreshAsync();
        _timer.Start();
    }

    public void ReloadSettings()
    {
        ConfigureTimer();
        StatusMessage = "Settings saved.";
    }

    public async Task RefreshDellChargeAsync()
    {
        CommandResult result = await _cctkService.ShowCurrentAsync();
        DellChargeSetting = result.Success ? CleanCctkOutput(result.StandardOutput) : "Unavailable";
        StatusMessage = result.Message;
    }

    private void ConfigureTimer()
    {
        _timer.Stop();
        _timer.Interval = TimeSpan.FromSeconds(Math.Clamp(_settingsService.Current.SensorSampleIntervalSeconds, 1, 60));
        _timer.Tick -= OnTimerTick;
        _timer.Tick += OnTimerTick;
    }

    private async void OnTimerTick(object? sender, EventArgs e) => await RefreshAsync();

    private async Task ApplyBatteryPresetAsync(object? parameter)
    {
        if (parameter is not BatteryPreset preset)
        {
            if (parameter is string value && Enum.TryParse(value, out BatteryPreset parsed))
            {
                preset = parsed;
            }
            else
            {
                return;
            }
        }

        StatusMessage = CctkService.IsAdministrator()
            ? "Applying Dell battery setting..."
            : "Requesting administrator approval...";

        CommandResult result = await _cctkService.ApplyPresetAsync(preset);
        StatusMessage = result.Message;
        if (result.Success)
        {
            await RefreshDellChargeAsync();
        }
    }

    private void ApplyWindowsPowerMode(object? parameter)
    {
        if (parameter is not WindowsPowerMode mode)
        {
            if (parameter is string value && Enum.TryParse(value, out WindowsPowerMode parsed))
            {
                mode = parsed;
            }
            else
            {
                return;
            }
        }

        bool pluggedIn = Battery.IsPluggedIn;
        try
        {
            StatusMessage = _windowsPowerModeService.SetConfiguredMode(pluggedIn, mode);
            RefreshWindowsPowerMode(pluggedIn);
        }
        catch (Exception ex)
        {
            LogService.Error(ex, "Failed to apply Windows power mode.");
            StatusMessage = ex.Message;
        }
    }

    private Task RefreshAsync()
    {
        if (_isRefreshing)
        {
            return Task.CompletedTask;
        }

        _isRefreshing = true;
        try
        {
            SensorReadings sensors = _sensorService.Read();
            HwinfoStatus = sensors.Status;
            BatteryStatus battery = _batteryService.GetStatus(sensors.BatteryPowerWatts);
            (double overallCpu,
                IReadOnlyList<ProcessUsageInfo> topCpu,
                IReadOnlyList<ProcessUsageInfo> topMemory,
                IReadOnlyList<ProcessUsageInfo> energyImpact,
                string energyImpactTitle,
                string energyImpactColumnHeader) = _processStatsService.Sample(battery);

            CpuUsagePercent = overallCpu;
            CpuTemperatureCelsius = sensors.CpuTemperatureCelsius;
            CpuPackagePowerWatts = sensors.CpuPackagePowerWatts;
            Battery = battery;
            BatteryPowerWatts = Battery.ChargeRateWatts;
            RefreshWindowsPowerMode(Battery.IsPluggedIn);
            BatteryUsage = _batteryUsageService.Record(Battery);
            Memory = _processStatsService.GetMemoryInfo();
            EnergyImpactTitle = energyImpactTitle;
            EnergyImpactColumnHeader = "% used";

            Replace(TopCpuProcesses, topCpu);
            Replace(TopMemoryProcesses, topMemory);
            Replace(EnergyImpactProcesses, energyImpact);
            OnPropertyChanged(nameof(TopAppUsageEmptyText));
            OnPropertyChanged(nameof(TopCpuProcessText));
            Replace(FanReadings, sensors.FanRpm.Count == 0
                ? ["Fan RPM unavailable"]
                : sensors.FanRpm.Select(f => $"{f.Key}: {f.Value:N0} RPM"));

            AddSample(new SensorSample
            {
                Timestamp = DateTimeOffset.Now,
                CpuUsagePercent = overallCpu,
                CpuTemperatureCelsius = sensors.CpuTemperatureCelsius,
                CpuPackagePowerWatts = sensors.CpuPackagePowerWatts,
                BatteryPowerWatts = Battery.ChargeRateWatts,
                FanRpm = sensors.FanRpm.Values.FirstOrDefault()
            });
        }
        finally
        {
            _isRefreshing = false;
        }

        return Task.CompletedTask;
    }

    private void AddSample(SensorSample sample)
    {
        _samples.Add(sample);
        DateTimeOffset cutoff = DateTimeOffset.Now.AddMinutes(-10);
        _samples.RemoveAll(s => s.Timestamp < cutoff);

        CpuGraphValues = _samples.Select(s => (double?)s.CpuUsagePercent).ToArray();
        TemperatureGraphValues = _samples.Select(s => s.CpuTemperatureCelsius).ToArray();
        BatteryWattsGraphValues = _samples.Select(s => s.BatteryPowerWatts).ToArray();
        CpuPowerGraphValues = _samples.Select(s => s.CpuPackagePowerWatts).ToArray();
        FanGraphValues = _samples.Select(s => s.FanRpm).ToArray();
        CpuGraphSummary = FormatGraphSummary(CpuGraphValues, "N1", "%");
        BatteryWattsGraphSummary = FormatGraphSummary(BatteryWattsGraphValues, "N1", " W");
        CpuPowerGraphSummary = FormatGraphSummary(CpuPowerGraphValues, "N1", " W");
        TemperatureGraphSummary = FormatGraphSummary(TemperatureGraphValues, "N0", " C");
    }

    private void RefreshWindowsPowerMode(bool pluggedIn)
    {
        try
        {
            CurrentWindowsPowerMode = _windowsPowerModeService.GetConfiguredMode(pluggedIn);
        }
        catch (Exception ex)
        {
            LogService.Error(ex, "Failed to read Windows power mode.");
            CurrentWindowsPowerMode = null;
        }
    }

    private static void Replace<T>(ObservableCollection<T> collection, IEnumerable<T> values)
    {
        collection.Clear();
        foreach (T value in values)
        {
            collection.Add(value);
        }
    }

    private static string CleanCctkOutput(string output)
    {
        string cleaned = output.Trim();
        return string.IsNullOrWhiteSpace(cleaned) ? "No output from cctk.exe" : cleaned;
    }

    private static string FormatGraphSummary(IEnumerable<double?> values, string format, string unit)
    {
        double[] visible = values.Where(v => v.HasValue).Select(v => v!.Value).TakeLast(120).ToArray();
        if (visible.Length == 0)
        {
            return "Cur -- | Avg -- | Min -- | Max --";
        }

        string current = visible[^1].ToString(format);
        string avg = visible.Average().ToString(format);
        string min = visible.Min().ToString(format);
        string max = visible.Max().ToString(format);
        return $"Cur {current}{unit} | Avg {avg}{unit} | Min {min}{unit} | Max {max}{unit}";
    }

    private static string FormatFriendlyChargeMode(string raw)
    {
        if (raw.Contains("Custom:50-80", StringComparison.OrdinalIgnoreCase))
        {
            return "Mode: Battery Health (50-80)";
        }

        if (raw.Contains("Custom:70-90", StringComparison.OrdinalIgnoreCase))
        {
            return "Mode: Balanced (70-90)";
        }

        if (raw.Contains("Standard", StringComparison.OrdinalIgnoreCase))
        {
            return "Mode: Standard";
        }

        if (raw.Contains("PrimAcUse", StringComparison.OrdinalIgnoreCase))
        {
            return "Mode: Primarily AC Use";
        }

        if (raw.Contains("Adaptive", StringComparison.OrdinalIgnoreCase))
        {
            return "Mode: Adaptive";
        }

        if (raw.Equals("Unavailable", StringComparison.OrdinalIgnoreCase) || raw.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
        {
            return "Mode: Unavailable";
        }

        return "Mode: Dell custom";
    }

    private string FormatPowerModeMenuText(WindowsPowerMode mode)
    {
        string prefix = CurrentWindowsPowerMode == mode ? "✓ " : string.Empty;
        return prefix + WindowsPowerModeService.ToDisplayName(mode);
    }
}
