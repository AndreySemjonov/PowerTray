using PowerTray.Models;

namespace PowerTray.Services;

public interface ISensorProvider
{
    string Name { get; }
    SensorReadings Read();
}
