namespace XPSBatteryTray.Models;

public sealed class CpuDriverInfo
{
    public string Name { get; init; } = string.Empty;
    public double NowPercent { get; init; }
    public double AveragePercent { get; init; }
    public double MaxPercent { get; init; }
    public double BarPercent { get; init; }
    public TimeSpan ActiveTime { get; init; }
    public string Trend { get; init; } = "steady";

    public string NowAverageText => $"Now {NowPercent:N1}% | Avg {AveragePercent:N1}%";
    public string MaxActiveText => $"Max {MaxPercent:N0}% | Active {FormatDuration(ActiveTime)}";
    public string TrendText => Trend;

    public string ToolTipText => string.Join(Environment.NewLine,
        Name,
        $"Current CPU: {NowPercent:N1}%",
        $"Recent average: {AveragePercent:N1}%",
        $"Recent max: {MaxPercent:N1}%",
        $"Active time: {FormatDuration(ActiveTime)}",
        $"Trend: {Trend}");

    private static string FormatDuration(TimeSpan value)
    {
        if (value.TotalMinutes >= 1)
        {
            return $"{(int)value.TotalMinutes}m {value.Seconds}s";
        }

        return $"{Math.Max(0, (int)value.TotalSeconds)}s";
    }
}
