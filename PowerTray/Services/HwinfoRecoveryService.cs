using System.Diagnostics;
using System.IO;
using Microsoft.Win32;
using XPSBatteryTray.Models;

namespace XPSBatteryTray.Services;

public sealed class HwinfoRecoveryService
{
    private const int RequiredConsecutiveFailures = 3;
    private static readonly TimeSpan RestartCooldown = TimeSpan.FromMinutes(10);

    private int _consecutiveSharedMemoryFailures;
    private DateTimeOffset _lastRestartAttempt = DateTimeOffset.MinValue;

    public void Observe(AppSettings settings, SensorReadings hwinfoReadings)
    {
        if (!settings.EnableHwinfoIntegration ||
            !settings.EnableHwinfoAutoRestart ||
            !IsMissingSharedMemoryFromRunningHwinfo(hwinfoReadings.Status))
        {
            _consecutiveSharedMemoryFailures = 0;
            return;
        }

        _consecutiveSharedMemoryFailures++;
        if (_consecutiveSharedMemoryFailures < RequiredConsecutiveFailures)
        {
            return;
        }

        DateTimeOffset now = DateTimeOffset.Now;
        if (now - _lastRestartAttempt < RestartCooldown)
        {
            return;
        }

        _lastRestartAttempt = now;
        _consecutiveSharedMemoryFailures = 0;
        RecoverHwinfo(settings);
    }

    private static bool IsMissingSharedMemoryFromRunningHwinfo(string status) =>
        status.Contains("HWiNFO running", StringComparison.OrdinalIgnoreCase) &&
        status.Contains("shared memory", StringComparison.OrdinalIgnoreCase) &&
        status.Contains("not available", StringComparison.OrdinalIgnoreCase);

    private static void RecoverHwinfo(AppSettings settings)
    {
        string? executablePath = FindHwinfoExecutablePath();
        if (settings.EnableHwinfoPersonalRecoveryScript &&
            !string.IsNullOrWhiteSpace(settings.HwinfoPersonalRecoveryScriptPath) &&
            File.Exists(settings.HwinfoPersonalRecoveryScriptPath))
        {
            RunPersonalRecoveryScript(settings.HwinfoPersonalRecoveryScriptPath, executablePath);
            return;
        }

        RestartHwinfo(executablePath);
    }

