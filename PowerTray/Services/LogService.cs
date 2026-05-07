using System.IO;

namespace PowerTray.Services;

public static class LogService
{
    private const string AppDataFolderName = "PowerTray";

    public static string AppDataRoot { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppDataFolderName);

    public static string LogPath { get; } = Path.Combine(AppDataRoot, "logs", "app.log");

    public static void Info(string message) => Write("INFO", message);

    public static void Error(Exception exception, string message) =>
        Write("ERROR", $"{message}{Environment.NewLine}{exception}");

    public static void Error(string message) => Write("ERROR", message);

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
