namespace PowerTray.Services;

public static class WindowsBatteryUsageIpc
{
    public const string PipeName = "PowerTray.WindowsBatteryUsage.v1";
    public const string WindowsBatteryUsageRequestType = "windowsBatteryUsage";
    public const string CctkReadbackRequestType = "cctkReadback";
    public const string CctkWriteRequestType = "cctkWrite";
    public const string PrimaryBatteryChargeReadback = "PrimaryBattChargeCfg";
    public const string ThermalManagementReadback = "ThermalManagement";
    public const string ThermalOptimizedWrite = "ThermalOptimized";
    public const string ThermalCoolWrite = "ThermalCool";
    public const string ThermalQuietWrite = "ThermalQuiet";
    public const string ThermalUltraPerformanceWrite = "ThermalUltraPerformance";
    public const string ChargeStandardWrite = "ChargeStandard";
    public const string ChargePrimarilyAcUseWrite = "ChargePrimarilyAcUse";
    public const string ChargeAdaptiveWrite = "ChargeAdaptive";
    public const string ChargeCustomWrite = "ChargeCustom";
    public const int MaxMessageBytes = 1024 * 1024;
    public static readonly TimeSpan ConnectTimeout = TimeSpan.FromMilliseconds(450);
    public static readonly TimeSpan ResponseTimeout = TimeSpan.FromSeconds(45);
}
