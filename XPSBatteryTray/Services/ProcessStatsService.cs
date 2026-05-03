using System.Diagnostics;
using System.Runtime.InteropServices;
using XPSBatteryTray.Models;

namespace XPSBatteryTray.Services;

public sealed class ProcessStatsService
{
    private readonly Dictionary<int, ProcessCpuSnapshot> _previousCpu = new();
    private readonly Dictionary<string, Queue<EnergySample>> _energyHistory = new(StringComparer.OrdinalIgnoreCase);
    private DateTimeOffset _previousSample = DateTimeOffset.Now;
    private double _lastOverallCpu;
    private static readonly TimeSpan EnergyWindow = TimeSpan.FromMinutes(10);

    public (double OverallCpuPercent, IReadOnlyList<ProcessUsageInfo> TopCpu, IReadOnlyList<ProcessUsageInfo> TopMemory, IReadOnlyList<ProcessUsageInfo> EnergyImpact) Sample()
    {
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
                    Name = string.IsNullOrWhiteSpace(process.ProcessName) ? $"PID {process.Id}" : process.ProcessName,
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
        UpdateEnergyHistory(usage, elapsedSeconds, now);

        IReadOnlyList<ProcessUsageInfo> topCpu = usage.OrderByDescending(p => p.CpuPercent).Take(5).ToArray();
        IReadOnlyList<ProcessUsageInfo> topMemory = usage.OrderByDescending(p => p.WorkingSetBytes).Take(5).ToArray();
        IReadOnlyList<ProcessUsageInfo> energy = BuildEnergyImpactList(usage).Take(5).ToArray();
        return (_lastOverallCpu, topCpu, topMemory, energy);
    }

    public SystemMemoryInfo GetMemoryInfo()
    {
        var status = new MemoryStatusEx();
        status.dwLength = (uint)Marshal.SizeOf<MemoryStatusEx>();
        return GlobalMemoryStatusEx(ref status)
            ? new SystemMemoryInfo { TotalBytes = status.ullTotalPhys, AvailableBytes = status.ullAvailPhys }
            : new SystemMemoryInfo();
    }

    private void UpdateEnergyHistory(IEnumerable<ProcessUsageInfo> usage, double elapsedSeconds, DateTimeOffset now)
    {
        DateTimeOffset cutoff = now - EnergyWindow;
        foreach (ProcessUsageInfo process in usage)
        {
            if (process.CpuPercent <= 0.05)
            {
                continue;
            }

            if (!_energyHistory.TryGetValue(process.Name, out Queue<EnergySample>? samples))
            {
                samples = new Queue<EnergySample>();
                _energyHistory[process.Name] = samples;
            }

            // CPU-percent minutes over the rolling window. A process using 10% CPU for 10 minutes scores 100.
            samples.Enqueue(new EnergySample(now, process.CpuPercent * elapsedSeconds / 60d));
        }

        foreach (string name in _energyHistory.Keys.ToArray())
        {
            Queue<EnergySample> samples = _energyHistory[name];
            while (samples.Count > 0 && samples.Peek().Timestamp < cutoff)
            {
                samples.Dequeue();
            }

            if (samples.Count == 0)
            {
                _energyHistory.Remove(name);
            }
        }
    }

    private IReadOnlyList<ProcessUsageInfo> BuildEnergyImpactList(IReadOnlyList<ProcessUsageInfo> currentUsage)
    {
        Dictionary<string, ProcessUsageInfo> currentByName = currentUsage
            .GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(p => p.CpuPercent).First(), StringComparer.OrdinalIgnoreCase);

        var totals = _energyHistory
            .Select(pair => new
            {
                Name = pair.Key,
                Score = pair.Value.Sum(sample => sample.Score),
                Current = currentByName.TryGetValue(pair.Key, out ProcessUsageInfo? current) ? current : null
            })
            .Where(item => item.Score > 0.05)
            .OrderByDescending(item => item.Score)
            .Take(5)
            .ToArray();

        double maxScore = totals.Length == 0 ? 1 : Math.Max(1, totals.Max(item => item.Score));
        return totals.Select(item => new ProcessUsageInfo
        {
            ProcessId = item.Current?.ProcessId ?? 0,
            Name = item.Name,
            CpuPercent = item.Current?.CpuPercent ?? 0,
            WorkingSetBytes = item.Current?.WorkingSetBytes ?? 0,
            RunTime = item.Current?.RunTime ?? TimeSpan.Zero,
            EstimatedEnergyImpact = item.Score,
            EstimatedEnergyImpactBarPercent = Math.Clamp(item.Score / maxScore * 100d, 0, 100)
        }).ToArray();
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

    private readonly record struct EnergySample(DateTimeOffset Timestamp, double Score);
}
