using XPSBatteryTray.Models;

namespace XPSBatteryTray.Services;

public sealed class SensorService
{
    private readonly SettingsService _settingsService;
    private readonly ISensorProvider _hwinfoProvider = new HwinfoSensorProvider();
    private readonly ISensorProvider _libreHardwareMonitorProvider = new LibreHardwareMonitorSensorProvider();
    private readonly ISensorProvider _windowsProvider = new WindowsSensorProvider();

    public SensorService(SettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    public SensorReadings Read()
    {
        if (_settingsService.Current.EnableHwinfoIntegration)
        {
            SensorReadings readings = _hwinfoProvider.Read();
            if (readings.IsAvailable)
            {
                return readings;
            }
        }

        if (_settingsService.Current.EnableLibreHardwareMonitorIntegration)
        {
            SensorReadings readings = _libreHardwareMonitorProvider.Read();
            if (readings.IsAvailable)
            {
                return readings;
            }

            return readings;
        }

        return _windowsProvider.Read();
    }
}
