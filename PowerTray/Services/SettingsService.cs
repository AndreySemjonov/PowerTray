using System.IO;
using System.Text.Json;
using PowerTray.Models;

namespace PowerTray.Services;

public sealed class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public string SettingsPath => Path.Combine(LogService.AppDataRoot, "settings.json");

    public AppSettings Current { get; private set; } = new();

    public AppSettings Load()
    {
        try
        {
            Directory.CreateDirectory(LogService.AppDataRoot);
            if (File.Exists(SettingsPath))
            {
                Current = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath), JsonOptions) ?? new AppSettings();
            }
        }
        catch (Exception ex)
        {
            LogService.Error(ex, "Failed to load settings; using defaults.");
            Current = new AppSettings();
        }

        if (string.IsNullOrWhiteSpace(Current.CctkPath))
        {
            Current.CctkPath = CctkService.FindDefaultCctkPath() ?? string.Empty;
        }

        Current.SensorSampleIntervalSeconds = Math.Clamp(Current.SensorSampleIntervalSeconds, 1, 60);
        Current.ScreenDimmerLevel = Math.Clamp(Current.ScreenDimmerLevel, 0, 90);
        ApplyFeatureLogging(Current);
        return Current;
    }

    public void Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(LogService.AppDataRoot);
            settings.SensorSampleIntervalSeconds = Math.Clamp(settings.SensorSampleIntervalSeconds, 1, 60);
            settings.ScreenDimmerLevel = Math.Clamp(settings.ScreenDimmerLevel, 0, 90);
            Current = settings;
            ApplyFeatureLogging(Current);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(Current, JsonOptions));
        }
        catch (Exception ex)
        {
            LogService.Error(ex, "Failed to save settings.");
            throw;
        }
    }

    private static void ApplyFeatureLogging(AppSettings settings)
    {
        LogService.ConfigureFeatureLogging(
            settings.ShowBatteryWattsTile,
            settings.ShowCpuGpuUsageTile,
            settings.ShowBatteryUsageSection);
    }
}
