using System.IO;

namespace XPSBatteryTray.Services;

public static class LogService
{
    private const string AppDataFolderName = "PowerTray";
    private const string LegacyAppDataFolderName = "DellBatteryTray";

    public static string AppDataRoot { get; } = InitializeAppDataRoot();

    public static string LegacyAppDataRoot { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), LegacyAppDataFolderName);

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

    private static string InitializeAppDataRoot()
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string currentRoot = Path.Combine(appData, AppDataFolderName);
        string legacyRoot = Path.Combine(appData, LegacyAppDataFolderName);

        try
        {
            if (Directory.Exists(legacyRoot))
            {
                Directory.CreateDirectory(currentRoot);
                CopyMissingFiles(legacyRoot, currentRoot);
            }
        }
        catch
        {
            // Migration is best effort; the app can continue with the new folder.
        }

        return currentRoot;
    }

    private static void CopyMissingFiles(string sourceRoot, string targetRoot)
    {
        foreach (string directory in Directory.EnumerateDirectories(sourceRoot, "*", SearchOption.AllDirectories))
        {
            string relativePath = Path.GetRelativePath(sourceRoot, directory);
            Directory.CreateDirectory(Path.Combine(targetRoot, relativePath));
        }

        foreach (string sourceFile in Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories))
        {
            string relativePath = Path.GetRelativePath(sourceRoot, sourceFile);
            string targetFile = Path.Combine(targetRoot, relativePath);
            if (File.Exists(targetFile))
            {
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(targetFile)!);
            File.Copy(sourceFile, targetFile);
        }
    }
}
