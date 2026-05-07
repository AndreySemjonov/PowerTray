using System.ComponentModel;
using System.Runtime.InteropServices;
using PowerTray.Models;

namespace PowerTray.Services;

public sealed class WindowsPowerModeService
{
    private static readonly Guid BestPowerEfficiencyGuid = Guid.Parse("961cc777-2547-4f9d-8174-7d86181b8a7a");
    private static readonly Guid BalancedGuid = Guid.Empty;
    private static readonly Guid BestPerformanceGuid = Guid.Parse("ded574b5-45a0-4f42-8737-46345c09c238");
    private string? _unavailableReason;

    public WindowsPowerMode? GetConfiguredMode(bool pluggedIn)
    {
        if (_unavailableReason is not null)
        {
            return null;
        }

        try
        {
            uint result = pluggedIn
                ? PowerGetUserConfiguredACPowerMode(out Guid modeGuid)
                : PowerGetUserConfiguredDCPowerMode(out modeGuid);

            if (result != 0)
            {
                LogPowerModeError(result, "read", pluggedIn);
                return null;
            }

            return FromGuid(modeGuid);
        }
        catch (Exception ex) when (ex is EntryPointNotFoundException or DllNotFoundException)
        {
            _unavailableReason = "Windows power mode APIs are not available on this system.";
            LogService.Error(ex, _unavailableReason);
            return null;
        }
    }

    public string SetConfiguredMode(bool pluggedIn, WindowsPowerMode mode)
    {
        if (_unavailableReason is not null)
        {
            throw new InvalidOperationException(_unavailableReason);
        }

        Guid modeGuid = ToGuid(mode);
        uint result;
        try
        {
            result = pluggedIn
                ? PowerSetUserConfiguredACPowerMode(ref modeGuid)
                : PowerSetUserConfiguredDCPowerMode(ref modeGuid);
        }
        catch (Exception ex) when (ex is EntryPointNotFoundException or DllNotFoundException)
        {
            _unavailableReason = "Windows power mode APIs are not available on this system.";
            LogService.Error(ex, _unavailableReason);
            throw new InvalidOperationException(_unavailableReason, ex);
        }

        if (result == 0)
        {
            string target = pluggedIn ? "plugged in" : "on battery";
            return $"Windows power mode set to {ToDisplayName(mode)} for {target}.";
        }

        LogPowerModeError(result, "set", pluggedIn);
        throw new Win32Exception((int)result, $"Windows could not set power mode: {new Win32Exception((int)result).Message}");
    }

    public static string ToDisplayName(WindowsPowerMode mode) => mode switch
    {
        WindowsPowerMode.BestPowerEfficiency => "Power efficiency",
        WindowsPowerMode.Balanced => "Balanced",
        WindowsPowerMode.BestPerformance => "Performance",
        _ => "Unknown"
    };

    private static WindowsPowerMode? FromGuid(Guid guid)
    {
        if (guid == BestPowerEfficiencyGuid)
        {
            return WindowsPowerMode.BestPowerEfficiency;
        }

        if (guid == BalancedGuid)
        {
            return WindowsPowerMode.Balanced;
        }

        if (guid == BestPerformanceGuid)
        {
            return WindowsPowerMode.BestPerformance;
        }

        return null;
    }

    private static Guid ToGuid(WindowsPowerMode mode) => mode switch
    {
        WindowsPowerMode.BestPowerEfficiency => BestPowerEfficiencyGuid,
        WindowsPowerMode.Balanced => BalancedGuid,
        WindowsPowerMode.BestPerformance => BestPerformanceGuid,
        _ => BalancedGuid
    };

    private static void LogPowerModeError(uint result, string action, bool pluggedIn)
    {
        string target = pluggedIn ? "AC" : "DC";
        LogService.Error(new Win32Exception((int)result), $"Failed to {action} Windows {target} power mode.");
    }

    [DllImport("powrprof.dll", SetLastError = false)]
    private static extern uint PowerGetUserConfiguredACPowerMode(out Guid powerModeGuid);

    [DllImport("powrprof.dll", SetLastError = false)]
    private static extern uint PowerGetUserConfiguredDCPowerMode(out Guid powerModeGuid);

    [DllImport("powrprof.dll", SetLastError = false)]
    private static extern uint PowerSetUserConfiguredACPowerMode(ref Guid powerModeGuid);

    [DllImport("powrprof.dll", SetLastError = false)]
    private static extern uint PowerSetUserConfiguredDCPowerMode(ref Guid powerModeGuid);
}
