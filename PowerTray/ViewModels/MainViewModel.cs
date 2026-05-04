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
    private bool _isUsageDetailsVisible;
    private TimeSpan? _averageBatteryTimeRemaining;
    private double? _averageBatteryDischargeWatts;

    private BatteryStatus _battery = new();
    private string _dellChargeSetting = "Unknown";
    private string _statusMessage = "Ready";
    private string _hwinfoStatus = "Checking sensors...";
    private double _cpuUsagePercent;
    private double? _gpuUsagePercent;
    private double? _cpuTemperatureCelsius;
    private double? _cpuPackagePowerWatts;
    private double? _batteryPowerWatts;
    private SystemMemoryInfo _memory = new();
    private IReadOnlyList<double?> _cpuGraphValues = [];
    private IReadOnlyList<double?> _gpuGraphValues = [];
    private IReadOnlyList<double?> _temperatureGraphValues = [];
    private IReadOnlyList<double?> _batteryWattsGraphValues = [];
    private IReadOnlyList<double?> _cpuPowerGraphValues = [];
    private IReadOnlyList<double?> _fanGraphValues = [];
    private IReadOnlyList<UsagePeakInfo> _cpuUsagePeaks = [];
    private IReadOnlyList<UsagePeakInfo> _gpuUsagePeaks = [];
    private string _cpuGraphSummary = "Cur -- | Avg -- | Min -- | Max --";
    private string _systemUsageGraphSummary = "CPU cur -- | GPU cur -- | CPU avg -- | GPU avg --";
    private string _batteryWattsGraphSummary = "Cur -- | Avg -- | Min -- | Max --";
    private string _temperatureGraphSummary = "Cur -- | Avg -- | Min -- | Max --";
    private string _cpuPowerGraphSummary = "Cur -- | Avg -- | Min -- | Max --";
    private string _energyImpactTitle = "Energy Since Charge";
    private string _energyImpactColumnHeader = "est. mWh";
    private WindowsPowerMode? _currentWindowsPowerMode;
    private BatteryUsageSnapshot _batteryUsage = new();
    private DateTime _selectedBatteryUsageDate = DateTime.Today;

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
        ShowUsageDetailsCommand = new RelayCommand(() => IsUsageDetailsVisible = true);
        HideUsageDetailsCommand = new RelayCommand(() => IsUsageDetailsVisible = false);
        PreviousBatteryUsageDayCommand = new RelayCommand(ShowPreviousBatteryUsageDay);
        NextBatteryUsageDayCommand = new RelayCommand(ShowNextBatteryUsageDay);
        TodayBatteryUsageCommand = new RelayCommand(ShowTodayBatteryUsage);

        ConfigureTimer();
    }

    public event EventHandler? OpenSettingsRequested;

    public bool IsUsageDetailsVisible
    {
        get => _isUsageDetailsVisible;
        private set => SetProperty(ref _isUsageDetailsVisible, value);
    }

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
                OnPropertyChanged(nameof(BatteryFlowChipText));
                OnPropertyChanged(nameof(BatteryTimeText));
                OnPropertyChanged(nameof(BatteryPowerText));
                OnPropertyChanged(nameof(BatteryHealthText));
                OnPropertyChanged(nameof(BatteryCycleText));
                OnPropertyChanged(nameof(BatteryHealthSummaryText));
                OnPropertyChanged(nameof(BatteryCapacityText));
                OnPropertyChanged(nameof(BatteryHealthToolTip));
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
                OnPropertyChanged(nameof(CpuNowDetailText));
            }
        }
    }

    public double? GpuUsagePercent
    {
        get => _gpuUsagePercent;
        private set
        {
            if (SetProperty(ref _gpuUsagePercent, value))
            {
                OnPropertyChanged(nameof(GpuUsageText));
                OnPropertyChanged(nameof(GpuNowDetailText));
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
                OnPropertyChanged(nameof(BatteryDrainSummaryText));
                OnPropertyChanged(nameof(BatteryFlowChipText));
                OnPropertyChanged(nameof(BatteryTimeText));
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

    public IReadOnlyList<double?> GpuGraphValues
    {
        get => _gpuGraphValues;
        private set => SetProperty(ref _gpuGraphValues, value);
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

    public IReadOnlyList<UsagePeakInfo> CpuUsagePeaks
    {
        get => _cpuUsagePeaks;
        private set => SetProperty(ref _cpuUsagePeaks, value);
    }

    public IReadOnlyList<UsagePeakInfo> GpuUsagePeaks
    {
        get => _gpuUsagePeaks;
        private set => SetProperty(ref _gpuUsagePeaks, value);
    }

    public string CpuGraphSummary
    {
        get => _cpuGraphSummary;
        private set => SetProperty(ref _cpuGraphSummary, value);
    }

    public string SystemUsageGraphSummary
    {
        get => _systemUsageGraphSummary;
        private set
        {
            if (SetProperty(ref _systemUsageGraphSummary, value))
            {
                OnPropertyChanged(nameof(SystemUsageGraphSummaryTop));
                OnPropertyChanged(nameof(SystemUsageGraphSummaryBottom));
            }
        }
    }

    public string BatteryWattsGraphSummary
    {
        get => _batteryWattsGraphSummary;
        private set
        {
            if (SetProperty(ref _batteryWattsGraphSummary, value))
            {
                OnPropertyChanged(nameof(BatteryWattsGraphSummaryTop));
                OnPropertyChanged(nameof(BatteryWattsGraphSummaryBottom));
            }
        }
    }

    public string CpuPowerGraphSummary
    {
        get => _cpuPowerGraphSummary;
        private set => SetProperty(ref _cpuPowerGraphSummary, value);
    }

    public string TemperatureGraphSummary
    {
        get => _temperatureGraphSummary;
        private set
        {
            if (SetProperty(ref _temperatureGraphSummary, value))
            {
                OnPropertyChanged(nameof(TemperatureGraphSummaryTop));
                OnPropertyChanged(nameof(TemperatureGraphSummaryBottom));
            }
        }
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
                OnPropertyChanged(nameof(PowerModeChipText));
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
                OnPropertyChanged(nameof(BatteryUsageSleepDrainText));
                OnPropertyChanged(nameof(BatteryUsageChargeBehaviorText));
                OnPropertyChanged(nameof(BatteryUsageBuckets));
                OnPropertyChanged(nameof(BatteryUsageDateText));
            }
        }
    }

    public DateTime SelectedBatteryUsageDate
    {
        get => _selectedBatteryUsageDate;
        private set
        {
            DateTime date = value.Date;
            if (date > DateTime.Today)
            {
                date = DateTime.Today;
            }

            if (SetProperty(ref _selectedBatteryUsageDate, date))
            {
                BatteryUsage = _batteryUsageService.GetSnapshot(_selectedBatteryUsageDate);
                OnPropertyChanged(nameof(BatteryUsageDateText));
                OnPropertyChanged(nameof(CanShowNextBatteryUsageDay));
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
    public ICommand ShowUsageDetailsCommand { get; }
    public ICommand HideUsageDetailsCommand { get; }
    public ICommand PreviousBatteryUsageDayCommand { get; }
    public ICommand NextBatteryUsageDayCommand { get; }
    public ICommand TodayBatteryUsageCommand { get; }

    public string BatterySummary => Battery.Percentage > 0
        ? $"{Battery.Percentage}% - {(Battery.IsPluggedIn ? "Plugged in" : "On battery")}"
        : Battery.IsPluggedIn ? "Plugged in" : "Battery status unavailable";

    public string BatteryPercentText => Battery.Percentage > 0 ? $"{Battery.Percentage}%" : "--%";
    public double BatteryLevelPercent => Math.Clamp(Battery.Percentage, 0, 100);
    public string PowerStateText => Battery.IsPluggedIn ? "Plugged in" : "On battery";
    public string PowerStateChipText => Battery.IsPluggedIn ? "AC" : "Battery";
    public string BatteryFlowChipText => BatteryPowerWatts switch
    {
        > 0.5 => "Charge",
        < -0.5 => "Drain",
        _ => Battery.IsPluggedIn ? "Hold" : "Idle"
    };
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

            if (HwinfoStatus.Contains("shared memory", StringComparison.OrdinalIgnoreCase))
            {
                return "HWiNFO no shared memory";
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
    public string PowerModeChipText => CurrentWindowsPowerMode switch
    {
        WindowsPowerMode.BestPowerEfficiency => "Efficiency",
        WindowsPowerMode.Balanced => "Balanced",
        WindowsPowerMode.BestPerformance => "Performance",
        _ => "Power --"
    };
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
    public string BatteryUsageSleepDrainText => BatteryUsage.SleepDrainText;
    public string BatteryUsageChargeBehaviorText => BatteryUsage.ChargeBehaviorText;
    public IReadOnlyList<BatteryUsageBucket> BatteryUsageBuckets => BatteryUsage.Buckets;
    public string BatteryWattsGraphSummaryTop => SplitGraphSummary(BatteryWattsGraphSummary, 0);
    public string BatteryWattsGraphSummaryBottom => SplitGraphSummary(BatteryWattsGraphSummary, 1);
    public string SystemUsageGraphSummaryTop => SplitGraphSummary(SystemUsageGraphSummary, 0);
    public string SystemUsageGraphSummaryBottom => SplitGraphSummary(SystemUsageGraphSummary, 1);
    public string TemperatureGraphSummaryTop => SplitGraphSummary(TemperatureGraphSummary, 0);
    public string TemperatureGraphSummaryBottom => SplitGraphSummary(TemperatureGraphSummary, 1);
    public string BatteryUsageDateText => SelectedBatteryUsageDate == DateTime.Today
        ? "Today"
        : SelectedBatteryUsageDate.ToString("MMM d");
    public bool CanShowNextBatteryUsageDay => SelectedBatteryUsageDate < DateTime.Today;
    public string TopAppUsageEmptyText => EnergyImpactProcesses.Count == 0 ? "No app usage data yet" : string.Empty;
    public string CpuNowDetailText => $"{CpuUsagePercent:N0}%";
    public string GpuNowDetailText => GpuUsagePercent is { } value ? $"{value:N0}%" : "--";
    public string CpuMaxDetailText => FormatMaxPercent(CpuGraphValues);
    public string GpuMaxDetailText => FormatMaxPercent(GpuGraphValues);
    public string DetailSamplingText => $"{_settingsService.Current.SensorSampleIntervalSeconds}s";
    public string CpuAverageDetailText => FormatAveragePercent(CpuGraphValues);
    public string GpuAverageDetailText => FormatAveragePercent(GpuGraphValues);
    public string CpuTimeAbove50Text => FormatTimeAbove(CpuGraphValues, 50, _settingsService.Current.SensorSampleIntervalSeconds);
    public string GpuTimeAbove50Text => FormatTimeAbove(GpuGraphValues, 50, _settingsService.Current.SensorSampleIntervalSeconds);
    public string CpuSamplesDetailText => FormatSampleCount(CpuGraphValues);
    public string GpuSamplesDetailText => FormatSampleCount(GpuGraphValues);

    public string BatteryTimeText
    {
        get
        {
            if (Battery.IsPluggedIn)
            {
                return BatteryPowerWatts switch
                {
                    > 0.5 => "Charging",
                    _ => "Plugged in"
                };
            }

            TimeSpan? remaining = _averageBatteryTimeRemaining ?? Battery.EstimatedTimeRemaining;
            return remaining is { } value
                ? $"{value.Hours + value.Days * 24}h {value.Minutes}m remaining"
                : "Time remaining unavailable";
        }
    }

    public string BatteryPowerText => BatteryPowerWatts is { } watts
        ? $"{watts:N1} W"
        : "Watts unavailable";
    public string BatteryDrainSummaryText
    {
        get
        {
            string current = BatteryPowerWatts is { } watts ? $"{watts:N1} W" : "-- W";
            string average = _averageBatteryDischargeWatts is { } avg ? $"-{avg:N1} W" : "-- W";
            string rate = CalculateBatteryPercentRateText(Battery, _averageBatteryDischargeWatts);
            return $"Cur: {current} | Avg: {average} | Rate: {rate}";
        }
    }

    public string BatteryHealthText => Battery.BatteryHealth.HealthPercent is { } health
        ? $"Health {health:N0}%"
        : "Health unavailable";

    public string BatteryCycleText => Battery.BatteryHealth.CycleCount is { } cycles
        ? $"Cycles {cycles:N0}"
        : "Cycles unavailable";

    public string BatteryHealthSummaryText
    {
        get
        {
            if (Battery.BatteryHealth.HealthPercent is { } health && Battery.BatteryHealth.CycleCount is { } cycles)
            {
                return $"Health {health:N0}% · {cycles:N0} cycles";
            }

            if (Battery.BatteryHealth.HealthPercent is { } healthOnly)
            {
                return $"Health {healthOnly:N0}%";
            }

            return Battery.BatteryHealth.CycleCount is { } cyclesOnly
                ? $"{cyclesOnly:N0} cycles"
                : "Health unavailable";
        }
    }

    public string BatteryCapacityText =>
        Battery.BatteryHealth.FullChargeCapacityMilliWattHours is { } fullCharge
        && Battery.BatteryHealth.DesignCapacityMilliWattHours is { } design
            ? $"{FormatCapacity(fullCharge)} / {FormatCapacity(design)}"
            : "Capacity unavailable";

    public string BatteryHealthToolTip =>
        $"{Battery.BatteryHealth.Source}\nFull charge: {FormatCapacity(Battery.BatteryHealth.FullChargeCapacityMilliWattHours)}\nDesign: {FormatCapacity(Battery.BatteryHealth.DesignCapacityMilliWattHours)}\n{BatteryCycleText}";

    public string CpuUsageText => $"{CpuUsagePercent:N1}%";
    public string GpuUsageText => GpuUsagePercent is { } value ? $"{value:N0}%" : "--%";
    public double CpuGaugeValue => Math.Clamp(CpuUsagePercent, 0, 100);
    public string CpuTemperatureText => CpuTemperatureCelsius is { } value ? $"{value:N0} C" : "Temp unavailable";
    public string CpuTemperatureDisplay => CpuTemperatureCelsius is { } value ? $"{value:N0} °C" : "-- °C";
    public string CpuPowerText => CpuPackagePowerWatts is { } value ? $"{value:N1} W" : "Power unavailable";
    public string CpuPackagePowerDisplay => CpuPackagePowerWatts is { } value ? $"{value:N1} W pkg" : "-- W pkg";
    public string TopCpuProcessText => TopCpuProcesses.FirstOrDefault() is { } process
        ? process.Name
        : "--";
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
        OnPropertyChanged(nameof(DetailSamplingText));
        NotifyUsageDetailMetricsChanged();
    }

    public async Task RefreshDellChargeAsync()
    {
        CommandResult result = await _cctkService.ShowCurrentAsync(allowElevation: true);
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
            DellChargeSetting = FormatPresetChargeSetting(preset);
            CommandResult refresh = await _cctkService.ShowCurrentAsync(allowElevation: true);
            if (refresh.Success)
            {
                DellChargeSetting = CleanCctkOutput(refresh.StandardOutput);
                StatusMessage = refresh.Message;
            }
            else
            {
                StatusMessage = $"{result.Message} Readback unavailable.";
            }
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
            GpuUsagePercent = sensors.GpuUsagePercent;
            CpuTemperatureCelsius = sensors.CpuTemperatureCelsius;
            CpuPackagePowerWatts = sensors.CpuPackagePowerWatts;
            Battery = battery;
            BatteryPowerWatts = Battery.ChargeRateWatts;
            RefreshWindowsPowerMode(Battery.IsPluggedIn);
            BatteryUsage = _batteryUsageService.Record(Battery, SelectedBatteryUsageDate, CurrentWindowsPowerMode);
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
                GpuUsagePercent = sensors.GpuUsagePercent,
                CpuTemperatureCelsius = sensors.CpuTemperatureCelsius,
                CpuPackagePowerWatts = sensors.CpuPackagePowerWatts,
                BatteryPowerWatts = Battery.ChargeRateWatts,
                FanRpm = sensors.FanRpm.Values.FirstOrDefault(),
                TopCpuProcessName = topCpu.FirstOrDefault()?.Name
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
        GpuGraphValues = _samples.Select(s => s.GpuUsagePercent).ToArray();
        TemperatureGraphValues = _samples.Select(s => s.CpuTemperatureCelsius).ToArray();
        BatteryWattsGraphValues = _samples.Select(s => s.BatteryPowerWatts).ToArray();
        CpuPowerGraphValues = _samples.Select(s => s.CpuPackagePowerWatts).ToArray();
        FanGraphValues = _samples.Select(s => s.FanRpm).ToArray();
        _averageBatteryDischargeWatts = CalculateAverageBatteryDischargeWatts(_samples);
        _averageBatteryTimeRemaining = CalculateAverageBatteryTimeRemaining(Battery, _averageBatteryDischargeWatts);
        CpuUsagePeaks = BuildUsagePeaks(_samples, s => s.CpuUsagePercent, s => s.TopCpuProcessName, includeProcessName: true);
        GpuUsagePeaks = BuildUsagePeaks(_samples, s => s.GpuUsagePercent, _ => null, includeProcessName: false);
        CpuGraphSummary = FormatGraphSummary(CpuGraphValues, "N1", "%");
        SystemUsageGraphSummary = FormatUsageGraphSummary(CpuGraphValues, GpuGraphValues);
        BatteryWattsGraphSummary = FormatGraphSummary(BatteryWattsGraphValues, "N1", " W");
        CpuPowerGraphSummary = FormatGraphSummary(CpuPowerGraphValues, "N1", " W");
        TemperatureGraphSummary = FormatGraphSummary(TemperatureGraphValues, "N0", " C");
        OnPropertyChanged(nameof(BatteryDrainSummaryText));
        OnPropertyChanged(nameof(BatteryTimeText));
        NotifyUsageDetailMetricsChanged();
    }

    private void ShowPreviousBatteryUsageDay()
    {
        SelectedBatteryUsageDate = SelectedBatteryUsageDate.AddDays(-1);
    }

    private void ShowNextBatteryUsageDay()
    {
        if (SelectedBatteryUsageDate < DateTime.Today)
        {
            SelectedBatteryUsageDate = SelectedBatteryUsageDate.AddDays(1);
        }
    }

    private void ShowTodayBatteryUsage()
    {
        SelectedBatteryUsageDate = DateTime.Today;
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

    private static string FormatUsageGraphSummary(IEnumerable<double?> cpuValues, IEnumerable<double?> gpuValues)
    {
        double[] cpu = cpuValues.Where(v => v.HasValue).Select(v => v!.Value).TakeLast(120).ToArray();
        double[] gpu = gpuValues.Where(v => v.HasValue).Select(v => v!.Value).TakeLast(120).ToArray();
        string cpuCurrent = cpu.Length > 0 ? $"{cpu[^1]:N0}%" : "--";
        string cpuAverage = cpu.Length > 0 ? $"{cpu.Average():N0}%" : "--";
        string gpuCurrent = gpu.Length > 0 ? $"{gpu[^1]:N0}%" : "--";
        string gpuAverage = gpu.Length > 0 ? $"{gpu.Average():N0}%" : "--";
        return $"CPU cur {cpuCurrent} | GPU cur {gpuCurrent} | CPU avg {cpuAverage} | GPU avg {gpuAverage}";
    }

    private static TimeSpan? CalculateAverageBatteryTimeRemaining(BatteryStatus battery, double? averageDrainWatts)
    {
        if (battery.IsPluggedIn || battery.Percentage <= 0)
        {
            return null;
        }

        double? fullChargeCapacityMilliWattHours = battery.BatteryHealth.FullChargeCapacityMilliWattHours
            ?? battery.BatteryHealth.DesignCapacityMilliWattHours;
        if (fullChargeCapacityMilliWattHours is not > 0)
        {
            return null;
        }

        if (averageDrainWatts is not > 0.1)
        {
            return null;
        }

        double remainingWattHours = fullChargeCapacityMilliWattHours.Value / 1000d * battery.Percentage / 100d;
        double remainingHours = remainingWattHours / averageDrainWatts.Value;
        if (remainingHours is <= 0 or > 100)
        {
            return null;
        }

        return TimeSpan.FromHours(remainingHours);
    }

    private static double? CalculateAverageBatteryDischargeWatts(IReadOnlyList<SensorSample> samples)
    {
        double[] dischargeWatts = samples
            .Select(s => s.BatteryPowerWatts)
            .Where(watts => watts is < -0.5)
            .Select(watts => Math.Abs(watts!.Value))
            .ToArray();
        if (dischargeWatts.Length < 5)
        {
            return null;
        }

        return CalculateTrimmedAverage(dischargeWatts);
    }

    private static string CalculateBatteryPercentRateText(BatteryStatus battery, double? averageDrainWatts)
    {
        if (battery.IsPluggedIn)
        {
            return "--%/h";
        }

        double? fullChargeCapacityMilliWattHours = battery.BatteryHealth.FullChargeCapacityMilliWattHours
            ?? battery.BatteryHealth.DesignCapacityMilliWattHours;
        if (fullChargeCapacityMilliWattHours is not > 0 || averageDrainWatts is not > 0.1)
        {
            return "--%/h";
        }

        double fullChargeWattHours = fullChargeCapacityMilliWattHours.Value / 1000d;
        double percentPerHour = averageDrainWatts.Value / fullChargeWattHours * 100d;
        return $"-{percentPerHour:N1}%/h";
    }

    private static double CalculateTrimmedAverage(double[] values)
    {
        if (values.Length < 10)
        {
            return values.Average();
        }

        double[] ordered = values.Order().ToArray();
        int trimCount = Math.Max(1, ordered.Length / 10);
        return ordered.Skip(trimCount).Take(ordered.Length - trimCount * 2).Average();
    }

    private static IReadOnlyList<UsagePeakInfo> BuildUsagePeaks(
        IReadOnlyList<SensorSample> samples,
        Func<SensorSample, double?> valueSelector,
        Func<SensorSample, string?> processSelector,
        bool includeProcessName)
    {
        DateTimeOffset now = DateTimeOffset.Now;
        var values = samples
            .Select((sample, index) => new
            {
                Sample = sample,
                Index = index,
                Value = valueSelector(sample)
            })
            .Where(item => item.Value.HasValue)
            .Select(item => new
            {
                item.Sample,
                item.Index,
                Value = Math.Clamp(item.Value!.Value, 0, 100)
            })
            .ToArray();

        if (values.Length == 0)
        {
            return [];
        }

        var candidates = new List<(SensorSample Sample, int Index, double Value)>();
        for (int i = 0; i < values.Length; i++)
        {
            double previous = i == 0 ? double.MinValue : values[i - 1].Value;
            double next = i == values.Length - 1 ? double.MinValue : values[i + 1].Value;
            if (values[i].Value >= previous && values[i].Value >= next)
            {
                candidates.Add((values[i].Sample, values[i].Index, values[i].Value));
            }
        }

        if (candidates.Count < 3)
        {
            candidates = values.Select(item => (item.Sample, item.Index, item.Value)).ToList();
        }

        int minimumSeparation = Math.Max(2, values.Length / 18);
        var selected = new List<(SensorSample Sample, int Index, double Value)>();
        foreach ((SensorSample sample, int index, double value) in candidates.OrderByDescending(item => item.Value))
        {
            if (selected.Any(existing => Math.Abs(existing.Index - index) < minimumSeparation))
            {
                continue;
            }

            selected.Add((sample, index, value));
            if (selected.Count == 3)
            {
                break;
            }
        }

        if (selected.Count < 3)
        {
            foreach ((SensorSample sample, int index, double value) in values.Select(item => (item.Sample, item.Index, item.Value)).OrderByDescending(item => item.Value))
            {
                if (selected.Any(existing => existing.Index == index))
                {
                    continue;
                }

                selected.Add((sample, index, value));
                if (selected.Count == 3)
                {
                    break;
                }
            }
        }

        return selected
            .OrderByDescending(item => item.Value)
            .Select((item, rank) => new UsagePeakInfo
            {
                Rank = rank + 1,
                Index = item.Index,
                Timestamp = item.Sample.Timestamp,
                Value = item.Value,
                ProcessName = includeProcessName ? processSelector(item.Sample) : null,
                TimeAgoText = FormatTimeAgo(now - item.Sample.Timestamp)
            })
            .ToArray();
    }

    private static string FormatMaxPercent(IEnumerable<double?> values)
    {
        double[] visible = values.Where(v => v.HasValue).Select(v => v!.Value).ToArray();
        return visible.Length == 0 ? "--" : $"{visible.Max():N0}%";
    }

    private static string FormatAveragePercent(IEnumerable<double?> values)
    {
        double[] visible = values.Where(v => v.HasValue).Select(v => v!.Value).ToArray();
        return visible.Length == 0 ? "--" : $"{visible.Average():N0}%";
    }

    private static string FormatTimeAbove(IEnumerable<double?> values, double threshold, int sampleIntervalSeconds)
    {
        int count = values.Count(v => v is { } value && value > threshold);
        TimeSpan duration = TimeSpan.FromSeconds(count * Math.Max(1, sampleIntervalSeconds));
        if (duration.TotalSeconds < 1)
        {
            return "0s";
        }

        return duration.TotalMinutes >= 1
            ? $"{(int)duration.TotalMinutes}m {duration.Seconds}s"
            : $"{duration.Seconds}s";
    }

    private static string FormatSampleCount(IEnumerable<double?> values) =>
        values.Count(v => v.HasValue).ToString("N0");

    private static string FormatTimeAgo(TimeSpan age)
    {
        if (age.TotalMinutes >= 1)
        {
            return $"{(int)age.TotalMinutes}m {Math.Max(0, age.Seconds)}s ago";
        }

        return $"{Math.Max(0, (int)age.TotalSeconds)}s ago";
    }

    private void NotifyUsageDetailMetricsChanged()
    {
        OnPropertyChanged(nameof(CpuMaxDetailText));
        OnPropertyChanged(nameof(GpuMaxDetailText));
        OnPropertyChanged(nameof(CpuAverageDetailText));
        OnPropertyChanged(nameof(GpuAverageDetailText));
        OnPropertyChanged(nameof(CpuTimeAbove50Text));
        OnPropertyChanged(nameof(GpuTimeAbove50Text));
        OnPropertyChanged(nameof(CpuSamplesDetailText));
        OnPropertyChanged(nameof(GpuSamplesDetailText));
    }

    private static string SplitGraphSummary(string summary, int row)
    {
        string[] parts = summary.Split('|', StringSplitOptions.TrimEntries);
        if (parts.Length < 4)
        {
            return row == 0 ? "Cur -- | Avg --" : "Min -- | Max --";
        }

        return row == 0
            ? $"{parts[0]} | {parts[1]}"
            : $"{parts[2]} | {parts[3]}";
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

    private string FormatPresetChargeSetting(BatteryPreset preset) => preset switch
    {
        BatteryPreset.Health => $"Custom:{_settingsService.Current.HealthStart}-{_settingsService.Current.HealthStop}",
        BatteryPreset.Balanced => $"Custom:{_settingsService.Current.BalancedStart}-{_settingsService.Current.BalancedStop}",
        BatteryPreset.Standard => "Standard",
        BatteryPreset.PrimarilyAcUse => "PrimAcUse",
        BatteryPreset.Adaptive => "Adaptive",
        _ => "Unknown"
    };

    private string FormatPowerModeMenuText(WindowsPowerMode mode)
    {
        string prefix = CurrentWindowsPowerMode == mode ? "✓ " : string.Empty;
        return prefix + WindowsPowerModeService.ToDisplayName(mode);
    }

    private static string FormatCapacity(int? milliWattHours)
    {
        if (milliWattHours is not { } value)
        {
            return "Unavailable";
        }

        return value >= 1000 ? $"{value / 1000d:N1} Wh" : $"{value:N0} mWh";
    }
}
