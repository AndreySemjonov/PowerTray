namespace XPSBatteryTray.Models;

public sealed class SensorSample
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.Now;
    public double CpuUsagePercent { get; init; }
    public double? CpuTemperatureCelsius { get; init; }
    public double? CpuPackagePowerWatts { get; init; }
    public double? BatteryPowerWatts { get; init; }
    public double? FanRpm { get; init; }
}
