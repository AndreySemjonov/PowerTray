using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Xml.Linq;
using XPSBatteryTray.Models;

namespace XPSBatteryTray.Services;

public sealed class WindowsBatteryUsageService
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan RangeMatchPadding = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan PowerCfgTimeout = TimeSpan.FromSeconds(30);
    private static readonly Dictionary<string, string> KnownNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Code"] = "Visual Studio Code",
        ["Codex"] = "Codex",
        ["devenv"] = "Visual Studio",
        ["dotnet"] = ".NET Host",
        ["dwm"] = "Desktop Window Manager",
        ["explorer"] = "Windows Explorer",
        ["EXCEL"] = "Microsoft Excel",
        ["HWiNFO64"] = "HWiNFO",
        ["msedge"] = "Microsoft Edge",
        ["msedgewebview2"] = "Microsoft Edge WebView2",
        ["MsMpEng"] = "Microsoft Defender",
        ["MSTeams"] = "Microsoft Teams",
        ["OneDrive"] = "Microsoft OneDrive",
        ["OpenAI.Codex"] = "Codex",
        ["powershell"] = "PowerShell",
        ["System"] = "System",
        ["Taskmgr"] = "Task Manager",
        ["windows.immersivecontrolpanel"] = "Settings",
        ["Microsoft.ScreenSketch"] = "Snipping Tool"
    };
    private static readonly HashSet<string> HiddenProcessNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "conhost",
        "DSAService",
        "dwm",
        "LogonUI",
        "lsass",
        "MoUsoCoreWorker",
        "MsMpEng",
        "services",
        "svchost",
        "winlogon",
        "WUDFHost"
    };

    private readonly object _sync = new();
    private IReadOnlyList<SrumUsageRecord> _cachedRecords = [];
    private DateTimeOffset _lastAttempt = DateTimeOffset.MinValue;
    private string _cachedStatusText = "Windows battery usage not loaded yet.";

    public WindowsBatteryUsageSnapshot GetSnapshot()
    {
        lock (_sync)
        {
            DateTimeOffset now = DateTimeOffset.Now;
            EnsureRecords(now);
            return BuildSnapshot(now, rangeStart: null, rangeEnd: null);
        }
    }

    public WindowsBatteryUsageSnapshot GetSnapshot(DateTimeOffset rangeStart, DateTimeOffset rangeEnd)
    {
        lock (_sync)
        {
            DateTimeOffset now = DateTimeOffset.Now;
            EnsureRecords(now);
            return BuildSnapshot(now, rangeStart, rangeEnd);
        }
    }

    private void EnsureRecords(DateTimeOffset now, bool force = false)
    {
        if (!force && now - _lastAttempt < CacheDuration)
        {
            return;
        }

        _lastAttempt = now;
        SrumRecordLoadResult result = LoadRecords();
        _cachedRecords = result.Records;
        _cachedStatusText = result.StatusText;
    }

    private WindowsBatteryUsageSnapshot BuildSnapshot(DateTimeOffset now, DateTimeOffset? rangeStart, DateTimeOffset? rangeEnd)
    {
        IEnumerable<SrumUsageRecord> records = _cachedRecords;
        bool isRange = rangeStart.HasValue && rangeEnd.HasValue;
        if (isRange)
        {
            DateTimeOffset paddedStart = rangeStart!.Value - RangeMatchPadding;
            DateTimeOffset paddedEnd = rangeEnd!.Value + RangeMatchPadding;
            records = records
                .Select(record => record with { RangeScale = CalculateRangeScale(record, paddedStart, paddedEnd) })
                .Where(record => record.RangeScale > 0);
        }
        else
        {
            DateTimeOffset cutoff = now.AddHours(-24);
            records = records.Where(record => record.Timestamp is null || record.Timestamp >= cutoff);
        }

        SrumUsageRecord[] matchedRecords = records.ToArray();
        SrumUsageRecord[] comparableRecords = matchedRecords.Where(record => record.IsComparable).ToArray();
        IReadOnlyList<WindowsBatteryUsageInfo> apps = BuildAppRows(comparableRecords);
        bool usedComparableRows = true;
        if (isRange && apps.Count == 0)
        {
            apps = BuildAppRows(matchedRecords);
            usedComparableRows = false;
        }

        string status = apps.Count > 0
            ? $"Updated {now:HH:mm} from Windows SRUM."
            : isRange
                ? $"No Windows app impact found for {rangeStart!.Value:HH:mm} - {rangeEnd!.Value:HH:mm}."
                : _cachedStatusText;
        string auditText = apps.Count > 0 ? BuildAuditText(apps, usedComparableRows ? comparableRecords.Length : matchedRecords.Length, isRange, usedComparableRows) : string.Empty;

        return new WindowsBatteryUsageSnapshot { Apps = apps, StatusText = status, AuditText = auditText, UpdatedAt = now };
    }

    private static IReadOnlyList<WindowsBatteryUsageInfo> BuildAppRows(IReadOnlyList<SrumUsageRecord> records)
    {
        var totals = new Dictionary<string, UsageAccumulator>(StringComparer.OrdinalIgnoreCase);
        foreach (SrumUsageRecord record in records)
        {
            if (record.EnergyMilliJoules <= 0)
            {
                continue;
            }

            UsageAccumulator accumulator = totals.GetValueOrDefault(record.Name);
            double scale = record.RangeScale > 0 ? record.RangeScale : 1;
            accumulator.EnergyMilliJoules += record.EnergyMilliJoules * scale;
            accumulator.ForegroundMinutes += record.ForegroundMinutes * scale;
            accumulator.BackgroundMinutes += record.BackgroundMinutes * scale;
            accumulator.SourceRowCount++;
            totals[record.Name] = accumulator;
        }

        double totalEnergy = totals.Values.Sum(item => item.EnergyMilliJoules);
        double maxEnergy = totals.Values.Select(item => item.EnergyMilliJoules).DefaultIfEmpty(1).Max();
        if (totalEnergy <= 0)
        {
            return [];
        }

        WindowsBatteryUsageInfo[] ranked = totals
            .OrderByDescending(pair => pair.Value.EnergyMilliJoules)
            .Select(pair => new WindowsBatteryUsageInfo
            {
                Name = pair.Key,
                EnergyMilliJoules = pair.Value.EnergyMilliJoules,
                Percent = Math.Clamp(pair.Value.EnergyMilliJoules / totalEnergy * 100d, 0, 100),
                BarPercent = Math.Clamp(pair.Value.EnergyMilliJoules / maxEnergy * 100d, 0, 100),
                ForegroundMinutes = pair.Value.ForegroundMinutes,
                BackgroundMinutes = pair.Value.BackgroundMinutes,
                SourceRowCount = pair.Value.SourceRowCount
            })
            .ToArray();

        WindowsBatteryUsageInfo[] topApps = ranked.Take(5).ToArray();
        WindowsBatteryUsageInfo[] otherApps = ranked.Skip(5).ToArray();
        if (otherApps.Length == 0)
        {
            return topApps;
        }

        double otherEnergy = otherApps.Sum(app => app.EnergyMilliJoules);
        double otherForeground = otherApps.Sum(app => app.ForegroundMinutes);
        double otherBackground = otherApps.Sum(app => app.BackgroundMinutes);
        int otherRows = otherApps.Sum(app => app.SourceRowCount);
        double otherPercent = Math.Clamp(otherEnergy / totalEnergy * 100d, 0, 100);
        return topApps
            .Append(new WindowsBatteryUsageInfo
            {
                Name = "Other apps/services",
                EnergyMilliJoules = otherEnergy,
                Percent = otherPercent,
                BarPercent = Math.Clamp(otherEnergy / maxEnergy * 100d, 0, 100),
                ForegroundMinutes = otherForeground,
                BackgroundMinutes = otherBackground,
                SourceRowCount = otherRows,
                IsOther = true
            })
            .ToArray();
    }

    private static string BuildAuditText(IReadOnlyList<WindowsBatteryUsageInfo> apps, int matchedRows, bool isRange, bool isComparable)
    {
        double topPercent = apps.Where(app => !app.IsOther).Sum(app => app.Percent);
        double otherPercent = apps.FirstOrDefault(app => app.IsOther)?.Percent ?? 0;
        string scope = isRange ? "Selected range" : "Last 24h";
        string mode = isComparable ? "Windows comparable" : "all matched SRUM rows";
        return $"{scope}: top apps {topPercent:N0}% | Other {otherPercent:N0}% | SRUM rows {matchedRows:N0} | {mode}";
    }

    private static double CalculateRangeScale(SrumUsageRecord record, DateTimeOffset rangeStart, DateTimeOffset rangeEnd)
    {
        if (record.Timestamp is not { } timestamp)
        {
            return 0;
        }

        if (record.Duration <= TimeSpan.Zero)
        {
            return timestamp >= rangeStart && timestamp < rangeEnd ? 1 : 0;
        }

        DateTimeOffset recordStart = timestamp - record.Duration;
        DateTimeOffset overlapStart = recordStart > rangeStart ? recordStart : rangeStart;
        DateTimeOffset overlapEnd = timestamp < rangeEnd ? timestamp : rangeEnd;
        double overlapMilliseconds = (overlapEnd - overlapStart).TotalMilliseconds;
        if (overlapMilliseconds <= 0)
        {
            return 0;
        }

        return Math.Clamp(overlapMilliseconds / record.Duration.TotalMilliseconds, 0, 1);
    }

    private static SrumRecordLoadResult LoadRecords()
    {
        string folder = Path.Combine(Path.GetTempPath(), "PowerTray");
        Directory.CreateDirectory(folder);
        string outputPath = Path.Combine(folder, $"srum-{Guid.NewGuid():N}.csv");
        string xmlOutputPath = Path.Combine(folder, $"srum-{Guid.NewGuid():N}.xml");

        try
        {
            CommandResult result = RunPowerCfg(outputPath, xml: false);
            if (!result.Success || !File.Exists(outputPath))
            {
                string message = CctkService.IsAdministrator()
                    ? $"Windows battery usage unavailable: {CleanMessage(result.Message)}"
                    : "Windows battery usage needs administrator access.";
                return new SrumRecordLoadResult([], message);
            }

            SrumRecordParseResult parseResult = ParseCsvRecords(File.ReadAllText(outputPath));
            return new SrumRecordLoadResult(parseResult.Records, parseResult.StatusText);
        }
        catch (Exception ex)
        {
            LogService.Error(ex, "Failed to load Windows battery usage.");
            return new SrumRecordLoadResult([], $"Windows battery usage unavailable: {ex.Message}");
        }
        finally
        {
            TryDelete(outputPath);
            TryDelete(xmlOutputPath);
        }
    }

    private static CommandResult RunPowerCfg(string outputPath, bool xml)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "powercfg.exe",
                Arguments = $"/srumutil /output \"{outputPath}\" {(xml ? "/xml" : "/csv")}",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using Process process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start powercfg.exe.");
            var stdoutBuilder = new StringBuilder();
            var stderrBuilder = new StringBuilder();
            process.OutputDataReceived += (_, args) =>
            {
                if (args.Data is not null)
                {
                    stdoutBuilder.AppendLine(args.Data);
                }
            };
            process.ErrorDataReceived += (_, args) =>
            {
                if (args.Data is not null)
                {
                    stderrBuilder.AppendLine(args.Data);
                }
            };
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            if (!process.WaitForExit((int)PowerCfgTimeout.TotalMilliseconds))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch
                {
                    // Best-effort cleanup for a stuck powercfg process.
                }

                return new CommandResult
                {
                    Success = false,
                    ExitCode = -1,
                    Message = $"powercfg /srumutil timed out after {PowerCfgTimeout.TotalSeconds:N0}s.",
                    StandardOutput = stdoutBuilder.ToString(),
                    StandardError = stderrBuilder.ToString()
                };
            }

            process.WaitForExit();
            string stdout = stdoutBuilder.ToString();
            string stderr = stderrBuilder.ToString();

            string message = string.IsNullOrWhiteSpace(stderr) ? stdout.Trim() : stderr.Trim();
            if (string.IsNullOrWhiteSpace(message))
            {
                message = process.ExitCode == 0 ? "Windows battery usage loaded." : $"powercfg failed with exit code {process.ExitCode}.";
            }

            return new CommandResult
            {
                Success = process.ExitCode == 0 && File.Exists(outputPath),
                ExitCode = process.ExitCode,
                StandardOutput = stdout,
                StandardError = stderr,
                Message = message
            };
        }
        catch (Exception ex)
        {
            LogService.Error(ex, "Failed to run powercfg /srumutil.");
            return new CommandResult { Success = false, ExitCode = -1, Message = ex.Message, StandardError = ex.ToString() };
        }
    }

    private static SrumRecordParseResult ParseCsvRecords(string csv)
    {
        var records = new List<SrumUsageRecord>();
        string[] lines = csv.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries);
        string[] header = [];
        Dictionary<string, int> columns = new(StringComparer.OrdinalIgnoreCase);
        int appColumn = -1;
        int timeColumn = -1;
        int[] energyColumns = [];
        int[] networkEnergyColumns = [];
        int[] foregroundColumns = [];
        int[] backgroundColumns = [];
        int durationColumn = -1;
        int dataRows = 0;
        string lastHeaderSummary = "no AppId header found";

        foreach (string line in lines)
        {
            string[] fields = SplitCsvLine(line);
            if (fields.Length < 2)
            {
                continue;
            }

            if (LooksLikeHeader(fields))
            {
                header = fields;
                columns = BuildColumnMap(header);
                appColumn = FindColumn(columns, "appid", "application", "processname", "imagename");
                timeColumn = FindTimestampColumn(columns);
                energyColumns = FindEnergyColumns(header);
                networkEnergyColumns = FindExactOrContainsColumns(header, "networkenergyconsumption", "networkenergy");
                foregroundColumns = FindDurationColumns(header, "foreground", "inuse");
                backgroundColumns = FindDurationColumns(header, "background");
                durationColumn = FindColumn(columns, "timeinmsec", "durationms", "durationmilliseconds");
                lastHeaderSummary = energyColumns.Length == 0
                    ? $"found AppId header but no energy columns: {string.Join(", ", header.Take(8))}"
                    : $"found {energyColumns.Length} energy column(s)";
                continue;
            }

            if (header.Length == 0 || appColumn < 0 || appColumn >= fields.Length || energyColumns.Length == 0)
            {
                continue;
            }

            dataRows++;
            string rawAppId = fields[appColumn];
            if (IsInvalidAppId(rawAppId))
            {
                continue;
            }

            string appName = FriendlyName(rawAppId);
            double energy = AdjustComparableEnergy(rawAppId, SumNumeric(fields, energyColumns), SumNumeric(fields, networkEnergyColumns));
            if (string.IsNullOrWhiteSpace(appName) || energy <= 0)
            {
                continue;
            }

            DateTimeOffset? timestamp = timeColumn >= 0 && timeColumn < fields.Length && TryParseTimestamp(fields[timeColumn], out DateTimeOffset parsedTimestamp)
                ? parsedTimestamp
                : null;
            TimeSpan duration = durationColumn >= 0 && durationColumn < fields.Length && TryParseMetric(fields[durationColumn], out double durationMilliseconds)
                ? TimeSpan.FromMilliseconds(Math.Max(0, durationMilliseconds))
                : TimeSpan.Zero;
            records.Add(new SrumUsageRecord(
                appName,
                timestamp,
                duration,
                energy,
                SumNumeric(fields, foregroundColumns) / 60d,
                SumNumeric(fields, backgroundColumns) / 60d,
                IsComparableAppId(rawAppId),
                RangeScale: 1));
        }

        if (records.Count == 0)
        {
            string status = dataRows == 0
                ? $"Windows SRUM export did not contain parsable app energy rows ({lastHeaderSummary})."
                : $"Windows SRUM export had {dataRows} app row(s), but no positive app energy values.";
            return new SrumRecordParseResult([], status, ShouldTryXmlFallback: true);
        }

        return new SrumRecordParseResult(records, $"Parsed {records.Count} Windows SRUM energy row(s).", ShouldTryXmlFallback: false);
    }

    private static SrumRecordParseResult ParseXmlRecords(string xml)
    {
        try
        {
            var document = XDocument.Parse(xml);
            var records = new List<SrumUsageRecord>();
            int appRows = 0;

            foreach (XElement element in document.Descendants())
            {
                Dictionary<string, string> fields = GetDirectFields(element);
                if (!TryGetField(fields, out string appId, "appid", "application", "processname", "imagename"))
                {
                    continue;
                }

                appRows++;
                if (IsInvalidAppId(appId))
                {
                    continue;
                }

                string appName = FriendlyName(appId);
                double energy = AdjustComparableEnergy(appId, SumEnergyFields(fields), SumSpecificEnergyFields(fields, "networkenergyconsumption", "networkenergy"));
                if (string.IsNullOrWhiteSpace(appName) || energy <= 0)
                {
                    continue;
                }

                DateTimeOffset? timestamp = TryGetField(fields, out string timestampText, "timestamp", "time", "datetime", "endtime", "starttime")
                    && TryParseTimestamp(timestampText, out DateTimeOffset parsedTimestamp)
                        ? parsedTimestamp
                        : null;
                TimeSpan duration = TryGetField(fields, out string durationText, "timeinmsec", "durationms", "durationmilliseconds")
                    && TryParseMetric(durationText, out double durationMilliseconds)
                        ? TimeSpan.FromMilliseconds(Math.Max(0, durationMilliseconds))
                        : TimeSpan.Zero;
                records.Add(new SrumUsageRecord(
                    appName,
                    timestamp,
                    duration,
                    energy,
                    SumDurationFields(fields, "foreground", "inuse") / 60d,
                    SumDurationFields(fields, "background") / 60d,
                    IsComparableAppId(appId),
                    RangeScale: 1));
            }

            if (records.Count == 0)
            {
                string status = appRows == 0
                    ? "Windows SRUM XML export did not contain parsable AppId rows."
                    : $"Windows SRUM XML export had {appRows} app row(s), but no positive app energy values.";
                return new SrumRecordParseResult([], status, ShouldTryXmlFallback: false);
            }

            return new SrumRecordParseResult(records, $"Parsed {records.Count} Windows SRUM XML energy row(s).", ShouldTryXmlFallback: false);
        }
        catch (Exception ex)
        {
            LogService.Error(ex, "Failed to parse Windows SRUM XML.");
            return new SrumRecordParseResult([], $"Windows SRUM XML parse failed: {ex.Message}", ShouldTryXmlFallback: false);
        }
    }

    private static bool LooksLikeHeader(IReadOnlyList<string> fields)
    {
        string[] normalizedFields = fields.Select(NormalizeColumnName).ToArray();
        bool hasAppColumn = normalizedFields.Any(field => field is "appid" or "application" or "processname" or "imagename");
        if (!hasAppColumn)
        {
            return false;
        }

        bool hasTimeColumn = normalizedFields.Any(field => field.Contains("timestamp", StringComparison.OrdinalIgnoreCase)
            || field is "time" or "datetime" or "endtime" or "starttime");
        bool hasEnergyColumn = normalizedFields.Any(IsEnergyField);
        return hasTimeColumn || hasEnergyColumn;
    }

    private static Dictionary<string, int> BuildColumnMap(IReadOnlyList<string> header)
    {
        var columns = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < header.Count; i++)
        {
            string key = NormalizeColumnName(header[i]);
            if (!string.IsNullOrWhiteSpace(key) && !columns.ContainsKey(key))
            {
                columns[key] = i;
            }
        }

        return columns;
    }

    private static int FindColumn(IReadOnlyDictionary<string, int> columns, params string[] names)
    {
        foreach (string name in names)
        {
            if (columns.TryGetValue(name, out int index))
            {
                return index;
            }
        }

        return -1;
    }

    private static int FindTimestampColumn(IReadOnlyDictionary<string, int> columns)
    {
        foreach ((string name, int index) in columns)
        {
            if (name.Contains("timestamp", StringComparison.OrdinalIgnoreCase)
                || name is "time" or "datetime" or "endtime" or "starttime")
            {
                return index;
            }
        }

        return -1;
    }

    private static int[] FindEnergyColumns(IReadOnlyList<string> header)
    {
        int[] totalColumns = FindExactOrContainsColumns(header, "totalenergyconsumption", "totalenergy");
        if (totalColumns.Length > 0)
        {
            return totalColumns;
        }

        return FindMetricColumns(header, "energy", "joule", "mj");
    }

    private static int[] FindMetricColumns(IReadOnlyList<string> header, params string[] tokens)
    {
        var matches = new List<int>();
        for (int i = 0; i < header.Count; i++)
        {
            string name = NormalizeColumnName(header[i]);
            if (tokens.Any(token => name.Contains(token, StringComparison.OrdinalIgnoreCase))
                && !name.Contains("appid", StringComparison.OrdinalIgnoreCase)
                && !name.Contains("userid", StringComparison.OrdinalIgnoreCase)
                && !name.Contains("timestamp", StringComparison.OrdinalIgnoreCase))
            {
                matches.Add(i);
            }
        }

        return matches.ToArray();
    }

    private static int[] FindExactOrContainsColumns(IReadOnlyList<string> header, params string[] names)
    {
        var matches = new List<int>();
        for (int i = 0; i < header.Count; i++)
        {
            string normalized = NormalizeColumnName(header[i]);
            if (names.Any(name => normalized.Equals(name, StringComparison.OrdinalIgnoreCase)))
            {
                matches.Add(i);
            }
        }

        if (matches.Count > 0)
        {
            return matches.ToArray();
        }

        for (int i = 0; i < header.Count; i++)
        {
            string normalized = NormalizeColumnName(header[i]);
            if (names.Any(name => normalized.Contains(name, StringComparison.OrdinalIgnoreCase)))
            {
                matches.Add(i);
            }
        }

        return matches.ToArray();
    }

    private static int[] FindDurationColumns(IReadOnlyList<string> header, params string[] tokens)
    {
        var matches = new List<int>();
        for (int i = 0; i < header.Count; i++)
        {
            string name = NormalizeColumnName(header[i]);
            bool isDuration = IsDurationField(name);
            if (isDuration && tokens.Any(token => name.Contains(token, StringComparison.OrdinalIgnoreCase)))
            {
                matches.Add(i);
            }
        }

        return matches.ToArray();
    }

    private static double SumNumeric(IReadOnlyList<string> fields, IEnumerable<int> columns)
    {
        double total = 0;
        foreach (int column in columns)
        {
            if (column >= fields.Count)
            {
                continue;
            }

            if (TryParseMetric(fields[column], out double value))
            {
                total += Math.Max(0, value);
            }
        }

        return total;
    }

    private static double SumEnergyFields(IReadOnlyDictionary<string, string> fields)
    {
        string[] preferredFields = ["totalenergyconsumption", "totalenergy"];
        double preferred = preferredFields
            .Where(fields.ContainsKey)
            .Select(key => TryParseMetric(fields[key], out double value) ? value : 0)
            .Sum();
        if (preferred > 0)
        {
            return preferred;
        }

        return fields
            .Where(pair => IsEnergyField(pair.Key))
            .Select(pair => TryParseMetric(pair.Value, out double value) ? value : 0)
            .Where(value => value > 0)
            .Sum();
    }

    private static double SumSpecificEnergyFields(IReadOnlyDictionary<string, string> fields, params string[] names) =>
        names
            .Where(fields.ContainsKey)
            .Select(key => TryParseMetric(fields[key], out double value) ? value : 0)
            .Where(value => value > 0)
            .Sum();

    private static double AdjustComparableEnergy(string appId, double totalEnergy, double networkEnergy)
    {
        if (appId.Equals("System", StringComparison.OrdinalIgnoreCase) && networkEnergy > 0)
        {
            return Math.Max(0, totalEnergy - networkEnergy);
        }

        return totalEnergy;
    }

    private static double SumDurationFields(IReadOnlyDictionary<string, string> fields, params string[] tokens) =>
        fields
            .Where(pair => IsDurationField(pair.Key) && tokens.Any(token => pair.Key.Contains(token, StringComparison.OrdinalIgnoreCase)))
            .Select(pair => TryParseMetric(pair.Value, out double value) ? value : 0)
            .Where(value => value > 0)
            .Sum();

    private static bool IsEnergyField(string name) =>
        (name.Contains("energy", StringComparison.OrdinalIgnoreCase)
            || name.Contains("joule", StringComparison.OrdinalIgnoreCase)
            || name.Contains("mj", StringComparison.OrdinalIgnoreCase))
        && !name.Contains("appid", StringComparison.OrdinalIgnoreCase)
        && !name.Contains("userid", StringComparison.OrdinalIgnoreCase)
        && !name.Contains("timestamp", StringComparison.OrdinalIgnoreCase);

    private static bool IsDurationField(string name) =>
        name.Contains("time", StringComparison.OrdinalIgnoreCase)
        || name.Contains("duration", StringComparison.OrdinalIgnoreCase)
        || name.Contains("seconds", StringComparison.OrdinalIgnoreCase);

    private static Dictionary<string, string> GetDirectFields(XElement element)
    {
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (XAttribute attribute in element.Attributes())
        {
            AddField(fields, attribute.Name.LocalName, attribute.Value);
        }

        foreach (XElement child in element.Elements())
        {
            if (!child.HasElements)
            {
                AddField(fields, child.Name.LocalName, child.Attribute("Value")?.Value ?? child.Value);
            }
        }

        return fields;
    }

    private static void AddField(Dictionary<string, string> fields, string name, string value)
    {
        string normalized = NormalizeColumnName(name);
        if (!string.IsNullOrWhiteSpace(normalized) && !fields.ContainsKey(normalized))
        {
            fields[normalized] = value;
        }
    }

    private static bool TryGetField(IReadOnlyDictionary<string, string> fields, out string value, params string[] names)
    {
        foreach (string name in names)
        {
            if (fields.TryGetValue(name, out value!))
            {
                return true;
            }
        }

        value = string.Empty;
        return false;
    }

    private static bool TryParseMetric(string raw, out double value)
    {
        string text = raw.Trim();
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            && long.TryParse(text[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out long hexValue))
        {
            value = hexValue;
            return true;
        }

        if (double.TryParse(text, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out value)
            || double.TryParse(text, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.CurrentCulture, out value))
        {
            return true;
        }

        string numeric = new(text.Where(c => char.IsDigit(c) || c is '-' or '+' or '.' or ',').ToArray());
        if (string.IsNullOrWhiteSpace(numeric))
        {
            value = 0;
            return false;
        }

        return double.TryParse(numeric, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out value)
            || double.TryParse(numeric, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.CurrentCulture, out value);
    }

    private static bool TryParseTimestamp(string value, out DateTimeOffset timestamp)
    {
        DateTimeStyles styles = HasExplicitOffset(value)
            ? DateTimeStyles.None
            : DateTimeStyles.AssumeUniversal;

        if (DateTimeOffset.TryParse(value, CultureInfo.CurrentCulture, styles, out timestamp)
            || DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, styles, out timestamp))
        {
            timestamp = timestamp.ToLocalTime();
            return true;
        }

        timestamp = default;
        return false;
    }

    private static bool HasExplicitOffset(string value)
    {
        string text = value.Trim();
        if (text.EndsWith("Z", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        int timeSeparatorIndex = text.IndexOf('T');
        if (timeSeparatorIndex < 0)
        {
            timeSeparatorIndex = text.IndexOf(' ');
        }

        if (timeSeparatorIndex < 0)
        {
            return false;
        }

        return text.IndexOf('+', timeSeparatorIndex) >= 0 || text.IndexOf('-', timeSeparatorIndex) >= 0;
    }

    private static string FriendlyName(string value)
    {
        string name = value.Trim().Trim('"');
        if (string.IsNullOrWhiteSpace(name))
        {
            return string.Empty;
        }

        if (TryGetPackageFriendlyName(name, out string packageFriendlyName))
        {
            return packageFriendlyName;
        }

        int bangIndex = name.LastIndexOf('!');
        if (bangIndex >= 0 && bangIndex < name.Length - 1)
        {
            name = name[(bangIndex + 1)..];
        }

        if (name.Contains('\\') || name.Contains('/'))
        {
            try
            {
                name = Path.GetFileNameWithoutExtension(name);
            }
            catch
            {
                name = name.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? name;
            }
        }

        int bracketIndex = name.IndexOf(" [", StringComparison.Ordinal);
        if (bracketIndex > 0)
        {
            name = name[..bracketIndex];
        }

        name = name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
        if (TryGetPackageFriendlyName(name, out packageFriendlyName))
        {
            return packageFriendlyName;
        }

        return KnownNames.TryGetValue(name, out string? friendlyName) ? friendlyName : name;
    }

    private static bool IsInvalidAppId(string value)
    {
        string name = value.Trim();
        return string.IsNullOrWhiteSpace(name)
            || name.Equals("Unknown", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("EMI_", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("E3_", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsComparableAppId(string value) =>
        !IsInvalidAppId(value)
        && !value.Trim().Equals("System Interrupts", StringComparison.OrdinalIgnoreCase)
        && !IsHiddenWindowsComponent(value.Trim());

    private static bool IsHiddenWindowsComponent(string value)
    {
        string processName = ExtractProcessName(value);
        if (HiddenProcessNames.Contains(processName))
        {
            return true;
        }

        return value.Contains("MicrosoftWindows.Client.CBS", StringComparison.OrdinalIgnoreCase)
            || value.Contains("Microsoft.AAD.BrokerPlugin", StringComparison.OrdinalIgnoreCase)
            || value.Contains("Microsoft.LockApp", StringComparison.OrdinalIgnoreCase)
            || value.Contains("Microsoft.Windows.ContentDeliveryManager", StringComparison.OrdinalIgnoreCase)
            || value.Contains("Microsoft.Windows.ShellExperienceHost", StringComparison.OrdinalIgnoreCase)
            || value.Contains("Microsoft.WindowsStore", StringComparison.OrdinalIgnoreCase)
            || value.Contains("Microsoft.StorePurchaseApp", StringComparison.OrdinalIgnoreCase);
    }

    private static string ExtractProcessName(string value)
    {
        string name = value.Trim();
        int bracketIndex = name.IndexOf(" [", StringComparison.Ordinal);
        if (bracketIndex > 0)
        {
            name = name[..bracketIndex];
        }

        if (name.Contains('\\') || name.Contains('/'))
        {
            try
            {
                name = Path.GetFileNameWithoutExtension(name);
            }
            catch
            {
                name = name.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? name;
            }
        }

        return name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
    }

    private static bool TryGetPackageFriendlyName(string value, out string friendlyName)
    {
        string candidate = value.Trim();
        if (KnownNames.TryGetValue(candidate, out friendlyName!))
        {
            return true;
        }

        string packageName = ExtractPackageName(candidate);
        if (KnownNames.TryGetValue(packageName, out friendlyName!))
        {
            return true;
        }

        if (!packageName.Equals(candidate, StringComparison.Ordinal))
        {
            friendlyName = HumanizePackageName(packageName);
            return !string.IsNullOrWhiteSpace(friendlyName);
        }

        friendlyName = string.Empty;
        return false;
    }

    private static string ExtractPackageName(string value)
    {
        string[] parts = value.Split('_', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 3 && LooksLikeVersion(parts[1]))
        {
            return parts[0];
        }

        return value;
    }

    private static bool LooksLikeVersion(string value) =>
        value.Count(c => c == '.') >= 1 && value.All(c => char.IsDigit(c) || c == '.');

    private static string HumanizePackageName(string value)
    {
        string name = value;
        foreach (string prefix in new[] { "Microsoft.", "MicrosoftWindows.", "Windows.", "OpenAI." })
        {
            if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                name = name[prefix.Length..];
                break;
            }
        }

        name = name.Replace('.', ' ');
        var result = new List<char>();
        for (int i = 0; i < name.Length; i++)
        {
            char c = name[i];
            if (i > 0 && char.IsUpper(c) && char.IsLower(name[i - 1]))
            {
                result.Add(' ');
            }

            result.Add(c);
        }

        return new string(result.ToArray()).Trim();
    }

    private static string NormalizeColumnName(string value) =>
        string.Concat(value.Where(char.IsLetterOrDigit)).ToLowerInvariant();

    private static string CleanMessage(string message)
    {
        string cleaned = message.Replace("\r", " ").Replace("\n", " ").Trim();
        return string.IsNullOrWhiteSpace(cleaned) ? "powercfg /srumutil failed." : cleaned;
    }

    private static string[] SplitCsvLine(string line)
    {
        var fields = new List<string>();
        var current = new List<char>();
        bool inQuotes = false;
        char delimiter = DetectDelimiter(line);
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Add('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (c == delimiter && !inQuotes)
            {
                fields.Add(new string(current.ToArray()));
                current.Clear();
            }
            else
            {
                current.Add(c);
            }
        }

        fields.Add(new string(current.ToArray()));
        return fields.ToArray();
    }

    private static char DetectDelimiter(string line)
    {
        int commaCount = CountOutsideQuotes(line, ',');
        int semicolonCount = CountOutsideQuotes(line, ';');
        int tabCount = CountOutsideQuotes(line, '\t');
        if (semicolonCount > commaCount && semicolonCount >= tabCount)
        {
            return ';';
        }

        return tabCount > commaCount ? '\t' : ',';
    }

    private static int CountOutsideQuotes(string text, char target)
    {
        int count = 0;
        bool inQuotes = false;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '"')
            {
                if (inQuotes && i + 1 < text.Length && text[i + 1] == '"')
                {
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (c == target && !inQuotes)
            {
                count++;
            }
        }

        return count;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Temporary SRUM export cleanup is best-effort.
        }
    }

    private readonly record struct SrumUsageRecord(
        string Name,
        DateTimeOffset? Timestamp,
        TimeSpan Duration,
        double EnergyMilliJoules,
        double ForegroundMinutes,
        double BackgroundMinutes,
        bool IsComparable,
        double RangeScale);

    private struct UsageAccumulator
    {
        public double EnergyMilliJoules;
        public double ForegroundMinutes;
        public double BackgroundMinutes;
        public int SourceRowCount;
    }

    private sealed record SrumRecordLoadResult(IReadOnlyList<SrumUsageRecord> Records, string StatusText);
    private sealed record SrumRecordParseResult(IReadOnlyList<SrumUsageRecord> Records, string StatusText, bool ShouldTryXmlFallback);
}
