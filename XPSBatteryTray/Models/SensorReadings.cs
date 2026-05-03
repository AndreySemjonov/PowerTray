namespace XPSBatteryTray.Models;

public sealed class SensorReadings
{
    public bool IsAvailable { get; init; }
    public string Status { get; init; } = "HWiNFO shared memory not available";
    public double? CpuTemperatureCelsius { get; init; }
    public double? CpuPackagePowerWatts { get; init; }
    public double? BatteryPowerWatts { get; init; }
    public IReadOnlyDictionary<string, double> FanRpm { get; init; } = new Dictionary<string, double>();
}
