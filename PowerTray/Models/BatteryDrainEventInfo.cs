namespace PowerTray.Models;

public sealed class BatteryDrainEventInfo
{
    public string Title { get; init; } = string.Empty;
    public string ValueText { get; init; } = string.Empty;
    public string DetailText { get; init; } = string.Empty;
    public string ContextText { get; init; } = string.Empty;
    public double BarPercent { get; init; }

    public string ToolTipText => string.Join(Environment.NewLine, new[]
    {
        Title,
        ValueText,
        DetailText,
        ContextText
    }.Where(text => !string.IsNullOrWhiteSpace(text)));
}
