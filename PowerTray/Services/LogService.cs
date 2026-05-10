using System.IO;

namespace PowerTray.Services;

public enum LogFeature
{
    BatteryWatts,
    CpuGpuUsage,
    BatteryUsage
}

public static class LogService
{
    private const string AppDataFolderName = "PowerTray";
    private static readonly object FeatureSync = new();
    private static readonly Dictionary<LogFeature, bool> FeatureLoggingEnabled = new()
    {
        [LogFeature.BatteryWatts] = true,
        [LogFeature.CpuGpuUsage] = true,
        [LogFeature.BatteryUsage] = true
    };

    public static string AppDataRoot { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppDataFolderName);

    public static string LogPath { get; } = Path.Combine(AppDataRoot, "logs", "app.log");

    public static void ConfigureFeatureLogging(bool batteryWatts, bool cpuGpuUsage, bool batteryUsage)
    {
        lock (FeatureSync)
        {
            FeatureLoggingEnabled[LogFeature.BatteryWatts] = batteryWatts;
            FeatureLoggingEnabled[LogFeature.CpuGpuUsage] = cpuGpuUsage;
            FeatureLoggingEnabled[LogFeature.BatteryUsage] = batteryUsage;
        }
    }

    public static void Info(string message) => Write("INFO", message);

    public static void FeatureInfo(LogFeature feature, string message)
    {
        if (IsFeatureLoggingEnabled(feature))
        {
            Info(message);
        }
    }

    public static void FeatureInfoAny(IReadOnlyCollection<LogFeature> features, string message)
    {
        if (IsAnyFeatureLoggingEnabled(features))
        {
            Info(message);
        }
    }

    public static void Error(Exception exception, string message) =>
        Write("ERROR", $"{message}{Environment.NewLine}{exception}");

    public static void FeatureError(LogFeature feature, Exception exception, string message)
    {
        if (IsFeatureLoggingEnabled(feature))
        {
            Error(exception, message);
        }
    }

    public static void FeatureErrorAny(IReadOnlyCollection<LogFeature> features, Exception exception, string message)
    {
        if (IsAnyFeatureLoggingEnabled(features))
        {
            Error(exception, message);
        }
    }

    public static void Error(string message) => Write("ERROR", message);

    private static bool IsFeatureLoggingEnabled(LogFeature feature)
    {
        lock (FeatureSync)
        {
            return FeatureLoggingEnabled.GetValueOrDefault(feature, true);
        }
    }

    private static bool IsAnyFeatureLoggingEnabled(IReadOnlyCollection<LogFeature> features)
    {
        lock (FeatureSync)
        {
            return features.Any(feature => FeatureLoggingEnabled.GetValueOrDefault(feature, true));
        }
    }

    private static void Write(string level, string message)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            File.AppendAllText(LogPath, $"{DateTimeOffset.Now:O} [{level}] {message}{Environment.NewLine}");
        }
        catch
        {
            // Logging must never bring down the tray utility.
        }
    }

}
