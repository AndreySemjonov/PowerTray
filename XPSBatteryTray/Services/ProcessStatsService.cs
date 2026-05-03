using System.Diagnostics;
using System.Runtime.InteropServices;
using XPSBatteryTray.Models;

namespace XPSBatteryTray.Services;

public sealed class ProcessStatsService
{
    private readonly Dictionary<int, ProcessCpuSnapshot> _previousCpu = new();
    private DateTimeOffset _previousSample = DateTimeOffset.Now;
    private double _lastOverallCpu;

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
                    EstimatedEnergyImpact = EstimateEnergyImpact(cpuPercent, runTime)
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

        IReadOnlyList<ProcessUsageInfo> topCpu = usage.OrderByDescending(p => p.CpuPercent).Take(5).ToArray();
        IReadOnlyList<ProcessUsageInfo> topMemory = usage.OrderByDescending(p => p.WorkingSetBytes).Take(5).ToArray();
        IReadOnlyList<ProcessUsageInfo> energy = usage.OrderByDescending(p => p.EstimatedEnergyImpact).Take(5).ToArray();
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

    private static double EstimateEnergyImpact(double cpuPercent, TimeSpan runTime)
    {
        double runtimeFactor = Math.Clamp(runTime.TotalMinutes / 10d, 0.2, 1.5);
        return cpuPercent * runtimeFactor;
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
}
