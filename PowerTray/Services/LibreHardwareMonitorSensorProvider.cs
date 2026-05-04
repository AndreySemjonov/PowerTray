using LibreHardwareMonitor.Hardware;
using XPSBatteryTray.Models;

namespace XPSBatteryTray.Services;

public sealed class LibreHardwareMonitorSensorProvider : ISensorProvider, IDisposable
{
    private readonly object _lock = new();
    private Computer? _computer;
    private bool _openFailed;
    private static DateTimeOffset _lastDiagnosticLog = DateTimeOffset.MinValue;

    public string Name => "LibreHardwareMonitor";

    public SensorReadings Read()
    {
        lock (_lock)
        {
            try
            {
                Computer computer = EnsureComputer();
                double? cpuTemp = null;
                double? cpuPower = null;
                double? gpuUsage = null;
                double? batteryPower = null;
                int batteryPowerScore = 0;
                var fans = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                var diagnostics = new List<string>();

                foreach (IHardware hardware in computer.Hardware)
                {
                    ReadHardware(hardware, diagnostics, ref cpuTemp, ref cpuPower, ref gpuUsage, ref batteryPower, ref batteryPowerScore, fans);
                }

                bool hasAnySensor = cpuTemp is not null || cpuPower is not null || gpuUsage is not null || batteryPower is not null || fans.Count > 0;
                if (!hasAnySensor)
                {
                    LogDiagnosticsIfNeeded("no matching sensors", diagnostics);
                    return Unavailable("LibreHardwareMonitor available but no matching sensors were found");
                }

                bool isPartial = cpuTemp is null || cpuPower is null;
                if (isPartial)
                {
                    LogDiagnosticsIfNeeded($"partial sensors: CPU temp={Format(cpuTemp)}, CPU power={Format(cpuPower)}, battery watts={Format(batteryPower)}, fans={fans.Count}", diagnostics);
                }

                return new SensorReadings
                {
                    IsAvailable = true,
                    Status = isPartial
                        ? "LibreHardwareMonitor partial; CPU sensors may require administrator rights"
                        : "LibreHardwareMonitor sensors active",
                    CpuTemperatureCelsius = cpuTemp,
                    CpuPackagePowerWatts = cpuPower,
                    GpuUsagePercent = gpuUsage,
                    BatteryPowerWatts = batteryPower,
                    FanRpm = fans
                };
            }
            catch (Exception ex)
            {
                _openFailed = true;
                LogService.Error(ex, "Failed to read LibreHardwareMonitor sensors.");
                return Unavailable(CctkService.IsAdministrator()
                    ? "LibreHardwareMonitor sensors unavailable"
                    : "LibreHardwareMonitor may require administrator rights");
            }
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _computer?.Close();
            _computer = null;
        }
    }

    private Computer EnsureComputer()
    {
        if (_computer is not null)
        {
            return _computer;
        }

        if (_openFailed)
        {
            throw new InvalidOperationException("LibreHardwareMonitor initialization failed earlier in this session.");
        }

        _computer = new Computer
        {
            IsBatteryEnabled = true,
            IsCpuEnabled = true,
            IsGpuEnabled = true,
            IsMotherboardEnabled = true,
            IsControllerEnabled = true
        };
        _computer.Open();
        return _computer;
    }

