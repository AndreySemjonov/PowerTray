using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using XPSBatteryTray.Models;

namespace XPSBatteryTray.Services;

public sealed class BatteryUsageService
{
    private const int RetentionDays = 7;
    private const int BucketCount = 96;
    private const double ChargeInferenceMinimumPercent = 1;
    private const double SleepDrainMaximumPercent = 3;
    private const double SleepDrainMaximumPercentPerHour = 2;
    private static readonly TimeSpan BucketSize = TimeSpan.FromMinutes(15);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private readonly string _historyPath = Path.Combine(LogService.AppDataRoot, "battery-usage-history.json");
    private BatteryUsageState _state = new();
    private bool _loaded;
    private DateTimeOffset _lastSave = DateTimeOffset.MinValue;
    private DateTimeOffset? _lastRecordTime;

    public BatteryUsageSnapshot Record(BatteryStatus battery, DateTime selectedDate, WindowsPowerMode? powerMode)
    {
        EnsureLoaded();
        DateTimeOffset now = DateTimeOffset.Now;

        if (_state.SessionStart == default)
        {
            _state.SessionStart = now;
        }

        AddActivityTime(now);
        AddOrUpdateSample(now, battery, powerMode);
        SaveIfNeeded(now, force: false);
        return BuildSnapshot(selectedDate.Date, now);
    }

    public BatteryUsageSnapshot GetSnapshot(DateTime selectedDate)
    {
        EnsureLoaded();
        return BuildSnapshot(selectedDate.Date, DateTimeOffset.Now);
    }