    private static void RestartHwinfo(string? executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
        {
            LogService.Error("Cannot auto-restart HWiNFO because HWiNFO64.exe could not be found.");
            return;
        }

        try
        {
            LogService.Info($"Restarting HWiNFO to recover missing shared memory. Path={executablePath}");
            foreach (Process process in Process.GetProcessesByName("HWiNFO64").Concat(Process.GetProcessesByName("HWiNFO")))
            {
                using (process)
                {
                    try
                    {
                        process.Kill(entireProcessTree: true);
                        process.WaitForExit(5000);
                    }
                    catch (Exception ex)
                    {
                        LogService.Error(ex, $"Failed to stop HWiNFO process {process.Id}.");
                    }
                }
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = executablePath,
                WorkingDirectory = Path.GetDirectoryName(executablePath) ?? string.Empty,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            LogService.Error(ex, "Failed to restart HWiNFO.");
        }
    }

    private static void RunPersonalRecoveryScript(string scriptPath, string? hwinfoExecutablePath)
    {
        try
        {
            LogService.Info($"Running personal HWiNFO recovery script. Path={scriptPath}");
            string extension = Path.GetExtension(scriptPath);
            ProcessStartInfo startInfo = extension.ToLowerInvariant() switch
            {
                ".ps1" => new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -ExecutionPolicy Bypass -File {Quote(scriptPath)} -Reason missing-shared-memory -HwinfoPath {Quote(hwinfoExecutablePath ?? string.Empty)}",
                    UseShellExecute = false,
                    CreateNoWindow = true
                },
                ".cmd" or ".bat" => new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c {Quote(scriptPath)} missing-shared-memory {Quote(hwinfoExecutablePath ?? string.Empty)}",
                    UseShellExecute = false,
                    CreateNoWindow = true
                },
                _ => new ProcessStartInfo
                {
                    FileName = scriptPath,
                    Arguments = $"missing-shared-memory {Quote(hwinfoExecutablePath ?? string.Empty)}",
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            startInfo.WorkingDirectory = Path.GetDirectoryName(scriptPath) ?? string.Empty;
            startInfo.Environment["POWERTRAY_HWiNFO_REASON"] = "missing-shared-memory";
            startInfo.Environment["POWERTRAY_HWiNFO_PATH"] = hwinfoExecutablePath ?? string.Empty;
            startInfo.Environment["XPSBATTERYTRAY_HWiNFO_REASON"] = "missing-shared-memory";
            startInfo.Environment["XPSBATTERYTRAY_HWiNFO_PATH"] = hwinfoExecutablePath ?? string.Empty;
            Process.Start(startInfo);
        }
        catch (Exception ex)
        {
            LogService.Error(ex, "Failed to run personal HWiNFO recovery script.");
        }
    }

    private static string? FindHwinfoExecutablePath()
    {
        string? fromProcess = TryGetPathFromRunningProcess();
        if (!string.IsNullOrWhiteSpace(fromProcess) && File.Exists(fromProcess))
        {
            return fromProcess;
        }

        foreach (string? candidate in FindRegistryCandidates().Concat(FindCommonPathCandidates()))
        {
            if (!string.IsNullOrWhiteSpace(candidate) && File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static string? TryGetPathFromRunningProcess()
    {
        foreach (Process process in Process.GetProcessesByName("HWiNFO64").Concat(Process.GetProcessesByName("HWiNFO")))
        {
            using (process)
            {
                try
                {
                    string? path = process.MainModule?.FileName;
                    if (!string.IsNullOrWhiteSpace(path))
                    {
                        return path;
                    }
                }
                catch
                {
                    // Elevated or protected HWiNFO instances can deny module path access.
                }
            }
        }

        return null;
    }

    private static IEnumerable<string?> FindRegistryCandidates()
    {
        string[] uninstallRoots =
        [
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
            @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
        ];

        foreach (RegistryKey root in new[] { Registry.LocalMachine, Registry.CurrentUser })
        {
            foreach (string uninstallRoot in uninstallRoots)
            {
                using RegistryKey? uninstall = root.OpenSubKey(uninstallRoot);
                if (uninstall is null)
                {
                    continue;
                }

                foreach (string subKeyName in uninstall.GetSubKeyNames())
                {
                    using RegistryKey? subKey = uninstall.OpenSubKey(subKeyName);
                    string displayName = subKey?.GetValue("DisplayName") as string ?? string.Empty;
                    if (!displayName.Contains("HWiNFO", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    yield return NormalizeExecutablePath(subKey?.GetValue("DisplayIcon") as string);
                    string? installLocation = subKey?.GetValue("InstallLocation") as string;
                    if (!string.IsNullOrWhiteSpace(installLocation))
                    {
                        yield return Path.Combine(installLocation, "HWiNFO64.exe");
                    }
                }
            }
        }
    }

    private static IEnumerable<string> FindCommonPathCandidates()
    {
        string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        yield return Path.Combine(programFiles, "HWiNFO64", "HWiNFO64.exe");
        yield return Path.Combine(programFiles, "HWiNFO", "HWiNFO64.exe");
        yield return Path.Combine(programFilesX86, "HWiNFO64", "HWiNFO64.exe");
        yield return Path.Combine(programFilesX86, "HWiNFO", "HWiNFO64.exe");
    }

    private static string? NormalizeExecutablePath(string? rawPath)
    {
        if (string.IsNullOrWhiteSpace(rawPath))
        {
            return null;
        }

        string path = rawPath.Trim().Trim('"');
        int exeIndex = path.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        return exeIndex >= 0 ? path[..(exeIndex + 4)] : path;
    }

    private static string Quote(string value) => $"\"{value.Replace("\"", "\\\"")}\"";
}
