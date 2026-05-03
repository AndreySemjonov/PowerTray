using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Principal;
using System.Text.Json;
using XPSBatteryTray.Models;

namespace XPSBatteryTray.Services;

public sealed class CctkService
{
    private const string QueryArgument = "--PrimaryBattChargeCfg";
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

    public Task<CommandResult> ShowCurrentAsync() => ExecuteAsync(QueryArgument, requiresAdmin: false);

    public Task<CommandResult> ApplyPresetAsync(BatteryPreset preset)
    {
        AppSettings settings = _settingsService.Current;
        string argument = preset switch
        {
            BatteryPreset.Health => $"--PrimaryBattChargeCfg=Custom:{settings.HealthStart}-{settings.HealthStop}",
            BatteryPreset.Balanced => $"--PrimaryBattChargeCfg=Custom:{settings.BalancedStart}-{settings.BalancedStop}",
            BatteryPreset.Standard => "--PrimaryBattChargeCfg=Standard",
            BatteryPreset.PrimarilyAcUse => "--PrimaryBattChargeCfg=PrimAcUse",
            BatteryPreset.Adaptive => "--PrimaryBattChargeCfg=Adaptive",
            _ => throw new ArgumentOutOfRangeException(nameof(preset), preset, null)
        };

        return ExecuteAsync(argument, requiresAdmin: true);
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

            return new CommandResult
            {
                Success = process.ExitCode == 0,
                ExitCode = process.ExitCode,
                StandardOutput = stdout,
                StandardError = stderr,
                Message = message
            };
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

        string outputPath = Path.Combine(Path.GetTempPath(), "XPSBatteryTray", $"{Guid.NewGuid():N}.json");
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
                return JsonSerializer.Deserialize<CommandResult>(json) ??
                       new CommandResult { Success = false, ExitCode = process.ExitCode, Message = "Elevated command returned no readable result." };
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
