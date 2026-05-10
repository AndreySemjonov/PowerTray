using PowerTray.Models;
using System.IO;
using System.Text.Json;

namespace PowerTray.Services;

public sealed class SensorService
{
    private static readonly TimeSpan CachedSensorGracePeriod = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan MinimumSensorCacheSaveInterval = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan MaximumSensorCacheSaveInterval = TimeSpan.FromMinutes(5);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private readonly SettingsService _settingsService;
    private readonly ISensorProvider _hwinfoProvider = new HwinfoSensorProvider();
    private readonly ISensorProvider _libreHardwareMonitorProvider = new LibreHardwareMonitorSensorProvider();
    private readonly ISensorProvider _windowsProvider = new WindowsSensorProvider();
    private readonly HwinfoRecoveryService _hwinfoRecoveryService = new();
    private double? _lastCpuTemperatureCelsius;
    private double? _lastCpuPackagePowerWatts;
    private DateTimeOffset _lastCpuSensorUpdate = DateTimeOffset.MinValue;
    private DateTimeOffset _lastCpuSensorCacheSave = DateTimeOffset.MinValue;

    public SensorService(SettingsService settingsService)
    {
        _settingsService = settingsService;
        LoadCachedCpuSensors();
    }

    public SensorReadings Read()
    {
        SensorReadings? hwinfoReadings = null;
        if (_settingsService.Current.EnableHwinfoIntegration)
        {
            hwinfoReadings = RememberCpuSensors(_hwinfoProvider.Read());
            _hwinfoRecoveryService.Observe(_settingsService.Current, hwinfoReadings);
            if (hwinfoReadings.IsAvailable && HasCpuSensors(hwinfoReadings))
            {
                return hwinfoReadings;
            }
        }

        if (_settingsService.Current.EnableLibreHardwareMonitorIntegration)
        {
            SensorReadings readings = RememberCpuSensors(_libreHardwareMonitorProvider.Read());
            SensorReadings merged = MergeRecentCpuSensors(MergeStatus(readings, hwinfoReadings));
            if (merged.IsAvailable)
            {
                return merged;
            }

            return hwinfoReadings is not null ? MergeRecentCpuSensors(hwinfoReadings) : merged;
        }

        return MergeRecentCpuSensors(hwinfoReadings ?? _windowsProvider.Read());
    }

    private static SensorReadings MergeStatus(SensorReadings readings, SensorReadings? primaryReadings)
    {
        if (primaryReadings is null ||
            primaryReadings.IsAvailable ||
            string.IsNullOrWhiteSpace(primaryReadings.Status) ||
            readings.Status.Contains(primaryReadings.Status, StringComparison.OrdinalIgnoreCase))
        {
            return readings;
        }

        return new SensorReadings
        {
            IsAvailable = readings.IsAvailable,
            Status = $"{primaryReadings.Status}; {readings.Status}",
            CpuUsagePercent = readings.CpuUsagePercent ?? primaryReadings.CpuUsagePercent,
            CpuTemperatureCelsius = readings.CpuTemperatureCelsius,
            CpuPackagePowerWatts = readings.CpuPackagePowerWatts,
            GpuUsagePercent = readings.GpuUsagePercent,
            BatteryPowerWatts = readings.BatteryPowerWatts,
            FanRpm = readings.FanRpm
        };
    }

    private SensorReadings RememberCpuSensors(SensorReadings readings)
    {
        if (readings.CpuTemperatureCelsius is not null || readings.CpuPackagePowerWatts is not null)
        {
            double? previousCpuTemperature = _lastCpuTemperatureCelsius;
            double? previousCpuPower = _lastCpuPackagePowerWatts;
            _lastCpuTemperatureCelsius = readings.CpuTemperatureCelsius ?? _lastCpuTemperatureCelsius;
            _lastCpuPackagePowerWatts = readings.CpuPackagePowerWatts ?? _lastCpuPackagePowerWatts;
            _lastCpuSensorUpdate = DateTimeOffset.Now;
            if (ShouldSaveCachedCpuSensors(previousCpuTemperature, previousCpuPower, _lastCpuSensorUpdate))
            {
                SaveCachedCpuSensors();
            }
        }

        return readings;
    }

