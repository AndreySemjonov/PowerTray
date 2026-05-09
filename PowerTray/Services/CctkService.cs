using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Principal;
using System.Text.Json;
using PowerTray.Models;

namespace PowerTray.Services;

public sealed class CctkService
{
    private const string QueryArgument = "--PrimaryBattChargeCfg";
    private const string ThermalQueryArgument = "--thermalmanagement";
    private readonly SettingsService _settingsService;

    public CctkService(SettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    public static string? FindDefaultCctkPath()
    {
        string[] candidates =
        [
            @"C:\Program Files (x86)\Dell\Command Configure\X86_64\cctk.exe",
            @"C:\Program Files\Dell\Command Configure\X86_64\cctk.exe"
        ];

        return candidates.FirstOrDefault(File.Exists);
    }

    public bool IsConfigured => File.Exists(_settingsService.Current.CctkPath);

    public static bool IsAdministrator()
    {
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    public async Task<CommandResult> ShowCurrentAsync(bool allowElevation = false)
    {
        CommandResult result = await ExecuteAsync(QueryArgument, requiresAdmin: false);
        if (result.Success || !allowElevation || IsAdministrator())
        {
            return result;
        }

        return await ExecuteAsync(QueryArgument, requiresAdmin: true);
    }

    public async Task<CommandResult> ShowThermalManagementAsync(bool allowElevation = false)
    {
        CommandResult result = await ExecuteAsync(ThermalQueryArgument, requiresAdmin: false);
        if (result.Success || !allowElevation || IsAdministrator())
        {
            return result;
        }

        return await ExecuteAsync(ThermalQueryArgument, requiresAdmin: true);
    }

    public async Task<CommandResult> ApplyThermalProfileAsync(DellThermalProfile profile)
    {
        string argument = $"--thermalmanagement={ToCctkThermalValue(profile)}";
        return await ExecuteAsync(argument, requiresAdmin: true);
    }

    public async Task<CommandResult> ApplyPresetAsync(BatteryPreset preset)
    {
        AppSettings settings = _settingsService.Current;
        if (preset == BatteryPreset.Health)
        {
            return await ApplyCustomPresetAsync(settings.HealthStart, settings.HealthStop, "Battery Health");
        }

        if (preset == BatteryPreset.Balanced)
        {
            return await ApplyCustomPresetAsync(settings.BalancedStart, settings.BalancedStop, "Balanced");
        }

        string argument = preset switch
        {
            BatteryPreset.Standard => "--PrimaryBattChargeCfg=Standard",
            BatteryPreset.PrimarilyAcUse => "--PrimaryBattChargeCfg=PrimAcUse",
            BatteryPreset.Adaptive => "--PrimaryBattChargeCfg=Adaptive",
            _ => throw new ArgumentOutOfRangeException(nameof(preset), preset, null)
        };

        return await ExecuteAsync(argument, requiresAdmin: true);
    }

    public async Task<CommandResult> ExecuteAsync(string argument, bool requiresAdmin)
    {
        string cctkPath = _settingsService.Current.CctkPath;
        if (!File.Exists(cctkPath))
        {
            string message = "Dell Command | Configure cctk.exe was not found. Open Settings and select cctk.exe.";
            return new CommandResult { Success = false, ExitCode = -1, Message = message, StandardError = message };
        }

        if (requiresAdmin && !IsAdministrator())
        {
            return await ExecuteElevatedViaSelfAsync(cctkPath, argument);
        }

        return await RunCctkAsync(cctkPath, argument);
    }

    public static async Task<CommandResult> RunCctkAsync(string cctkPath, string argument)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = cctkPath,
                Arguments = argument,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using Process process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start cctk.exe.");
            string stdout = await process.StandardOutput.ReadToEndAsync();
            string stderr = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            string message = string.IsNullOrWhiteSpace(stderr) ? stdout.Trim() : stderr.Trim();
            if (string.IsNullOrWhiteSpace(message))
            {
                message = process.ExitCode == 0 ? "Command completed." : $"Command failed with exit code {process.ExitCode}.";
            }

            var result = new CommandResult
            {
                Success = process.ExitCode == 0,
                ExitCode = process.ExitCode,
                StandardOutput = stdout,
                StandardError = stderr,
                Message = message
            };
            LogService.Info($"cctk {argument} -> exit {result.ExitCode}. stdout: {stdout.Trim()} stderr: {stderr.Trim()}");
            return result;
        }
        catch (Exception ex)
        {
            LogService.Error(ex, "Failed to run cctk.exe.");
            return new CommandResult { Success = false, ExitCode = -1, Message = ex.Message, StandardError = ex.ToString() };
        }
    }

