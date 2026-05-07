namespace PowerTray.Models;

public sealed class ProcessUsageInfo
{
    private static readonly HashSet<string> SystemProcessNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Console Window Host",
        "Desktop Window Manager",
        "File Explorer",
        "Microsoft Defender",
        "Start Menu",
        "System",
        "System Idle",
        "Text Input",
        "Windows Search",
        "Windows Security",
        "Windows Service Host",
        "Windows Shell",
        "WMI Provider Host"
    };

    public int ProcessId { get; init; }
    public string Name { get; init; } = string.Empty;
    public double CpuPercent { get; init; }
    public long WorkingSetBytes { get; init; }
    public TimeSpan RunTime { get; init; }
    public double EstimatedEnergyImpact { get; init; }
    public double EstimatedEnergyImpactBarPercent { get; init; }
    public double EstimatedEnergyPercent { get; init; }

    public string MemoryText => $"{WorkingSetBytes / 1024d / 1024d:N0} MB";
    public string CpuText => $"{CpuPercent:N1}%";
    public string EnergyText => $"{EstimatedEnergyPercent:N1}%";
    public bool IsSystemProcess => SystemProcessNames.Contains(Name);
    public string ResourceImpactBadgeText => IsSystemProcess ? "System" : "CPU";
    public string ResourceImpactText => $"{EstimatedEnergyImpactBarPercent:N0}";
    public string EstimatedDrainShareText => EstimatedEnergyPercent > 0.05
        ? $"{EstimatedEnergyPercent:N1}% of observed battery drop"
        : "not enough battery drop observed yet";
    public string ResourceImpactToolTip
    {
        get
        {
            string ownerText = IsSystemProcess
                ? "Windows/system process. Other apps can indirectly drive this work."
                : "App process ranked by CPU-attributed activity.";
            return string.Join(Environment.NewLine,
                Name,
                $"Type: {ResourceImpactBadgeText}",
                $"Current CPU: {CpuText}",
                $"Memory: {MemoryText}",
                $"Relative score: {ResourceImpactText}",
                $"Estimated share: {EstimatedDrainShareText}",
                ownerText,
                "Note: Windows does not expose exact per-app battery drain here.");
        }
    }
}