    private bool ShouldSaveCachedCpuSensors(double? previousCpuTemperature, double? previousCpuPower, DateTimeOffset now)
    {
        if (now - _lastCpuSensorCacheSave < MinimumSensorCacheSaveInterval)
        {
            return false;
        }

        if (_lastCpuSensorCacheSave == DateTimeOffset.MinValue || now - _lastCpuSensorCacheSave >= MaximumSensorCacheSaveInterval)
        {
            return true;
        }

        return HasMeaningfulChange(previousCpuTemperature, _lastCpuTemperatureCelsius, 0.5)
            || HasMeaningfulChange(previousCpuPower, _lastCpuPackagePowerWatts, 0.5);
    }

    private static bool HasMeaningfulChange(double? previous, double? current, double threshold) =>
        previous.HasValue != current.HasValue || (previous.HasValue && current.HasValue && Math.Abs(previous.Value - current.Value) >= threshold);

    private SensorReadings MergeRecentCpuSensors(SensorReadings readings)
    {
        if (HasCpuSensors(readings) || DateTimeOffset.Now - _lastCpuSensorUpdate > CachedSensorGracePeriod)
        {
            return readings;
        }

        double? cpuTemp = readings.CpuTemperatureCelsius ?? _lastCpuTemperatureCelsius;
        double? cpuPower = readings.CpuPackagePowerWatts ?? _lastCpuPackagePowerWatts;
        if (cpuTemp is null && cpuPower is null)
        {
            return readings;
        }

        string status = readings.Status.Contains("recent CPU", StringComparison.OrdinalIgnoreCase)
            ? readings.Status
            : $"{readings.Status}; using recent CPU sensor values";

        return new SensorReadings
        {
            IsAvailable = readings.IsAvailable || cpuTemp is not null || cpuPower is not null || readings.BatteryPowerWatts is not null,
            Status = status,
            CpuUsagePercent = readings.CpuUsagePercent,
            CpuTemperatureCelsius = cpuTemp,
            CpuPackagePowerWatts = cpuPower,
            GpuUsagePercent = readings.GpuUsagePercent,
            BatteryPowerWatts = readings.BatteryPowerWatts,
            FanRpm = readings.FanRpm
        };
    }

    private static bool HasCpuSensors(SensorReadings readings) =>
        readings.CpuTemperatureCelsius is not null || readings.CpuPackagePowerWatts is not null;

    private void LoadCachedCpuSensors()
    {
        try
        {
            string path = GetCachePath();
            if (!File.Exists(path))
            {
                return;
            }

            CachedCpuSensors? cache = JsonSerializer.Deserialize<CachedCpuSensors>(File.ReadAllText(path), JsonOptions);
            if (cache is null || DateTimeOffset.Now - cache.UpdatedAt > CachedSensorGracePeriod)
            {
                return;
            }

            _lastCpuTemperatureCelsius = cache.CpuTemperatureCelsius;
            _lastCpuPackagePowerWatts = cache.CpuPackagePowerWatts;
            _lastCpuSensorUpdate = cache.UpdatedAt;
        }
        catch (Exception ex)
        {
            LogService.FeatureError(LogFeature.CpuGpuUsage, ex, "Failed to load cached CPU sensor values.");
        }
    }

    private void SaveCachedCpuSensors()
    {
        try
        {
            Directory.CreateDirectory(LogService.AppDataRoot);
            File.WriteAllText(GetCachePath(), JsonSerializer.Serialize(new CachedCpuSensors
            {
                UpdatedAt = _lastCpuSensorUpdate,
                CpuTemperatureCelsius = _lastCpuTemperatureCelsius,
                CpuPackagePowerWatts = _lastCpuPackagePowerWatts
            }, JsonOptions));
            _lastCpuSensorCacheSave = DateTimeOffset.Now;
        }
        catch (Exception ex)
        {
            LogService.FeatureError(LogFeature.CpuGpuUsage, ex, "Failed to save cached CPU sensor values.");
        }
    }

    private static string GetCachePath() => Path.Combine(LogService.AppDataRoot, "sensor-cache.json");

    private sealed class CachedCpuSensors
    {
        public DateTimeOffset UpdatedAt { get; set; }
        public double? CpuTemperatureCelsius { get; set; }
        public double? CpuPackagePowerWatts { get; set; }
    }
}
