using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using XPSBatteryTray.Models;

namespace XPSBatteryTray.Services;

public sealed class BatteryUsageService
{
    private const int RetentionDays = 7;
    private const int BucketCount = 96;
    private static readonly TimeSpan BucketSize = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan MissingDataThreshold = TimeSpan.FromMinutes(30);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private readonly string _historyPath = Path.Combine(LogService.AppDataRoot, "battery-usage-history.json");
    private BatteryUsageState _state = new();
    private bool _loaded;
    private DateTimeOffset _lastSave = DateTimeOffset.MinValue;
    private DateTimeOffset? _lastRecordTime;

    public BatteryUsageSnapshot Record(BatteryStatus battery, DateTime selectedDate)
    {
        EnsureLoaded();
        DateTimeOffset now = DateTimeOffset.Now;

        if (_state.SessionStart == default)
        {
            _state.SessionStart = now;
        }

        AddActivityTime(now);
        AddOrUpdateSample(now, battery);
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

    private void AddOrUpdateSample(DateTimeOffset now, BatteryStatus battery)
    {
        var sample = new BatteryUsageSample
        {
            Timestamp = now,
            BatteryPercent = Math.Clamp(battery.Percentage, 0, 100),
            IsPluggedIn = battery.IsPluggedIn,
            BatteryWatts = battery.ChargeRateWatts,
            IsPowerSave = battery.IsPowerSave,
            IsCritical = battery.IsCritical
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
            Title = "Battery Usage Since Charge (24h)",
            Date = selectedDate,
            SessionText = FormatDuration(age),
            ActiveText = FormatDuration(TimeSpan.FromSeconds(_state.ActiveSeconds)),
            IdleText = FormatDuration(TimeSpan.FromSeconds(_state.IdleSeconds)),
            EstimatedDrainText = FormatMilliWattHours(EstimateDrainMilliWattHours()),
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
            if (!hasData && !isFuture)
            {
                representative = FindNearestSample(samples, bucketStart + BucketSize / 2);
                hasData = representative is not null && Distance(representative.Timestamp, bucketStart + BucketSize / 2) <= MissingDataThreshold;
            }

            int percent = hasData ? representative!.BatteryPercent : 0;
            double averageWatts = bucketSamples
                .Where(s => s.BatteryWatts.HasValue)
                .Select(s => s.BatteryWatts!.Value)
                .DefaultIfEmpty(representative?.BatteryWatts ?? 0)
                .Average();
            bool charging = hasData && (bucketSamples.Length > 0
                ? bucketSamples.Count(s => s.IsCharging) >= Math.Max(1, bucketSamples.Length / 2)
                : representative!.IsCharging);
            bool powerSave = hasData && (bucketSamples.Length > 0
                ? bucketSamples.Any(s => s.IsPowerSave)
                : representative!.IsPowerSave);
            bool critical = hasData && (bucketSamples.Length > 0
                ? bucketSamples.Any(s => s.IsCritical || s.BatteryPercent <= 10)
                : representative!.IsCritical || representative.BatteryPercent <= 10);

            buckets.Add(new BatteryUsageBucket
            {
                Start = bucketStart,
                End = bucketEnd,
                Label = BuildBucketLabel(i, bucketStart),
                BatteryPercent = percent,
                AverageWatts = averageWatts,
                IsCharging = charging,
                IsPowerSave = powerSave,
                IsCritical = critical,
                IsMissingData = !hasData && !isFuture,
                IsCurrent = now >= bucketStart && now < bucketEnd,
                HasData = hasData
            });
        }

        return buckets;
    }

    private static BatteryUsageSample? FindNearestSample(IReadOnlyList<BatteryUsageSample> samples, DateTimeOffset target)
    {
        BatteryUsageSample? nearest = null;
        TimeSpan nearestDistance = TimeSpan.MaxValue;
        foreach (BatteryUsageSample sample in samples)
        {
            TimeSpan distance = Distance(sample.Timestamp, target);
            if (distance < nearestDistance)
            {
                nearest = sample;
                nearestDistance = distance;
            }
        }

        return nearest;
    }

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

    private static TimeSpan Distance(DateTimeOffset left, DateTimeOffset right) =>
        TimeSpan.FromTicks(Math.Abs((left - right).Ticks));

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalHours >= 1)
        {
            return $"{(int)duration.TotalHours}h {duration.Minutes}m";
        }

        return $"{Math.Max(0, (int)Math.Round(duration.TotalMinutes))}m";
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

        public bool IsCharging => IsPluggedIn || BatteryWatts is > 0.5;
    }
}
