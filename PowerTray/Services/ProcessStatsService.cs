using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using PowerTray.Models;

namespace PowerTray.Services;

public sealed class ProcessStatsService
{
    private const double MaxProcessAttributedDrainShare = 0.85;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static readonly Dictionary<string, string> KnownProcessNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Code"] = "Visual Studio Code",
        ["Codex"] = "Codex",
        ["conhost"] = "Console Window Host",
        ["devenv"] = "Visual Studio",
        ["dotnet"] = ".NET",
        ["dwm"] = "Desktop Window Manager",
        ["explorer"] = "File Explorer",
        ["HWiNFO64"] = "HWiNFO",
        ["Idle"] = "System Idle",
        ["msedge"] = "Microsoft Edge",
        ["msedgewebview2"] = "Edge WebView2",
        ["MsMpEng"] = "Microsoft Defender",
        ["powershell"] = "PowerShell",
        ["pwsh"] = "PowerShell",
        ["SearchHost"] = "Windows Search",
        ["SecurityHealthService"] = "Windows Security",
        ["ShellHost"] = "Windows Shell",
        ["StartMenuExperienceHost"] = "Start Menu",
        ["svchost"] = "Windows Service Host",
        ["System"] = "System",
        ["Taskmgr"] = "Task Manager",
        ["TextInputHost"] = "Text Input",
        ["WmiPrvSE"] = "WMI Provider Host"
    };

    private readonly Dictionary<int, ProcessCpuSnapshot> _previousCpu = new();
    private readonly Dictionary<string, string> _friendlyNameCache = new(StringComparer.OrdinalIgnoreCase);
    private DateTimeOffset _previousSample = DateTimeOffset.Now;
    private DateTimeOffset _lastEnergySave = DateTimeOffset.MinValue;
    private EnergyHistoryState _energyState = new();
    private bool _energyStateLoaded;
    private bool? _lastPluggedIn;
    private double _lastOverallCpu;

    private string EnergyHistoryPath => Path.Combine(LogService.AppDataRoot, "energy-history.json");

    public (double OverallCpuPercent, IReadOnlyList<ProcessUsageInfo> TopCpu, IReadOnlyList<ProcessUsageInfo> TopMemory, IReadOnlyList<ProcessUsageInfo> EnergyImpact, string EnergyImpactTitle, string EnergyImpactColumnHeader) Sample(BatteryStatus battery)
    {
        EnsureEnergyStateLoaded();
        Process[] processes = Process.GetProcesses();
        DateTimeOffset now = DateTimeOffset.Now;
        double elapsedSeconds = Math.Max(0.1, (now - _previousSample).TotalSeconds);
        int processorCount = Math.Max(1, Environment.ProcessorCount);
        var currentIds = new HashSet<int>();
        var usage = new List<ProcessUsageInfo>();
        double totalCpu = 0;

        foreach (Process process in processes)
        {
            try
            {
                currentIds.Add(process.Id);
                TimeSpan totalProcessorTime = process.TotalProcessorTime;
                _previousCpu.TryGetValue(process.Id, out ProcessCpuSnapshot previous);
                double cpuPercent = previous.TotalProcessorTime == default
                    ? 0
                    : (totalProcessorTime - previous.TotalProcessorTime).TotalSeconds / elapsedSeconds / processorCount * 100d;

                cpuPercent = Math.Clamp(cpuPercent, 0, 100);
                totalCpu += cpuPercent;
                TimeSpan runTime = process.StartTime <= DateTime.Now ? DateTime.Now - process.StartTime : TimeSpan.Zero;

                usage.Add(new ProcessUsageInfo
                {
                    ProcessId = process.Id,
                    Name = GetFriendlyProcessName(process),
                    CpuPercent = cpuPercent,
                    WorkingSetBytes = process.WorkingSet64,
                    RunTime = runTime,
                    EstimatedEnergyImpact = 0
                });

                _previousCpu[process.Id] = new ProcessCpuSnapshot(totalProcessorTime);
            }
            catch
            {
                // Processes can exit or deny access between enumeration and sampling.
            }
            finally
            {
                process.Dispose();
            }
        }

        foreach (int staleId in _previousCpu.Keys.Where(id => !currentIds.Contains(id)).ToArray())
        {
            _previousCpu.Remove(staleId);
        }

        _previousSample = now;
        _lastOverallCpu = Math.Clamp(totalCpu, 0, 100);
        UpdateEnergyHistory(usage, elapsedSeconds, now, battery, totalCpu);

        IReadOnlyList<ProcessUsageInfo> topCpu = usage.OrderByDescending(p => p.CpuPercent).Take(10).ToArray();
        IReadOnlyList<ProcessUsageInfo> topMemory = usage.OrderByDescending(p => p.WorkingSetBytes).Take(5).ToArray();
        IReadOnlyList<ProcessUsageInfo> energy = BuildEnergyImpactList(usage).Take(5).ToArray();
        return (_lastOverallCpu, topCpu, topMemory, energy, BuildEnergyImpactTitle(now), "Score");
    }

    public SystemMemoryInfo GetMemoryInfo()
    {
        var status = new MemoryStatusEx();
        status.dwLength = (uint)Marshal.SizeOf<MemoryStatusEx>();
        return GlobalMemoryStatusEx(ref status)
            ? new SystemMemoryInfo { TotalBytes = status.ullTotalPhys, AvailableBytes = status.ullAvailPhys }
            : new SystemMemoryInfo();
    }

    private void UpdateEnergyHistory(IReadOnlyList<ProcessUsageInfo> usage, double elapsedSeconds, DateTimeOffset now, BatteryStatus battery, double totalCpu)
    {
        bool isDischarging = !battery.IsPluggedIn;
        if (isDischarging && (!_energyState.IsActive || _lastPluggedIn == true))
        {
            StartNewEnergySession(now, battery.Percentage);
        }
        else if (!isDischarging && battery.ChargeRateWatts is > 1)
        {
            _energyState.IsActive = false;
        }

        _lastPluggedIn = battery.IsPluggedIn;
        _energyState.LastUpdated = now;
        _energyState.LastBatteryPercent = battery.Percentage;

        if (!isDischarging || !_energyState.IsActive)
        {
            SaveEnergyStateIfNeeded(now, force: false);
            return;
        }

        ProcessUsageInfo[] active = usage.Where(p => p.CpuPercent > 0.05).ToArray();
        if (active.Length == 0)
        {
            SaveEnergyStateIfNeeded(now, force: false);
            return;
        }

        double totalActiveCpu = Math.Max(0.1, active.Sum(p => p.CpuPercent));
        double sampleScore;
        if (battery.ChargeRateWatts is < -0.05)
        {
            double sampleMilliWattHours = Math.Abs(battery.ChargeRateWatts.Value) * elapsedSeconds / 3600d * 1000d;
            double activeDrainShare = Math.Clamp(totalCpu / 50d, 0.05, MaxProcessAttributedDrainShare);
            sampleScore = sampleMilliWattHours * activeDrainShare;
            _energyState.UsesBatteryRate = true;
        }
        else
        {
            sampleScore = totalActiveCpu * elapsedSeconds / 60d;
        }

        foreach (ProcessUsageInfo process in active)
        {
            double share = process.CpuPercent / totalActiveCpu;
            _energyState.ProcessTotals[process.Name] = _energyState.ProcessTotals.GetValueOrDefault(process.Name) + sampleScore * share;
        }

        SaveEnergyStateIfNeeded(now, force: false);
    }

    private IReadOnlyList<ProcessUsageInfo> BuildEnergyImpactList(IReadOnlyList<ProcessUsageInfo> currentUsage)
    {
        Dictionary<string, ProcessUsageInfo> currentByName = currentUsage
            .GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(p => p.CpuPercent).First(), StringComparer.OrdinalIgnoreCase);

        var allTotals = _energyState.ProcessTotals
            .Select(pair => new
            {
                Name = pair.Key,
                Score = pair.Value,
                Current = currentByName.TryGetValue(pair.Key, out ProcessUsageInfo? current) ? current : null
            })
            .Where(item => item.Score > 0.05)
            .ToArray();

        var totals = allTotals
            .OrderByDescending(item => item.Score)
            .Take(5)
            .ToArray();

        double maxScore = totals.Length == 0 ? 1 : Math.Max(1, totals.Max(item => item.Score));
        double totalScore = Math.Max(0.001, allTotals.Sum(item => item.Score));
        double observedBatteryUsedPercent = Math.Max(0, _energyState.StartBatteryPercent - _energyState.LastBatteryPercent);
        return totals.Select(item => new ProcessUsageInfo
        {
            ProcessId = item.Current?.ProcessId ?? 0,
            Name = item.Name,
            CpuPercent = item.Current?.CpuPercent ?? 0,
            WorkingSetBytes = item.Current?.WorkingSetBytes ?? 0,
            RunTime = item.Current?.RunTime ?? TimeSpan.Zero,
            EstimatedEnergyImpact = item.Score,
            EstimatedEnergyImpactBarPercent = Math.Clamp(item.Score / maxScore * 100d, 0, 100),
            EstimatedEnergyPercent = Math.Clamp(item.Score / totalScore * observedBatteryUsedPercent, 0, 100)
        }).ToArray();
    }

    private void EnsureEnergyStateLoaded()
    {
        if (_energyStateLoaded)
        {
            return;
        }

        _energyStateLoaded = true;
        try
        {
            if (File.Exists(EnergyHistoryPath))
            {
                _energyState = JsonSerializer.Deserialize<EnergyHistoryState>(File.ReadAllText(EnergyHistoryPath), JsonOptions) ?? new EnergyHistoryState();
                _energyState.ProcessTotals = new Dictionary<string, double>(_energyState.ProcessTotals, StringComparer.OrdinalIgnoreCase);
                NormalizeEnergyHistoryProcessNames();
            }
        }
        catch (Exception ex)
        {
            LogService.FeatureErrorAny([LogFeature.CpuGpuUsage, LogFeature.BatteryUsage], ex, "Failed to load energy history; starting a new session.");
            _energyState = new EnergyHistoryState();
        }
    }

    private void StartNewEnergySession(DateTimeOffset now, int batteryPercent)
    {
        _energyState = new EnergyHistoryState
        {
            SessionStart = now,
            LastUpdated = now,
            StartBatteryPercent = batteryPercent,
            LastBatteryPercent = batteryPercent,
            IsActive = true
        };
        SaveEnergyStateIfNeeded(now, force: true);
    }

    private void SaveEnergyStateIfNeeded(DateTimeOffset now, bool force)
    {
        if (!force && now - _lastEnergySave < TimeSpan.FromSeconds(30))
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(LogService.AppDataRoot);
            File.WriteAllText(EnergyHistoryPath, JsonSerializer.Serialize(_energyState, JsonOptions));
            _lastEnergySave = now;
        }
        catch (Exception ex)
        {
            LogService.FeatureErrorAny([LogFeature.CpuGpuUsage, LogFeature.BatteryUsage], ex, "Failed to save energy history.");
        }
    }

    private string BuildEnergyImpactTitle(DateTimeOffset now)
    {
        if (!_energyState.IsActive || _energyState.SessionStart == default)
        {
            return "Resource Impact";
        }

        TimeSpan age = now - _energyState.SessionStart;
        string ageText = age.TotalHours >= 1
            ? $"{(int)age.TotalHours}h {age.Minutes}m"
            : $"{Math.Max(1, age.Minutes)}m";
        return $"Resource Impact ({ageText})";
    }

    private string GetFriendlyProcessName(Process process)
    {
        string rawName = string.IsNullOrWhiteSpace(process.ProcessName) ? $"PID {process.Id}" : process.ProcessName;
        if (TryGetKnownProcessName(rawName, out string friendlyName))
        {
            return friendlyName;
        }

        if (_friendlyNameCache.TryGetValue(rawName, out string? cached))
        {
            return cached;
        }

        friendlyName = TryGetVersionName(process) ?? NormalizeProcessName(rawName);
        _friendlyNameCache[rawName] = friendlyName;
        return friendlyName;
    }

    private static bool TryGetKnownProcessName(string rawName, out string friendlyName)
    {
        string normalized = NormalizeProcessName(rawName);
        return KnownProcessNames.TryGetValue(normalized, out friendlyName!);
    }

    private static string? TryGetVersionName(Process process)
    {
        try
        {
            FileVersionInfo? versionInfo = process.MainModule?.FileVersionInfo;
            string? candidate = FirstUsefulVersionString(versionInfo?.ProductName, versionInfo?.FileDescription);
            return candidate is null ? null : NormalizeProcessName(candidate);
        }
        catch
        {
            return null;
        }
    }

    private static string? FirstUsefulVersionString(params string?[] values)
    {
        foreach (string? value in values)
        {
            string normalized = NormalizeProcessName(value);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                continue;
            }

            if (normalized.Equals("Application", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return normalized;
        }

        return null;
    }

    private static string NormalizeProcessName(string? value)
    {
        string normalized = value?.Trim() ?? string.Empty;
        return normalized.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? normalized[..^4]
            : normalized;
    }

    private void NormalizeEnergyHistoryProcessNames()
    {
        if (_energyState.ProcessTotals.Count == 0)
        {
            return;
        }

        var normalizedTotals = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach ((string name, double score) in _energyState.ProcessTotals)
        {
            string displayName = TryGetKnownProcessName(name, out string friendlyName)
                ? friendlyName
                : NormalizeProcessName(name);
            normalizedTotals[displayName] = normalizedTotals.GetValueOrDefault(displayName) + score;
        }

        _energyState.ProcessTotals = normalizedTotals;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx lpBuffer);

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    private readonly record struct ProcessCpuSnapshot(TimeSpan TotalProcessorTime);

    public sealed class EnergyHistoryState
    {
        public DateTimeOffset SessionStart { get; set; }
        public DateTimeOffset LastUpdated { get; set; }
        public int StartBatteryPercent { get; set; }
        public int LastBatteryPercent { get; set; }
        public bool IsActive { get; set; }
        public bool UsesBatteryRate { get; set; }
        public Dictionary<string, double> ProcessTotals { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
