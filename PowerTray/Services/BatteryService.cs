using System.Runtime.InteropServices;
using System.Management;
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
                HealthStatus = "Unavailable",
                BatteryHealth = TryGetBatteryHealthInfo()
            };
        }

        TimeSpan? remaining = status.BatteryLifeTime >= 0
            ? TimeSpan.FromSeconds(status.BatteryLifeTime)
            : null;

        return new BatteryStatus
        {
            Percentage = status.BatteryLifePercent == 255 ? 0 : status.BatteryLifePercent,
            IsPluggedIn = status.ACLineStatus == 1,
            IsPowerSave = status.SystemStatusFlag == 1,
            IsCritical = (status.BatteryFlag & 4) == 4 || (status.BatteryLifePercent != 255 && status.BatteryLifePercent <= 10),
            EstimatedTimeRemaining = remaining,
            ChargeRateWatts = sensorBatteryWatts ?? TryGetBatteryPowerWattsFromWmi(),
            HealthStatus = status.BatteryFlag switch
            {
                128 => "No battery",
                255 => "Unknown",
                _ => null
            },
            BatteryHealth = TryGetBatteryHealthInfo()
        };
    }

    private static BatteryHealthInfo TryGetBatteryHealthInfo()
    {
        try
        {
            int? designCapacity = ReadFirstInt(@"root\WMI", "SELECT DesignedCapacity FROM BatteryStaticData", "DesignedCapacity")
                ?? ReadFirstInt(@"root\CIMV2", "SELECT DesignCapacity FROM Win32_Battery", "DesignCapacity");
            int? fullChargeCapacity = ReadFirstInt(@"root\WMI", "SELECT FullChargedCapacity FROM BatteryFullChargedCapacity", "FullChargedCapacity")
                ?? ReadFirstInt(@"root\CIMV2", "SELECT FullChargeCapacity FROM Win32_Battery", "FullChargeCapacity");
            int? cycleCount = ReadFirstInt(@"root\WMI", "SELECT CycleCount FROM BatteryCycleCount", "CycleCount");

            bool hasAnyValue = designCapacity.HasValue || fullChargeCapacity.HasValue || cycleCount.HasValue;
            return new BatteryHealthInfo
            {
                DesignCapacityMilliWattHours = designCapacity,
                FullChargeCapacityMilliWattHours = fullChargeCapacity,
                CycleCount = cycleCount,
                Source = hasAnyValue ? "Windows battery WMI" : "Unavailable"
            };
        }
        catch (Exception ex)
        {
            LogService.Error(ex, "Failed to read battery health information from WMI.");
            return new BatteryHealthInfo();
        }
    }

    private static int? ReadFirstInt(string scope, string query, string propertyName)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(scope, query);
            foreach (ManagementObject item in searcher.Get().Cast<ManagementObject>())
            {
                using (item)
                {
                    return ToInt(item[propertyName]);
                }
            }
        }
        catch (ManagementException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }

        return null;
    }

    private static int? ToInt(object? value)
    {
        if (value is null)
        {
            return null;
        }

        try
        {
            int result = Convert.ToInt32(value);
            return result > 0 ? result : null;
        }
        catch
        {
            return null;
        }
    }

    private static double? TryGetBatteryPowerWattsFromWmi()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(@"root\WMI", "SELECT ChargeRate, DischargeRate, Charging, Discharging FROM BatteryStatus");
            foreach (ManagementObject battery in searcher.Get().Cast<ManagementObject>())
            {
                using (battery)
                {
                    double? dischargeRateWatts = MilliwattsToWatts(battery["DischargeRate"]);
                    double? chargeRateWatts = MilliwattsToWatts(battery["ChargeRate"]);
                    bool discharging = battery["Discharging"] is bool d && d;
                    bool charging = battery["Charging"] is bool c && c;

                    if (discharging && dischargeRateWatts is > 0)
                    {
                        return -dischargeRateWatts.Value;
                    }

                    if (charging && chargeRateWatts is > 0)
                    {
                        return chargeRateWatts.Value;
                    }

                    if (dischargeRateWatts is > 0)
                    {
                        return -dischargeRateWatts.Value;
                    }

                    if (chargeRateWatts is > 0)
                    {
                        return chargeRateWatts.Value;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            LogService.Error(ex, "Failed to read battery charge/discharge rate from WMI.");
        }

        return null;
    }

    private static double? MilliwattsToWatts(object? value)
    {
        if (value is null)
        {
            return null;
        }

        try
        {
            double milliwatts = Convert.ToDouble(value);
            return milliwatts > 0 && milliwatts < 1_000_000 ? milliwatts / 1000d : null;
        }
        catch
        {
            return null;
        }
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
