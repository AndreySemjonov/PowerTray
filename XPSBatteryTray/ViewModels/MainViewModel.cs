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
    private IReadOnlyList<double?> _fanGraphValues = [];
    private string _cpuGraphSummary = "Current -- / Min -- / Max --";
    private string _batteryWattsGraphSummary = "Current -- / Min -- / Max --";
    private string _temperatureGraphSummary = "Current -- / Min -- / Max --";

    public MainViewModel(SettingsService settingsService, CctkService cctkService, BatteryService batteryService, SensorService sensorService, ProcessStatsService processStatsService)
    {
        _settingsService = settingsService;
        _cctkService = cctkService;
        _batteryService = batteryService;
        _sensorService = sensorService;
        _processStatsService = processStatsService;

        TopCpuProcesses = new ObservableCollection<ProcessUsageInfo>();
        TopMemoryProcesses = new ObservableCollection<ProcessUsageInfo>();
        EnergyImpactProcesses = new ObservableCollection<ProcessUsageInfo>();
        FanReadings = new ObservableCollection<string>();

        RefreshDellChargeCommand = new RelayCommand(async () => await RefreshDellChargeAsync());
        ApplyBatteryPresetCommand = new RelayCommand(async parameter => await ApplyBatteryPresetAsync(parameter));
        OpenSettingsCommand = new RelayCommand(() => OpenSettingsRequested?.Invoke(this, EventArgs.Empty));
        OpenBatteryModesCommand = new RelayCommand(() => OpenBatteryModesRequested?.Invoke(this, EventArgs.Empty));

        ConfigureTimer();
    }

    public event EventHandler? OpenSettingsRequested;
    public event EventHandler? OpenBatteryModesRequested;

    public BatteryStatus Battery
    {
        get => _battery;
        private set
        {
            if (SetProperty(ref _battery, value))
            {
                OnPropertyChanged(nameof(BatterySummary));
                OnPropertyChanged(nameof(BatteryTimeText));
                OnPropertyChanged(nameof(BatteryPowerText));
            }
        }
    }

    public string DellChargeSetting
    {
        get => _dellChargeSetting;
        private set => SetProperty(ref _dellChargeSetting, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public string HwinfoStatus
    {
        get => _hwinfoStatus;
        private set => SetProperty(ref _hwinfoStatus, value);
    }

    public double CpuUsagePercent
    {
        get => _cpuUsagePercent;
        private set
        {
            if (SetProperty(ref _cpuUsagePercent, value))
            {
                OnPropertyChanged(nameof(CpuUsageText));
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

    public string TemperatureGraphSummary
    {
        get => _temperatureGraphSummary;
        private set => SetProperty(ref _temperatureGraphSummary, value);
    }

    public ObservableCollection<ProcessUsageInfo> TopCpuProcesses { get; }
    public ObservableCollection<ProcessUsageInfo> TopMemoryProcesses { get; }
    public ObservableCollection<ProcessUsageInfo> EnergyImpactProcesses { get; }
    public ObservableCollection<string> FanReadings { get; }

    public ICommand RefreshDellChargeCommand { get; }
    public ICommand ApplyBatteryPresetCommand { get; }
    public ICommand OpenSettingsCommand { get; }
    public ICommand OpenBatteryModesCommand { get; }

    public string BatterySummary => Battery.Percentage > 0
        ? $"{Battery.Percentage}% - {(Battery.IsPluggedIn ? "Plugged in" : "On battery")}"
        : Battery.IsPluggedIn ? "Plugged in" : "Battery status unavailable";

    public string BatteryTimeText => Battery.EstimatedTimeRemaining is { } remaining
        ? $"{remaining.Hours + remaining.Days * 24}h {remaining.Minutes}m remaining"
        : "Time remaining unavailable";

    public string BatteryPowerText => BatteryPowerWatts is { } watts
        ? $"{watts:N1} W"
        : "Watts unavailable";

    public string CpuUsageText => $"{CpuUsagePercent:N1}%";
    public string CpuTemperatureText => CpuTemperatureCelsius is { } value ? $"{value:N0} C" : "Temp unavailable";
    public string CpuPowerText => CpuPackagePowerWatts is { } value ? $"{value:N1} W" : "Power unavailable";
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
            (double overallCpu, IReadOnlyList<ProcessUsageInfo> topCpu, IReadOnlyList<ProcessUsageInfo> topMemory, IReadOnlyList<ProcessUsageInfo> energyImpact) = _processStatsService.Sample();

            CpuUsagePercent = overallCpu;
            CpuTemperatureCelsius = sensors.CpuTemperatureCelsius;
            CpuPackagePowerWatts = sensors.CpuPackagePowerWatts;
            Battery = _batteryService.GetStatus(sensors.BatteryPowerWatts);
            BatteryPowerWatts = Battery.ChargeRateWatts;
            Memory = _processStatsService.GetMemoryInfo();

            Replace(TopCpuProcesses, topCpu);
            Replace(TopMemoryProcesses, topMemory);
            Replace(EnergyImpactProcesses, energyImpact);
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
        FanGraphValues = _samples.Select(s => s.FanRpm).ToArray();
        CpuGraphSummary = FormatGraphSummary(CpuGraphValues, "N1", "%");
        BatteryWattsGraphSummary = FormatGraphSummary(BatteryWattsGraphValues, "N1", " W");
        TemperatureGraphSummary = FormatGraphSummary(TemperatureGraphValues, "N0", " C");
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
            return $"Current -- / Min -- / Max --";
        }

        string current = visible[^1].ToString(format);
        string min = visible.Min().ToString(format);
        string max = visible.Max().ToString(format);
        return $"Current {current}{unit} / Min {min}{unit} / Max {max}{unit}";
    }
}
