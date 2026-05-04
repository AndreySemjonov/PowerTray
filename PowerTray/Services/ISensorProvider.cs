using XPSBatteryTray.Models;

namespace XPSBatteryTray.Services;

public interface ISensorProvider
{
    string Name { get; }
    SensorReadings Read();
}
