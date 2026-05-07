namespace PowerTray.Models;

public sealed class WindowsBatteryUsageInfo
{
    public string Name { get; init; } = string.Empty;
    public double Percent { get; init; }
    public double BarPercent { get; init; }
    public double EnergyMilliJoules { get; init; }
    public double ForegroundMinutes { get; init; }
    public double BackgroundMinutes { get; init; }
    public int SourceRowCount { get; init; }
    public bool IsOther { get; init; }

    public string PercentText => $"{Percent:N0}%";
    public string DetailText
    {
        get
        {
            string foreground = ForegroundMinutes > 0 ? FormatMinutes(ForegroundMinutes) : "--";
            string background = BackgroundMinutes > 0 ? FormatMinutes(BackgroundMinutes) : "--";
            return IsOther ? $"{SourceRowCount:N0} smaller apps/services" : $"In use {foreground} | Background {background}";
        }
    }

    public string ToolTipText => string.Join(Environment.NewLine,
        Name,
        $"Share: {Percent:N1}%",
        $"Energy: {EnergyMilliJoules:N0} mJ",
        DetailText,
        "Source: Windows SRUM / Energy Estimation Engine.");

    private static string FormatMinutes(double minutes) =>
        minutes >= 60 ? $"{(int)(minutes / 60)}h {(int)(minutes % 60)}m" : $"{Math.Max(1, (int)Math.Round(minutes))}m";
}