    public static async Task<int> RunElevatedCommandChildAsync(string[] args)
    {
        if (args.Length < 4)
        {
            return -2;
        }

        string cctkPath = args[1];
        string argument = args[2];
        string outputPath = args[3];
        CommandResult result = await RunCctkAsync(cctkPath, argument);
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        await File.WriteAllTextAsync(outputPath, JsonSerializer.Serialize(result));
        return result.ExitCode;
    }

    private static async Task<CommandResult> ExecuteElevatedViaSelfAsync(string cctkPath, string argument)
    {
        (string fileName, string prefixArguments) = GetSelfLaunchInfo();
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return new CommandResult { Success = false, ExitCode = -1, Message = "Could not locate the tray executable for elevation." };
        }

        string outputPath = Path.Combine(Path.GetTempPath(), "PowerTray", $"{Guid.NewGuid():N}.json");
        string elevatedArgs = $"{prefixArguments} --run-cctk {Quote(cctkPath)} {Quote(argument)} {Quote(outputPath)}".Trim();

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = elevatedArgs,
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden
            };

            using Process process = Process.Start(startInfo) ?? throw new InvalidOperationException("The elevated cctk helper did not start.");
            await process.WaitForExitAsync();

            if (File.Exists(outputPath))
            {
                string json = await File.ReadAllTextAsync(outputPath);
                File.Delete(outputPath);
                CommandResult result = JsonSerializer.Deserialize<CommandResult>(json) ??
                       new CommandResult { Success = false, ExitCode = process.ExitCode, Message = "Elevated command returned no readable result." };
                LogService.Info($"elevated cctk {argument} -> exit {result.ExitCode}. stdout: {result.StandardOutput.Trim()} stderr: {result.StandardError.Trim()}");
                return result;
            }

            return new CommandResult
            {
                Success = false,
                ExitCode = process.ExitCode,
                Message = process.ExitCode == 0
                    ? "Elevated command completed but no result was returned."
                    : "Administrator approval was cancelled or the elevated command failed."
            };
        }
        catch (Win32Exception ex) when ((uint)ex.NativeErrorCode == 1223)
        {
            return new CommandResult { Success = false, ExitCode = -1, Message = "Administrator approval was cancelled." };
        }
        catch (Exception ex)
        {
            LogService.Error(ex, "Failed to launch elevated cctk helper.");
            return new CommandResult { Success = false, ExitCode = -1, Message = ex.Message, StandardError = ex.ToString() };
        }
    }

    private static string Quote(string value) => "\"" + value.Replace("\"", "\\\"") + "\"";

    private static string ToCctkThermalValue(DellThermalProfile profile) => profile switch
    {
        DellThermalProfile.Optimized => "optimized",
        DellThermalProfile.Cool => "cool",
        DellThermalProfile.Quiet => "quiet",
        DellThermalProfile.UltraPerformance => "ultraperformance",
        _ => "optimized"
    };

    private async Task<CommandResult> ApplyCustomPresetAsync(int start, int stop, string label)
    {
        if (start < 50 || start > 95 || stop < 55 || stop > 100 || stop - start < 5)
        {
            return new CommandResult
            {
                Success = false,
                ExitCode = -1,
                Message = $"{label} range is invalid. Custom start must be 50-95, stop must be 55-100, and stop-start must be at least 5."
            };
        }

        string expected = $"Custom:{start}-{stop}";
        string directArgument = $"--PrimaryBattChargeCfg={expected}";
        CommandResult direct = await ExecuteAsync(directArgument, requiresAdmin: true);

        if (direct.Success && IsCustomVerified(CombineOutput(direct), start, stop))
        {
            return new CommandResult
            {
                Success = true,
                ExitCode = direct.ExitCode,
                StandardOutput = direct.StandardOutput,
                StandardError = direct.StandardError,
                Message = $"{label} applied: {expected}"
            };
        }

        CommandResult? verification = null;
        if (direct.Success && IsAdministrator())
        {
            verification = await QueryCustomChargeAsync();
            if (IsCustomVerified(CombineOutput(verification), start, stop))
            {
                return new CommandResult
                {
                    Success = true,
                    ExitCode = direct.ExitCode,
                    StandardOutput = verification.StandardOutput,
                    StandardError = direct.StandardError,
                    Message = $"{label} applied: {expected}"
                };
            }
        }

        LogService.Info($"{label} direct custom command did not verify. Direct: {direct.Message}. Verification: {CombineOutput(verification).Trim()}");

        string fallbackArgument = $"--PrimaryBattChargeCfg=Custom --CustomChargeStart={start} --CustomChargeStop={stop}";
        CommandResult fallback = await ExecuteAsync(fallbackArgument, requiresAdmin: true);

        if (fallback.Success && IsCustomVerified(CombineOutput(fallback), start, stop))
        {
            return new CommandResult
            {
                Success = true,
                ExitCode = fallback.ExitCode,
                StandardOutput = fallback.StandardOutput,
                StandardError = fallback.StandardError,
                Message = $"{label} applied: {expected}"
            };
        }

        CommandResult? fallbackVerification = null;
        if (fallback.Success && IsAdministrator())
        {
            fallbackVerification = await QueryCustomChargeAsync();
            if (IsCustomVerified(CombineOutput(fallbackVerification), start, stop))
            {
                return new CommandResult
                {
                    Success = true,
                    ExitCode = fallback.ExitCode,
                    StandardOutput = fallbackVerification.StandardOutput,
                    StandardError = fallback.StandardError,
                    Message = $"{label} applied: {expected}"
                };
            }
        }

        string message = fallback.Success
            ? $"{label} command completed, but Dell did not report {expected}. Current: {CombineOutput(fallbackVerification).Trim()}"
            : $"{label} failed. Dell output: {fallback.Message}";

        return new CommandResult
        {
            Success = false,
            ExitCode = fallback.ExitCode,
            StandardOutput = $"{direct.StandardOutput}{Environment.NewLine}{verification?.StandardOutput}{Environment.NewLine}{fallback.StandardOutput}{Environment.NewLine}{fallbackVerification?.StandardOutput}",
            StandardError = $"{direct.StandardError}{Environment.NewLine}{verification?.StandardError}{Environment.NewLine}{fallback.StandardError}{Environment.NewLine}{fallbackVerification?.StandardError}",
            Message = message
        };
    }

    private Task<CommandResult> QueryCustomChargeAsync() =>
        ExecuteAsync("--PrimaryBattChargeCfg --CustomChargeStart --CustomChargeStop", requiresAdmin: false);

    private static bool IsCustomVerified(string output, int start, int stop)
    {
        string normalized = string.Concat(output.Where(c => !char.IsWhiteSpace(c)));
        return normalized.Contains($"PrimaryBattChargeCfg=Custom:{start}-{stop}", StringComparison.OrdinalIgnoreCase) ||
               (normalized.Contains("PrimaryBattChargeCfg=Custom", StringComparison.OrdinalIgnoreCase) &&
                normalized.Contains($"CustomChargeStart={start}", StringComparison.OrdinalIgnoreCase) &&
                normalized.Contains($"CustomChargeStop={stop}", StringComparison.OrdinalIgnoreCase));
    }

    private static string CombineOutput(CommandResult? result)
    {
        if (result is null)
        {
            return string.Empty;
        }

        return $"{result.StandardOutput}{Environment.NewLine}{result.StandardError}{Environment.NewLine}{result.Message}";
    }

    private static (string FileName, string PrefixArguments) GetSelfLaunchInfo()
    {
        string? processPath = Environment.ProcessPath;
        string assemblyPath = Assembly.GetEntryAssembly()?.Location ?? string.Empty;
        if (string.IsNullOrWhiteSpace(processPath))
        {
            return (string.Empty, string.Empty);
        }

        if (Path.GetFileName(processPath).Equals("dotnet.exe", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(assemblyPath))
        {
            return (processPath, Quote(assemblyPath));
        }

        return (processPath, string.Empty);
    }
}
