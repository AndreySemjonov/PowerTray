using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using XPSBatteryTray.Models;
using MediaBrush = System.Windows.Media.Brush;
using MediaColor = System.Windows.Media.Color;
using MediaPen = System.Windows.Media.Pen;
using WindowsPoint = System.Windows.Point;

namespace XPSBatteryTray.Controls;

public sealed class BatteryUsageCombinedControl : FrameworkElement
{
    public static readonly DependencyProperty BucketsProperty =
        DependencyProperty.Register(nameof(Buckets), typeof(IEnumerable<BatteryUsageBucket>), typeof(BatteryUsageCombinedControl),
            new FrameworkPropertyMetadata(Array.Empty<BatteryUsageBucket>(), FrameworkPropertyMetadataOptions.AffectsRender));

    public IEnumerable<BatteryUsageBucket> Buckets
    {
        get => (IEnumerable<BatteryUsageBucket>)GetValue(BucketsProperty);
        set => SetValue(BucketsProperty, value);
    }

    private HoverSelection? _hoverSelection;

    public BatteryUsageCombinedControl()
    {
        ToolTip = new System.Windows.Controls.ToolTip
        {
            Placement = PlacementMode.Mouse,
            StaysOpen = true
        };
        MouseMove += OnMouseMove;
        MouseLeave += OnMouseLeave;
    }

    protected override void OnRender(DrawingContext context)
    {
        base.OnRender(context);
        BatteryUsageBucket[] buckets = Buckets?.ToArray() ?? [];
        var bounds = new Rect(0, 0, ActualWidth, ActualHeight);
        if (bounds.Width < 80 || bounds.Height < 80 || buckets.Length == 0)
        {
            DrawCollecting(context, bounds);
            return;
        }

        ChartLayout layout = CreateLayout(ActualWidth, ActualHeight);

        DrawNoDataBands(context, buckets, layout.Left, layout.PlotWidth, layout.Top, layout.PlotHeight);
        DrawExternalPowerBands(context, buckets, layout.Left, layout.PlotWidth, layout.Top, layout.PlotHeight);
        DrawHoverHighlight(context, buckets, layout);
        DrawGrid(context, layout.Left, layout.PlotWidth, layout.Top, layout.PlotHeight);
        DrawBars(context, buckets, layout.Left, layout.PlotWidth, layout.Top, layout.PlotHeight);
        DrawPowerModeLane(context, buckets, layout.Left, layout.PlotWidth, layout.PowerModeLaneY);
        DrawLaneSeparators(context, layout.Left, layout.PlotWidth, layout.PowerModeLaneY, layout.AverageWattsLaneY);
        DrawAverageWattsLane(context, buckets, layout.Left, layout.PlotWidth, layout.AverageWattsLaneY);
        DrawCurrentMarker(context, buckets, layout.Left, layout.PlotWidth, layout.Top, layout.PlotHeight);
        DrawAxisLabels(context, layout.Left + layout.PlotWidth + 10, layout.Top, layout.PlotHeight);
        DrawText(context, "Avg W", 10, new SolidColorBrush(MediaColor.FromRgb(142, 149, 158)), layout.Left + layout.PlotWidth + 10, layout.AverageWattsLaneY);
        DrawText(context, "Time", 10, new SolidColorBrush(MediaColor.FromRgb(142, 149, 158)), layout.Left + layout.PlotWidth + 10, layout.TimeLabelsY);
        DrawTimeLabels(context, layout.Left, layout.PlotWidth, layout.TimeLabelsY);
    }

    private static ChartLayout CreateLayout(double width, double height)
    {
        const double leftPadding = 8;
        const double rightPadding = 46;
        const double topPadding = 26;
        const double bottomPadding = 72;
        double plotWidth = Math.Max(1, width - leftPadding - rightPadding);
        double plotHeight = Math.Max(1, height - topPadding - bottomPadding);
        double bottom = topPadding + plotHeight;
        return new ChartLayout(
            leftPadding,
            topPadding,
            plotWidth,
            plotHeight,
            bottom + 8,
            bottom + 18,
            bottom + 47);
    }

    private static void DrawNoDataBands(DrawingContext context, IReadOnlyList<BatteryUsageBucket> buckets, double left, double width, double top, double height)
    {
        double slot = width / buckets.Count;
        var bandBrush = new SolidColorBrush(MediaColor.FromArgb(42, 36, 40, 45));
        foreach ((double start, double end) in Ranges(buckets, b => b.Kind == BatteryUsageBucketKind.NoData))
        {
            double x = left + start * slot;
            double w = Math.Max(2, (end - start) * slot);
            context.DrawRectangle(bandBrush, null, new Rect(x, top, w, height));
        }
    }

