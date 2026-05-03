namespace XPSBatteryTray.Models;

public sealed class ProcessUsageInfo
{
    public int ProcessId { get; init; }
    public string Name { get; init; } = string.Empty;
    public double CpuPercent { get; init; }
    public long WorkingSetBytes { get; init; }
    public TimeSpan RunTime { get; init; }
    public double EstimatedEnergyImpact { get; init; }

    public string MemoryText => $"{WorkingSetBytes / 1024d / 1024d:N0} MB";
    public string CpuText => $"{CpuPercent:N1}%";
    public string EnergyText => $"{EstimatedEnergyImpact:N1}";
}
