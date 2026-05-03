using System.Runtime.InteropServices;
using XPSBatteryTray.Models;

namespace XPSBatteryTray.Services;

public sealed class BatteryService
{
    public BatteryStatus GetStatus(double? sensorBatteryWatts = null)
    {
        if (!GetSystemPowerStatus(out SystemPowerStatus status))
        {
            return new BatteryStatus
            {
                Percentage = 0,
                IsPluggedIn = false,
                ChargeRateWatts = sensorBatteryWatts,
                HealthStatus = "Unavailable"
            };
        }

        TimeSpan? remaining = status.BatteryLifeTime >= 0
            ? TimeSpan.FromSeconds(status.BatteryLifeTime)
            : null;

        return new BatteryStatus
        {
            Percentage = status.BatteryLifePercent == 255 ? 0 : status.BatteryLifePercent,
            IsPluggedIn = status.ACLineStatus == 1,
            EstimatedTimeRemaining = remaining,
            ChargeRateWatts = sensorBatteryWatts,
            HealthStatus = status.BatteryFlag switch
            {
                128 => "No battery",
                255 => "Unknown",
                _ => null
            }
        };
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemPowerStatus(out SystemPowerStatus lpSystemPowerStatus);

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemPowerStatus
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public int BatteryLifeTime;
        public int BatteryFullLifeTime;
    }
}
