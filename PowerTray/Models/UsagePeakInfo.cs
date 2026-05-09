namespace PowerTray.Models;

public sealed class UsagePeakInfo
{
    public int Rank { get; init; }
    public int Index { get; init; }
    public DateTimeOffset Timestamp { get; init; }
    public double Value { get; init; }
    public string? ProcessName { get; init; }
    public string TimeAgoText { get; init; } = string.Empty;
    public string ValueFormat { get; init; } = "N0";
    public string ValuePrefix { get; init; } = string.Empty;
    public string ValueUnit { get; init; } = "%";

    public string ValueText => $"{ValuePrefix}{Value.ToString(ValueFormat)}{ValueUnit}";
    public string ProcessText => string.IsNullOrWhiteSpace(ProcessName) ? string.Empty : ProcessName;
    public string LabelText => string.IsNullOrWhiteSpace(ProcessName)
        ? ValueText
        : $"{ValueText} - {FormatCompactProcessName(ProcessName)}";

    private static string FormatCompactProcessName(string value) =>
        value.Length <= 22 ? value : $"{value[..19]}...";
}