    private static void DrawExternalPowerBands(DrawingContext context, IReadOnlyList<BatteryUsageBucket> buckets, double left, double width, double top, double height)
    {
        double slot = width / buckets.Count;
        DrawPowerBand(context, buckets, left, top, height, slot, b => b.IsCharging, MediaColor.FromArgb(70, 58, 122, 51), "\u26A1", MediaColor.FromRgb(163, 232, 105));
        DrawPowerBand(context, buckets, left, top, height, slot, b => b.Kind == BatteryUsageBucketKind.ChargeHold, MediaColor.FromArgb(74, 35, 107, 108), "\u2161", MediaColor.FromRgb(84, 214, 198));
    }

    private static void DrawPowerBand(
        DrawingContext context,
        IReadOnlyList<BatteryUsageBucket> buckets,
        double left,
        double top,
        double height,
        double slot,
        Func<BatteryUsageBucket, bool> predicate,
        MediaColor bandColor,
        string markerText,
        MediaColor markerColor)
    {
        var bandBrush = new SolidColorBrush(bandColor);
        var markerBrush = new SolidColorBrush(markerColor);
        foreach ((double start, double end) in Ranges(buckets, predicate))
        {
            double x = left + start * slot;
            double w = Math.Max(2, (end - start) * slot);
            context.DrawRoundedRectangle(bandBrush, null, new Rect(x, top, w, height), 4, 4);

            var marker = FormatText(markerText, 20, markerBrush, "Segoe UI Symbol");
            context.DrawText(marker, new WindowsPoint(x + w / 2d - marker.Width / 2d, Math.Max(0, top - 24)));
        }
    }

    private static IEnumerable<(double Start, double End)> Ranges(IReadOnlyList<BatteryUsageBucket> buckets, Func<BatteryUsageBucket, bool> predicate)
    {
        bool inRange = false;
        int start = 0;
        for (int i = 0; i <= buckets.Count; i++)
        {
            bool active = i < buckets.Count && predicate(buckets[i]);
            if (active && !inRange)
            {
                start = i;
                inRange = true;
            }
            else if (!active && inRange)
            {
                inRange = false;
                yield return (start, i);
            }
        }
    }

    private static void DrawGrid(DrawingContext context, double left, double width, double top, double height)
    {
        var gridPen = new MediaPen(new SolidColorBrush(MediaColor.FromArgb(74, 85, 91, 99)), 1);
        foreach (double percent in new[] { 100d, 50d, 0d })
        {
            double y = PercentToY(percent, top, height);
            context.DrawLine(gridPen, new WindowsPoint(left, y), new WindowsPoint(left + width, y));
        }

        var thresholdPen = new MediaPen(new SolidColorBrush(MediaColor.FromArgb(52, 85, 91, 99)), 1)
        {
            DashStyle = new DashStyle([4, 4], 0)
        };
        foreach (double percent in new[] { 75d, 25d })
        {
            double y = PercentToY(percent, top, height);
            context.DrawLine(thresholdPen, new WindowsPoint(left, y), new WindowsPoint(left + width, y));
        }
    }

    private static void DrawBars(DrawingContext context, IReadOnlyList<BatteryUsageBucket> buckets, double left, double width, double top, double height)
    {
        double slot = width / buckets.Count;
        double gap = Math.Clamp(slot * 0.32, 1.2, 4);
        double barWidth = Math.Max(2.5, slot - gap);
        for (int i = 0; i < buckets.Count; i++)
        {
            BatteryUsageBucket bucket = buckets[i];
            if (!ShouldDrawBar(bucket))
            {
                continue;
            }

            double normalized = Math.Clamp(bucket.BatteryPercent / 100d, 0, 1);
            double barHeight = Math.Max(3, normalized * height);
            double x = left + i * slot + (slot - barWidth) / 2d;
            double y = top + height - barHeight;
            var rect = new Rect(x, y, barWidth, barHeight);
            if (bucket.Kind == BatteryUsageBucketKind.Missing)
            {
                var fill = new SolidColorBrush(MediaColor.FromArgb(52, 196, 204, 214));
                var pen = new MediaPen(new SolidColorBrush(MediaColor.FromRgb(196, 204, 214)), 1)
                {
                    DashStyle = new DashStyle([2, 2], 0)
                };
                context.DrawRoundedRectangle(fill, pen, rect, 2, 2);
            }
            else
            {
                context.DrawRoundedRectangle(GetBarBrush(bucket), null, rect, 2, 2);
            }
        }
    }