    private void EnsureLoaded()
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        try
        {
            if (File.Exists(_historyPath))
            {
                _state = JsonSerializer.Deserialize<BatteryUsageState>(File.ReadAllText(_historyPath), JsonOptions) ?? new BatteryUsageState();
                DateTimeOffset cutoff = DateTimeOffset.Now.AddDays(-RetentionDays);
                _state.Samples = _state.Samples.Where(s => s.Timestamp >= cutoff).ToList();
            }
        }
        catch (Exception ex)
        {
            LogService.Error(ex, "Failed to load battery usage history; starting fresh.");
            _state = new BatteryUsageState();
        }
    }

    private void AddActivityTime(DateTimeOffset now)
    {
        if (_lastRecordTime is not { } lastRecordTime)
        {
            _lastRecordTime = now;
            return;
        }

        double elapsedSeconds = Math.Clamp((now - lastRecordTime).TotalSeconds, 0, 300);
        if (elapsedSeconds <= 0)
        {
            return;
        }

        if (GetIdleTime() < TimeSpan.FromMinutes(2))
        {
            _state.ActiveSeconds += elapsedSeconds;
        }
        else
        {
            _state.IdleSeconds += elapsedSeconds;
        }

        _lastRecordTime = now;
    }

    private void AddOrUpdateSample(DateTimeOffset now, BatteryStatus battery, WindowsPowerMode? powerMode)
    {
        var sample = new BatteryUsageSample
        {
            Timestamp = now,
            BatteryPercent = Math.Clamp(battery.Percentage, 0, 100),
            IsPluggedIn = battery.IsPluggedIn,
            BatteryWatts = battery.ChargeRateWatts,
            IsPowerSave = battery.IsPowerSave,
            IsCritical = battery.IsCritical,
            PowerMode = powerMode
        };

        BatteryUsageSample? last = _state.Samples.LastOrDefault();
        if (last is not null && now - last.Timestamp < TimeSpan.FromSeconds(10))
        {
            sample.Timestamp = last.Timestamp;
            _state.Samples[^1] = sample;
        }
        else
        {
            _state.Samples.Add(sample);
        }

        DateTimeOffset cutoff = now.AddDays(-RetentionDays);
        _state.Samples.RemoveAll(s => s.Timestamp < cutoff);
    }

    private BatteryUsageSnapshot BuildSnapshot(DateTime selectedDate, DateTimeOffset now)
    {
        IReadOnlyList<BatteryUsageBucket> buckets = BuildBuckets(selectedDate, now);
        TimeSpan age = _state.SessionStart == default ? TimeSpan.Zero : now - _state.SessionStart;
        return new BatteryUsageSnapshot
        {
            Title = "Battery usage (24h)",
            Date = selectedDate,
            SessionText = FormatDuration(age),
            ActiveText = FormatDuration(TimeSpan.FromSeconds(_state.ActiveSeconds)),
            IdleText = FormatDuration(TimeSpan.FromSeconds(_state.IdleSeconds)),
            EstimatedDrainText = FormatMilliWattHours(EstimateDrainMilliWattHours()),
            SleepDrainText = BuildSleepDrainText(buckets),
            ChargeBehaviorText = BuildChargeBehaviorText(buckets),
            Buckets = buckets
        };
    }

    private IReadOnlyList<BatteryUsageBucket> BuildBuckets(DateTime selectedDate, DateTimeOffset now)
    {
        DateTime day = DateTime.SpecifyKind(selectedDate.Date, DateTimeKind.Unspecified);
        DateTimeOffset dayStart = new(day, TimeZoneInfo.Local.GetUtcOffset(day));
        DateTimeOffset dayEnd = dayStart.AddDays(1);

        BatteryUsageSample[] samples = _state.Samples
            .Where(s => s.Timestamp >= dayStart && s.Timestamp < dayEnd)
            .OrderBy(s => s.Timestamp)
            .ToArray();
        BatteryUsageSample[] allSamples = _state.Samples
            .OrderBy(s => s.Timestamp)
            .ToArray();

        var buckets = new List<BatteryUsageBucket>(BucketCount);
        for (int i = 0; i < BucketCount; i++)
        {
            DateTimeOffset bucketStart = dayStart + TimeSpan.FromTicks(BucketSize.Ticks * i);
            DateTimeOffset bucketEnd = bucketStart + BucketSize;
            BatteryUsageSample[] bucketSamples = samples
                .Where(s => s.Timestamp >= bucketStart && s.Timestamp < bucketEnd)
                .ToArray();

            BatteryUsageSample? representative = bucketSamples.LastOrDefault();
            bool hasData = representative is not null;
            bool isFuture = bucketStart > now;
            BatteryUsageBucketKind kind = BatteryUsageBucketKind.Observed;
            int percent = hasData ? representative!.BatteryPercent : 0;
            if (!hasData && !isFuture)
            {
                (kind, percent) = InferGapBucket(allSamples, bucketStart, bucketEnd);
            }

            bool pluggedIn = hasData && (bucketSamples.Length > 0
                ? bucketSamples.Count(s => s.IsPluggedIn) >= Math.Max(1, bucketSamples.Length / 2)
                : representative!.IsPluggedIn);
            bool positiveWatts = hasData && (bucketSamples.Length > 0
                ? bucketSamples.Any(s => s.BatteryWatts is > 0.5)
                : representative!.BatteryWatts is > 0.5);
            bool percentRising = bucketSamples.Length >= 2
                ? bucketSamples[^1].BatteryPercent > bucketSamples[0].BatteryPercent
                : representative is not null && FindPreviousSample(allSamples, bucketStart) is { } previousSample
                    && representative.BatteryPercent > previousSample.BatteryPercent;
            double averageWatts = bucketSamples
                .Where(s => s.BatteryWatts.HasValue)
                .Select(s => s.BatteryWatts!.Value)
                .DefaultIfEmpty(representative?.BatteryWatts ?? 0)
                .Average();
            bool charging = kind == BatteryUsageBucketKind.InferredCharge || (pluggedIn && (positiveWatts || percentRising));
            if (hasData && pluggedIn && !charging)
            {
                kind = BatteryUsageBucketKind.ChargeHold;
            }

            bool powerSave = hasData && (bucketSamples.Length > 0
                ? bucketSamples.Any(s => s.IsPowerSave)
                : representative!.IsPowerSave);
            bool critical = hasData && (bucketSamples.Length > 0
                ? bucketSamples.Any(s => s.IsCritical || s.BatteryPercent <= 10)
                : representative!.IsCritical || representative.BatteryPercent <= 10);
            WindowsPowerMode? powerMode = hasData
                ? MostCommonPowerMode(bucketSamples) ?? representative!.PowerMode
                : null;

            buckets.Add(new BatteryUsageBucket
            {
                Start = bucketStart,
                End = bucketEnd,
                Label = BuildBucketLabel(i, bucketStart),
                BatteryPercent = percent,
                AverageWatts = averageWatts,
                IsPluggedIn = pluggedIn || kind is BatteryUsageBucketKind.InferredCharge or BatteryUsageBucketKind.ChargeHold,
                IsCharging = charging,
                IsPowerSave = powerSave,
                IsCritical = critical,
                IsMissingData = kind == BatteryUsageBucketKind.NoData,
                IsCurrent = now >= bucketStart && now < bucketEnd,
                HasData = hasData,
                Kind = kind,
                PowerMode = powerMode
            });
        }

        return buckets;
    }

    private static (BatteryUsageBucketKind Kind, int Percent) InferGapBucket(IReadOnlyList<BatteryUsageSample> samples, DateTimeOffset bucketStart, DateTimeOffset bucketEnd)
    {
        BatteryUsageSample? before = null;
        BatteryUsageSample? after = null;
        foreach (BatteryUsageSample sample in samples)
        {
            if (sample.Timestamp < bucketStart)
            {
                before = sample;
                continue;
            }

            if (sample.Timestamp >= bucketEnd)
            {
                after = sample;
                break;
            }
        }

        int fallbackPercent = before?.BatteryPercent ?? after?.BatteryPercent ?? 0;
        if (before is null || after is null || after.Timestamp <= before.Timestamp)
        {
            return (BatteryUsageBucketKind.NoData, fallbackPercent);
        }

        int percent = InterpolateBatteryPercent(before, after, bucketStart + BucketSize / 2);
        double delta = after.BatteryPercent - before.BatteryPercent;
        if (delta >= ChargeInferenceMinimumPercent)
        {
            return (BatteryUsageBucketKind.InferredCharge, percent);
        }

        double drain = Math.Max(0, -delta);
        if (before.IsPluggedIn && after.IsPluggedIn && drain <= SleepDrainMaximumPercent)
        {
            return (BatteryUsageBucketKind.ChargeHold, percent);
        }

        double hours = Math.Max((after.Timestamp - before.Timestamp).TotalHours, BucketSize.TotalHours);
        double sleepDrainLimit = Math.Max(SleepDrainMaximumPercent, hours * SleepDrainMaximumPercentPerHour);
        return drain <= sleepDrainLimit
            ? (BatteryUsageBucketKind.Sleep, percent)
            : (BatteryUsageBucketKind.Missing, percent);
    }

    private static int InterpolateBatteryPercent(BatteryUsageSample before, BatteryUsageSample after, DateTimeOffset timestamp)
    {
        double totalTicks = (after.Timestamp - before.Timestamp).Ticks;
        if (totalTicks <= 0)
        {
            return Math.Clamp(before.BatteryPercent, 0, 100);
        }

        double progress = Math.Clamp((timestamp - before.Timestamp).Ticks / totalTicks, 0, 1);
        double percent = before.BatteryPercent + (after.BatteryPercent - before.BatteryPercent) * progress;
        return Math.Clamp((int)Math.Round(percent), 0, 100);
    }

    private static BatteryUsageSample? FindPreviousSample(IReadOnlyList<BatteryUsageSample> samples, DateTimeOffset timestamp)
    {
        BatteryUsageSample? previous = null;
        foreach (BatteryUsageSample sample in samples)
        {
            if (sample.Timestamp >= timestamp)
            {
                break;
            }

            previous = sample;
        }

        return previous;
    }

    private static WindowsPowerMode? MostCommonPowerMode(IReadOnlyList<BatteryUsageSample> samples) =>
        samples
            .Where(sample => sample.PowerMode.HasValue)
            .GroupBy(sample => sample.PowerMode!.Value)
            .OrderByDescending(group => group.Count())
            .Select(group => (WindowsPowerMode?)group.Key)
            .FirstOrDefault();

    private void SaveIfNeeded(DateTimeOffset now, bool force)
    {
        if (!force && now - _lastSave < TimeSpan.FromSeconds(30))
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(LogService.AppDataRoot);
            File.WriteAllText(_historyPath, JsonSerializer.Serialize(_state, JsonOptions));
            _lastSave = now;
        }
        catch (Exception ex)
        {
            LogService.Error(ex, "Failed to save battery usage history.");
        }
    }

    private static string BuildBucketLabel(int bucketIndex, DateTimeOffset bucketStart)
    {
        if (bucketIndex == BucketCount - 1)
        {
            return "24";
        }

        return bucketStart.Hour % 2 == 0 && bucketStart.Minute == 0 ? bucketStart.ToString("HH") : string.Empty;
    }

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalHours >= 1)
        {
            return $"{(int)duration.TotalHours}h {duration.Minutes}m";
        }

        return $"{Math.Max(0, (int)Math.Round(duration.TotalMinutes))}m";
    }

    private static string BuildSleepDrainText(IReadOnlyList<BatteryUsageBucket> buckets)
    {
        TimeSpan duration = TimeSpan.Zero;
        double drain = 0;
        foreach ((int start, int end) in BucketRanges(buckets, b => b.Kind == BatteryUsageBucketKind.Sleep))
        {
            BatteryUsageBucket first = buckets[start];
            BatteryUsageBucket last = buckets[end - 1];
            duration += SumDuration(buckets, start, end);
            drain += Math.Max(0, first.BatteryPercent - last.BatteryPercent);
        }

        if (duration < TimeSpan.FromMinutes(1))
        {
            return "Sleep: none";
        }

        double rate = duration.TotalHours > 0 ? drain / duration.TotalHours : 0;
        return $"Sleep: {FormatDuration(duration)} · -{drain:N0}% · {rate:N1}%/h";
    }

    private static string BuildChargeBehaviorText(IReadOnlyList<BatteryUsageBucket> buckets)
    {
        TimeSpan onBattery = SumDuration(buckets, b => b.HasData && !b.IsPluggedIn);
        TimeSpan charging = SumDuration(buckets, b => b.IsCharging);
        TimeSpan hold = SumDuration(buckets, b => b.Kind == BatteryUsageBucketKind.ChargeHold);
        TimeSpan sleep = SumDuration(buckets, b => b.Kind == BatteryUsageBucketKind.Sleep);
        TimeSpan missing = SumDuration(buckets, b => b.Kind is BatteryUsageBucketKind.Missing or BatteryUsageBucketKind.NoData);

        var parts = new List<string>();
        AddDurationPart(parts, "Battery", onBattery);
        AddDurationPart(parts, "Charge", charging);
        AddDurationPart(parts, "Hold", hold);
        AddDurationPart(parts, "Sleep", sleep);
        AddDurationPart(parts, "Missing", missing);
        return parts.Count > 0 ? string.Join(" · ", parts) : "Usage: collecting";
    }

    private static void AddDurationPart(List<string> parts, string label, TimeSpan duration)
    {
        if (duration >= TimeSpan.FromMinutes(1))
        {
            parts.Add($"{label} {FormatDuration(duration)}");
        }
    }

    private static TimeSpan SumDuration(IReadOnlyList<BatteryUsageBucket> buckets, Func<BatteryUsageBucket, bool> predicate)
    {
        long ticks = buckets
            .Where(predicate)
            .Sum(bucket => Math.Max(0, (bucket.End - bucket.Start).Ticks));
        return TimeSpan.FromTicks(ticks);
    }

    private static TimeSpan SumDuration(IReadOnlyList<BatteryUsageBucket> buckets, int start, int end)
    {
        long ticks = 0;
        for (int i = start; i < end; i++)
        {
            ticks += Math.Max(0, (buckets[i].End - buckets[i].Start).Ticks);
        }

        return TimeSpan.FromTicks(ticks);
    }

    private static IEnumerable<(int Start, int End)> BucketRanges(IReadOnlyList<BatteryUsageBucket> buckets, Func<BatteryUsageBucket, bool> predicate)
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

    private double EstimateDrainMilliWattHours()
    {
        DateTimeOffset cutoff = DateTimeOffset.Now.AddDays(-1);
        BatteryUsageSample[] samples = _state.Samples
            .Where(s => s.Timestamp >= cutoff)
            .OrderBy(s => s.Timestamp)
            .ToArray();
        if (samples.Length < 2)
        {
            return 0;
        }

        double total = 0;
        for (int i = 1; i < samples.Length; i++)
        {
            if (samples[i - 1].BatteryWatts is not { } previousWatts || samples[i].BatteryWatts is not { } currentWatts)
            {
                continue;
            }

            double averageWatts = (previousWatts + currentWatts) / 2d;
            if (averageWatts >= -0.05)
            {
                continue;
            }

            double hours = Math.Clamp((samples[i].Timestamp - samples[i - 1].Timestamp).TotalHours, 0, 5d / 60d);
            total += Math.Abs(averageWatts) * hours * 1000d;
        }

        return total;
    }

    private static string FormatMilliWattHours(double value)
    {
        if (value >= 1000)
        {
            return $"{value / 1000d:N1} Wh";
        }

        return $"{value:N1} mWh";
    }

    private static TimeSpan GetIdleTime()
    {
        var info = new LastInputInfo { CbSize = (uint)Marshal.SizeOf<LastInputInfo>() };
        if (!GetLastInputInfo(ref info))
        {
            return TimeSpan.Zero;
        }

        uint tickCount = GetTickCount();
        uint idleMilliseconds = tickCount - info.DwTime;
        return TimeSpan.FromMilliseconds(idleMilliseconds);
    }

    [DllImport("user32.dll")]
    private static extern bool GetLastInputInfo(ref LastInputInfo plii);

    [DllImport("kernel32.dll")]
    private static extern uint GetTickCount();

    [StructLayout(LayoutKind.Sequential)]
    private struct LastInputInfo
    {
        public uint CbSize;
        public uint DwTime;
    }

    private sealed class BatteryUsageState
    {
        public DateTimeOffset SessionStart { get; set; }
        public double ActiveSeconds { get; set; }
        public double IdleSeconds { get; set; }
        public List<BatteryUsageSample> Samples { get; set; } = [];
    }

    private sealed class BatteryUsageSample
    {
        public DateTimeOffset Timestamp { get; set; }
        public int BatteryPercent { get; set; }
        public bool IsPluggedIn { get; set; }
        public double? BatteryWatts { get; set; }
        public bool IsPowerSave { get; set; }
        public bool IsCritical { get; set; }
        public WindowsPowerMode? PowerMode { get; set; }

        public bool IsCharging => BatteryWatts is > 0.5;
    }
}
