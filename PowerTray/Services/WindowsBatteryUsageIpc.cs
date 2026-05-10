namespace PowerTray.Services;

public static class WindowsBatteryUsageIpc
{
    public const string PipeName = "PowerTray.WindowsBatteryUsage.v1";
    public const string WindowsBatteryUsageRequestType = "windowsBatteryUsage";
    public const string CctkReadbackRequestType = "cctkReadback";
    public const string PrimaryBatteryChargeReadback = "PrimaryBattChargeCfg";
    public const string ThermalManagementReadback = "ThermalManagement";
    public const int MaxMessageBytes = 1024 * 1024;
    public static readonly TimeSpan ConnectTimeout = TimeSpan.FromMilliseconds(450);
    public static readonly TimeSpan ResponseTimeout = TimeSpan.FromSeconds(45);
}