    private static bool ShouldDrawBar(BatteryUsageBucket bucket) =>
        bucket.HasData
        || bucket.Kind is BatteryUsageBucketKind.Sleep
            or BatteryUsageBucketKind.Missing
            or BatteryUsageBucketKind.InferredCharge
            or BatteryUsageBucketKind.ChargeHold;

    private static void DrawPowerModeLane(DrawingContext context, IReadOnlyList<BatteryUsageBucket> buckets, double left, double width, double y)
    {
        double slot = width / buckets.Count;
        foreach (WindowsPowerMode mode in Enum.GetValues<WindowsPowerMode>())
        {
            var pen = new MediaPen(GetPowerModeBrush(mode), 3)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round
            };

            foreach ((double start, double end) in Ranges(buckets, b => b.PowerMode == mode))
            {
                double startX = left + start * slot + 1;
                double endX = left + end * slot - 1;
                if (endX <= startX)
                {
                    continue;
                }

                context.DrawLine(pen, new WindowsPoint(startX, y), new WindowsPoint(endX, y));
            }
        }
    }

    private void DrawHoverHighlight(DrawingContext context, IReadOnlyList<BatteryUsageBucket> buckets, ChartLayout layout)
    {
        if (_hoverSelection is not { } selection || selection.Start < 0 || selection.End <= selection.Start || selection.End > buckets.Count)
        {
            return;
        }

        double slot = layout.PlotWidth / buckets.Count;
        double x = layout.Left + selection.Start * slot;
        double width = Math.Max(2, (selection.End - selection.Start) * slot);
        var fill = new SolidColorBrush(MediaColor.FromArgb(34, 255, 255, 255));
        var pen = new MediaPen(new SolidColorBrush(MediaColor.FromArgb(115, 180, 190, 202)), 1);
        if (selection.Kind == HoverKind.PowerMode)
        {
            context.DrawRoundedRectangle(fill, pen, new Rect(x, layout.PowerModeLaneY - 5, width, 10), 4, 4);
            return;
        }

        context.DrawRoundedRectangle(fill, pen, new Rect(x, layout.Top, width, layout.AverageWattsLaneY + 26 - layout.Top), 4, 4);
    }

    private static void DrawLaneSeparators(DrawingContext context, double left, double width, double powerModeLaneY, double averageWattsLaneY)
    {
        var pen = new MediaPen(new SolidColorBrush(MediaColor.FromArgb(88, 75, 82, 90)), 1);
        context.DrawLine(pen, new WindowsPoint(left, powerModeLaneY + 7), new WindowsPoint(left + width, powerModeLaneY + 7));
        context.DrawLine(pen, new WindowsPoint(left, averageWattsLaneY + 26), new WindowsPoint(left + width, averageWattsLaneY + 26));
    }

    private static void DrawAverageWattsLane(DrawingContext context, IReadOnlyList<BatteryUsageBucket> buckets, double left, double width, double y)
    {
        double slot = width / buckets.Count;
        var labelRects = new List<Rect>();
        foreach ((int start, int end) in UsageRanges(buckets))
        {
            double x = left + start * slot;
            double w = Math.Max(2, (end - start) * slot);

            BatteryUsageBucket[] range = buckets.Skip(start).Take(end - start).ToArray();
            double[] watts = range
                .Where(bucket => bucket.HasData || bucket.Kind is BatteryUsageBucketKind.Sleep
                    or BatteryUsageBucketKind.Missing
                    or BatteryUsageBucketKind.InferredCharge
                    or BatteryUsageBucketKind.ChargeHold)
                .Select(bucket => bucket.AverageWatts)
                .ToArray();
            if (watts.Length == 0)
            {
                continue;
            }

            double averageWatts = watts.Average();
            string label = $"{averageWatts:N1}";
            MediaBrush brush = GetUsageRangeBrush(range[^1]);
            var text = FormatText(label, 10, brush, "Segoe UI Semibold");
            double textX = Math.Clamp(x + w / 2d - text.Width / 2d, left, left + width - text.Width);
            double textY = FindAvailableLabelY(new Rect(textX, y, text.Width, text.Height), labelRects, y);
            var labelRect = new Rect(textX, textY, text.Width, text.Height);
            labelRects.Add(labelRect);
            context.DrawText(text, new WindowsPoint(textX, textY));
        }
    }

    private static double FindAvailableLabelY(Rect preferredRect, IReadOnlyList<Rect> placedLabels, double baseY)
    {
        double[] rows = [baseY, baseY + 11];
        foreach (double rowY in rows)
        {
            var candidate = new Rect(preferredRect.X, rowY, preferredRect.Width, preferredRect.Height);
            if (!placedLabels.Any(rect => rect.IntersectsWith(candidate)))
            {
                return candidate.Y;
            }
        }

        return rows[1];
    }

    private static IEnumerable<(int Start, int End)> UsageRanges(IReadOnlyList<BatteryUsageBucket> buckets)
    {
        string? current = null;
        int start = 0;
        for (int i = 0; i <= buckets.Count; i++)
        {
            string? category = i < buckets.Count ? GetUsageRangeCategory(buckets[i]) : null;
            if (category is not null && current is null)
            {
                current = category;
                start = i;
            }
            else if (category != current)
            {
                if (current is not null)
                {
                    yield return (start, i);
                }

                current = category;
                start = i;
            }
        }
    }

    private static string? GetUsageRangeCategory(BatteryUsageBucket bucket)
    {
        if (bucket.Kind == BatteryUsageBucketKind.NoData)
        {
            return null;
        }

        if (bucket.Kind == BatteryUsageBucketKind.Sleep)
        {
            return "sleep";
        }

        if (bucket.Kind == BatteryUsageBucketKind.Missing)
        {
            return "missing";
        }

        if (bucket.IsCharging || bucket.Kind == BatteryUsageBucketKind.InferredCharge)
        {
            return "charge";
        }

        if (bucket.Kind == BatteryUsageBucketKind.ChargeHold)
        {
            return "hold";
        }

        if (bucket.HasData && !bucket.IsPluggedIn)
        {
            return "discharge";
        }

        return null;
    }

    private static MediaBrush GetUsageRangeBrush(BatteryUsageBucket bucket)
    {
        string? category = GetUsageRangeCategory(bucket);
        MediaColor color = category switch
        {
            "charge" => MediaColor.FromRgb(154, 215, 108),
            "hold" => MediaColor.FromRgb(84, 214, 198),
            "sleep" => MediaColor.FromRgb(88, 166, 255),
            "missing" => MediaColor.FromRgb(196, 204, 214),
            _ => MediaColor.FromRgb(174, 180, 188)
        };

        return new SolidColorBrush(color);
    }

    private void OnMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        BatteryUsageBucket[] buckets = Buckets?.ToArray() ?? [];
        if (buckets.Length == 0)
        {
            ClearHover();
            return;
        }

        HoverSelection? selection = HitTestHover(e.GetPosition(this), buckets, CreateLayout(ActualWidth, ActualHeight));
        if (selection is null)
        {
            ClearHover();
            return;
        }

        SetHover(selection.Value, BuildHoverText(buckets, selection.Value));
    }

    private void OnMouseLeave(object sender, System.Windows.Input.MouseEventArgs e) => ClearHover();

    private void SetHover(HoverSelection selection, string text)
    {
        bool changed = !_hoverSelection.Equals(selection);
        _hoverSelection = selection;
        if (ToolTip is System.Windows.Controls.ToolTip toolTip)
        {
            toolTip.Content = text;
            toolTip.IsOpen = true;
        }

        if (changed)
        {
            InvalidateVisual();
        }
    }

    private void ClearHover()
    {
        if (ToolTip is System.Windows.Controls.ToolTip toolTip)
        {
            toolTip.IsOpen = false;
        }

        if (_hoverSelection is null)
        {
            return;
        }

        _hoverSelection = null;
        InvalidateVisual();
    }

    private static HoverSelection? HitTestHover(WindowsPoint point, IReadOnlyList<BatteryUsageBucket> buckets, ChartLayout layout)
    {
        if (point.X < layout.Left || point.X > layout.Left + layout.PlotWidth)
        {
            return null;
        }

        int index = Math.Clamp((int)((point.X - layout.Left) / (layout.PlotWidth / buckets.Count)), 0, buckets.Count - 1);
        if (point.Y >= layout.PowerModeLaneY - 7 && point.Y <= layout.PowerModeLaneY + 7 && buckets[index].PowerMode.HasValue)
        {
            return BuildRangeSelection(buckets, index, HoverKind.PowerMode, bucket => bucket.PowerMode == buckets[index].PowerMode);
        }

        if (point.Y >= layout.Top && point.Y <= layout.AverageWattsLaneY + 34)
        {
            string? category = GetUsageRangeCategory(buckets[index]);
            if (category is not null)
            {
                return BuildRangeSelection(buckets, index, HoverKind.Usage, bucket => GetUsageRangeCategory(bucket) == category);
            }
        }

        return null;
    }

    private static HoverSelection BuildRangeSelection(IReadOnlyList<BatteryUsageBucket> buckets, int index, HoverKind kind, Func<BatteryUsageBucket, bool> predicate)
    {
        int start = index;
        while (start > 0 && predicate(buckets[start - 1]))
        {
            start--;
        }

        int end = index + 1;
        while (end < buckets.Count && predicate(buckets[end]))
        {
            end++;
        }

        return new HoverSelection(start, end, kind);
    }

    private static string BuildHoverText(IReadOnlyList<BatteryUsageBucket> buckets, HoverSelection selection)
    {
        BatteryUsageBucket[] range = buckets.Skip(selection.Start).Take(selection.End - selection.Start).ToArray();
        BatteryUsageBucket first = range[0];
        BatteryUsageBucket last = range[^1];
        string title = selection.Kind == HoverKind.PowerMode
            ? "Power Plan"
            : FormatUsageCategory(first);
        string time = $"{first.Start:HH:mm} - {last.End:HH:mm}";
        string duration = FormatDuration(last.End - first.Start);
        string battery = $"{first.BatteryPercent:N0}% -> {last.BatteryPercent:N0}%";
        double averageWatts = range.Select(bucket => bucket.AverageWatts).DefaultIfEmpty(0).Average();
        string powerMode = MostCommonPowerMode(range) is { } mode ? FormatPowerMode(mode) : "Unavailable";

        return selection.Kind == HoverKind.PowerMode
            ? $"{title}\n{FormatPowerMode(first.PowerMode)}\n{time} ({duration})\nAvg W: {averageWatts:N1}"
            : $"{title}\n{time} ({duration})\nBattery: {battery}\nAvg W: {averageWatts:N1}\nPower plan: {powerMode}";
    }

    private static string FormatUsageCategory(BatteryUsageBucket bucket)
    {
        if (bucket.IsCritical)
        {
            return "Critical";
        }

        if (bucket.IsPowerSave && !bucket.IsPluggedIn)
        {
            return "Power Save";
        }

        return GetUsageRangeCategory(bucket) switch
        {
            "charge" => "Charge",
            "hold" => "Charge Hold",
            "sleep" => "Sleep",
            "missing" => "Missing Data",
            "discharge" => "Battery Discharge",
            _ => "Battery Usage"
        };
    }

    private static WindowsPowerMode? MostCommonPowerMode(IEnumerable<BatteryUsageBucket> buckets) =>
        buckets
            .Where(bucket => bucket.PowerMode.HasValue)
            .GroupBy(bucket => bucket.PowerMode!.Value)
            .OrderByDescending(group => group.Count())
            .Select(group => (WindowsPowerMode?)group.Key)
            .FirstOrDefault();

    private static string FormatPowerMode(WindowsPowerMode? mode) => mode switch
    {
        WindowsPowerMode.BestPowerEfficiency => "Power efficiency",
        WindowsPowerMode.Balanced => "Balanced",
        WindowsPowerMode.BestPerformance => "Performance",
        _ => "Unavailable"
    };

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalHours >= 1)
        {
            return $"{(int)duration.TotalHours}h {duration.Minutes}m";
        }

        return $"{Math.Max(1, (int)Math.Round(duration.TotalMinutes))}m";
    }

    private static void DrawCurrentMarker(DrawingContext context, IReadOnlyList<BatteryUsageBucket> buckets, double left, double width, double top, double height)
    {
        int index = -1;
        for (int i = 0; i < buckets.Count; i++)
        {
            if (buckets[i].IsCurrent)
            {
                index = i;
                break;
            }
        }

        if (index < 0)
        {
            return;
        }

        double slot = width / buckets.Count;
        double x = left + index * slot + slot / 2d;
        var markerPen = new MediaPen(new SolidColorBrush(MediaColor.FromArgb(105, 116, 182, 255)), 1);
        context.DrawLine(markerPen, new WindowsPoint(x, top), new WindowsPoint(x, top + height));
    }

    private static MediaBrush GetBarBrush(BatteryUsageBucket bucket)
    {
        if (bucket.Kind == BatteryUsageBucketKind.Sleep)
        {
            return new SolidColorBrush(MediaColor.FromRgb(88, 166, 255));
        }

        if (bucket.Kind == BatteryUsageBucketKind.InferredCharge)
        {
            return new SolidColorBrush(MediaColor.FromRgb(154, 215, 108));
        }

        if (bucket.Kind == BatteryUsageBucketKind.ChargeHold)
        {
            return new SolidColorBrush(MediaColor.FromRgb(84, 214, 198));
        }

        if (bucket.IsCharging)
        {
            return new SolidColorBrush(MediaColor.FromRgb(154, 215, 108));
        }

        if (bucket.IsCritical)
        {
            return new SolidColorBrush(MediaColor.FromRgb(214, 92, 83));
        }

        if (bucket.IsPowerSave)
        {
            return new SolidColorBrush(MediaColor.FromRgb(237, 184, 72));
        }

        return new SolidColorBrush(MediaColor.FromRgb(142, 148, 154));
    }

    private static MediaBrush GetPowerModeBrush(WindowsPowerMode mode) => mode switch
    {
        WindowsPowerMode.BestPowerEfficiency => new SolidColorBrush(MediaColor.FromRgb(88, 166, 255)),
        WindowsPowerMode.Balanced => new SolidColorBrush(MediaColor.FromRgb(154, 161, 170)),
        WindowsPowerMode.BestPerformance => new SolidColorBrush(MediaColor.FromRgb(225, 76, 70)),
        _ => new SolidColorBrush(MediaColor.FromRgb(142, 148, 154))
    };

    private static void DrawAxisLabels(DrawingContext context, double x, double top, double height)
    {
        var majorBrush = new SolidColorBrush(MediaColor.FromRgb(188, 193, 200));
        foreach (double percent in new[] { 100d, 50d, 0d })
        {
            DrawText(context, $"{percent:N0}%", 12, majorBrush, x, PercentToY(percent, top, height) - 9);
        }

        var thresholdBrush = new SolidColorBrush(MediaColor.FromRgb(142, 149, 158));
        foreach (double percent in new[] { 75d, 25d })
        {
            DrawText(context, $"{percent:N0}%", 10, thresholdBrush, x, PercentToY(percent, top, height) - 7);
        }
    }

    private static double PercentToY(double percent, double top, double height) =>
        top + (1d - Math.Clamp(percent, 0, 100) / 100d) * height;

    private static void DrawTimeLabels(DrawingContext context, double left, double width, double y)
    {
        var brush = new SolidColorBrush(MediaColor.FromRgb(174, 180, 188));
        for (int hour = 0; hour <= 24; hour += 2)
        {
            string label = hour.ToString("00");
            double x = left + width * hour / 24d;
            var formatted = FormatText(label, 11, brush);
            if (hour == 24)
            {
                x -= formatted.Width;
            }
            else if (hour > 0)
            {
                x -= formatted.Width / 2d;
            }

            context.DrawText(formatted, new WindowsPoint(x, y));
        }
    }

    private static void DrawCollecting(DrawingContext context, Rect bounds)
    {
        var pen = new MediaPen(new SolidColorBrush(MediaColor.FromArgb(80, 92, 98, 106)), 1);
        context.DrawLine(pen, new WindowsPoint(12, bounds.Height / 2d), new WindowsPoint(Math.Max(12, bounds.Width - 12), bounds.Height / 2d));
        var brush = new SolidColorBrush(MediaColor.FromRgb(175, 181, 190));
        var text = FormatText("Collecting battery usage data...", 12, brush);
        context.DrawText(text, new WindowsPoint(Math.Max(8, bounds.Width / 2d - text.Width / 2d), bounds.Height / 2d - text.Height - 8));
    }

    private static void DrawText(DrawingContext context, string text, double size, MediaBrush brush, double x, double y, string typeface = "Segoe UI")
    {
        context.DrawText(FormatText(text, size, brush, typeface), new WindowsPoint(x, y));
    }

    private static FormattedText FormatText(string text, double size, MediaBrush brush, string typeface = "Segoe UI") =>
        new(
            text,
            System.Globalization.CultureInfo.CurrentCulture,
            System.Windows.FlowDirection.LeftToRight,
            new Typeface(typeface),
            size,
            brush,
            1.0);

    private readonly record struct ChartLayout(
        double Left,
        double Top,
        double PlotWidth,
        double PlotHeight,
        double PowerModeLaneY,
        double AverageWattsLaneY,
        double TimeLabelsY);

    private readonly record struct HoverSelection(int Start, int End, HoverKind Kind);

    private enum HoverKind
    {
        Usage,
        PowerMode
    }
}