    private static void ReadHardware(
        IHardware hardware,
        List<string> diagnostics,
        ref double? cpuTemp,
        ref double? cpuPower,
        ref double? gpuUsage,
        ref double? batteryPower,
        ref int batteryPowerScore,
        Dictionary<string, double> fans)
    {
        hardware.Update();
        diagnostics.Add($"{hardware.HardwareType}: {hardware.Name}");

        foreach (IHardware subHardware in hardware.SubHardware)
        {
            ReadHardware(subHardware, diagnostics, ref cpuTemp, ref cpuPower, ref gpuUsage, ref batteryPower, ref batteryPowerScore, fans);
        }

        foreach (ISensor sensor in hardware.Sensors)
        {
            if (sensor.Value is not { } rawValue || double.IsNaN(rawValue) || double.IsInfinity(rawValue))
            {
                if (hardware.HardwareType == HardwareType.Cpu ||
                    sensor.SensorType is SensorType.Temperature or SensorType.Power)
                {
                    diagnostics.Add($"{hardware.HardwareType}: {hardware.Name} / {sensor.SensorType} / {sensor.Name} = unavailable");
                }

                continue;
            }

            double value = rawValue;
            string haystack = $"{hardware.Name} {sensor.Name} {sensor.SensorType}";
            diagnostics.Add($"{hardware.HardwareType}: {hardware.Name} / {sensor.SensorType} / {sensor.Name} = {value:N2}");

            if (cpuTemp is null &&
                sensor.SensorType == SensorType.Temperature &&
                (ContainsAny(haystack, "CPU Package", "Core Max", "Core Temperatures", "CPU Core") ||
                 hardware.HardwareType == HardwareType.Cpu))
            {
                cpuTemp = value;
                continue;
            }

            if (cpuPower is null &&
                sensor.SensorType == SensorType.Power &&
                (ContainsAny(haystack, "CPU Package", "Package", "IA Cores", "Processor") ||
                 hardware.HardwareType == HardwareType.Cpu))
            {
                cpuPower = Math.Abs(value);
                continue;
            }

            if (TryGetBatteryPower(sensor, haystack, value, out double normalizedBatteryPower, out int score) && score > batteryPowerScore)
            {
                batteryPower = normalizedBatteryPower;
                batteryPowerScore = score;
                continue;
            }

            if (gpuUsage is null &&
                sensor.SensorType == SensorType.Load &&
                IsGpuHardware(hardware.HardwareType) &&
                ContainsAny(haystack, "GPU Core", "GPU Total", "3D", "D3D", "Graphics", "Core"))
            {
                gpuUsage = Math.Clamp(value, 0, 100);
                continue;
            }

            if (sensor.SensorType == SensorType.Fan || ContainsAny(haystack, "Fan", "RPM"))
            {
                fans[sensor.Name] = value;
            }
        }
    }

    private static bool TryGetBatteryPower(ISensor sensor, string haystack, double value, out double batteryPower, out int score)
    {
        batteryPower = value;
        score = 0;

        if (ContainsAny(haystack, "Remaining Capacity", "Full Charge Capacity", "Design Capacity", "Wear Level", "Charge Level", "Battery Voltage", "Estimated Remaining Time"))
        {
            return false;
        }

        bool isBattery = ContainsAny(haystack, "Battery", "Charge Rate", "Discharge Rate");
        if (!isBattery)
        {
            return false;
        }

        if (ContainsAny(haystack, "Discharge Rate"))
        {
            batteryPower = -Math.Abs(value);
            score = 100;
            return true;
        }

        if (ContainsAny(haystack, "Charge Rate"))
        {
            batteryPower = value;
            score = 100;
            return true;
        }

        if (sensor.SensorType == SensorType.Power && ContainsAny(haystack, "Battery Power", "Charge Power", "Battery Rate", "Power Rate", "Present Rate", "Battery"))
        {
            score = 70;
            return true;
        }

        return false;
    }

    private static bool ContainsAny(string value, params string[] terms) =>
        terms.Any(term => value.Contains(term, StringComparison.OrdinalIgnoreCase));

    private static bool IsGpuHardware(HardwareType hardwareType) =>
        hardwareType is HardwareType.GpuAmd or HardwareType.GpuIntel or HardwareType.GpuNvidia;

    private static void LogDiagnosticsIfNeeded(string reason, IReadOnlyList<string> diagnostics)
    {
        DateTimeOffset now = DateTimeOffset.Now;
        if (now - _lastDiagnosticLog < TimeSpan.FromMinutes(1))
        {
            return;
        }

        _lastDiagnosticLog = now;
        string sample = string.Join(Environment.NewLine, diagnostics.Take(140));
        LogService.Info($"LibreHardwareMonitor diagnostics: {reason}.{Environment.NewLine}{sample}");
    }

    private static string Format(double? value) => value?.ToString("N1") ?? "none";

    private static SensorReadings Unavailable(string status) => new() { IsAvailable = false, Status = status };
}
