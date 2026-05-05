using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Xml.Linq;
using XPSBatteryTray.Models;

namespace XPSBatteryTray.Services;

public sealed class WindowsBatteryUsageService
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(15);
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

    private readonly object _sync = new();
    private WindowsBatteryUsageSnapshot? _cachedSnapshot;
    private DateTimeOffset _lastAttempt = DateTimeOffset.MinValue;

    public WindowsBatteryUsageSnapshot GetSnapshot()
    {
        lock (_sync)
        {
            DateTimeOffset now = DateTimeOffset.Now;
            if (_cachedSnapshot is not null && now - _lastAttempt < CacheDuration)
            {
                return _cachedSnapshot;
            }

            _lastAttempt = now;
            _cachedSnapshot = LoadSnapshot(now);
            return _cachedSnapshot;
        }
    }

    private static WindowsBatteryUsageSnapshot LoadSnapshot(DateTimeOffset now)
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
                return new WindowsBatteryUsageSnapshot { StatusText = message, UpdatedAt = now };
            }

            string csv = File.ReadAllText(outputPath);
            SrumParseResult parseResult = ParseCsv(csv, now);
            if (parseResult.Apps.Count == 0 && parseResult.ShouldTryXmlFallback)
            {
                CommandResult xmlResult = RunPowerCfg(xmlOutputPath, xml: true);
                if (xmlResult.Success && File.Exists(xmlOutputPath))
                {
                    parseResult = ParseXml(File.ReadAllText(xmlOutputPath), now);
                }
            }

            IReadOnlyList<WindowsBatteryUsageInfo> apps = parseResult.Apps.Take(5).ToArray();
            string status = apps.Count == 0
                ? parseResult.StatusText
                : $"Updated {now:HH:mm} from Windows SRUM.";
            return new WindowsBatteryUsageSnapshot { Apps = apps, StatusText = status, UpdatedAt = now };
        }
        catch (Exception ex)
        {
            LogService.Error(ex, "Failed to load Windows battery usage.");
            return new WindowsBatteryUsageSnapshot { StatusText = $"Windows battery usage unavailable: {ex.Message}", UpdatedAt = now };
        }
        finally
        {
            try
            {
                if (File.Exists(outputPath))
                {
                    File.Delete(outputPath);
                }

                if (File.Exists(xmlOutputPath))
                {
                    File.Delete(xmlOutputPath);
                }
            }
            catch
            {
                // Temporary SRUM export cleanup is best-effort.
            }
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
            string stdout = process.StandardOutput.ReadToEnd();
            string stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();

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

    private static SrumParseResult ParseCsv(string csv, DateTimeOffset now)
    {
        var totals = new Dictionary<string, UsageAccumulator>(StringComparer.OrdinalIgnoreCase);
        string[] lines = csv.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries);
        string[] header = [];
        Dictionary<string, int> columns = new(StringComparer.OrdinalIgnoreCase);
        int appColumn = -1;
        int timeColumn = -1;
        int[] energyColumns = [];
        int[] foregroundColumns = [];
        int[] backgroundColumns = [];
        int dataRows = 0;
        int rowsWithEnergy = 0;
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
                foregroundColumns = FindDurationColumns(header, "foreground", "inuse");
                backgroundColumns = FindDurationColumns(header, "background");
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
            if (timeColumn >= 0 && timeColumn < fields.Length && TryParseTimestamp(fields[timeColumn], out DateTimeOffset timestamp)
                && timestamp < now.AddHours(-24))
            {
                continue;
            }

            string rawAppId = fields[appColumn];
            if (IsIgnoredAppId(rawAppId))
            {
                continue;
            }

            string appName = FriendlyName(rawAppId);
            if (string.IsNullOrWhiteSpace(appName))
            {
                continue;
            }

            double energy = SumNumeric(fields, energyColumns);
            if (energy <= 0)
            {
                continue;
            }

            rowsWithEnergy++;
            UsageAccumulator accumulator = totals.GetValueOrDefault(appName);
            accumulator.EnergyMilliJoules += energy;
            accumulator.ForegroundMinutes += SumNumeric(fields, foregroundColumns) / 60d;
            accumulator.BackgroundMinutes += SumNumeric(fields, backgroundColumns) / 60d;
            totals[appName] = accumulator;
        }

        double totalEnergy = totals.Values.Sum(item => item.EnergyMilliJoules);
        double maxEnergy = totals.Values.Select(item => item.EnergyMilliJoules).DefaultIfEmpty(1).Max();
        if (totalEnergy <= 0)
        {
            string status = dataRows == 0
                ? $"Windows SRUM export did not contain parsable app energy rows ({lastHeaderSummary})."
                : $"Windows SRUM export had {dataRows} app row(s), but no positive energy values in the parsed columns.";
            return new SrumParseResult([], status, ShouldTryXmlFallback: true);
        }

        WindowsBatteryUsageInfo[] apps = totals
            .OrderByDescending(pair => pair.Value.EnergyMilliJoules)
            .Select(pair => new WindowsBatteryUsageInfo
            {
                Name = pair.Key,
                EnergyMilliJoules = pair.Value.EnergyMilliJoules,
                Percent = Math.Clamp(pair.Value.EnergyMilliJoules / totalEnergy * 100d, 0, 100),
                BarPercent = Math.Clamp(pair.Value.EnergyMilliJoules / maxEnergy * 100d, 0, 100),
                ForegroundMinutes = pair.Value.ForegroundMinutes,
                BackgroundMinutes = pair.Value.BackgroundMinutes
            })
            .ToArray();
        return new SrumParseResult(apps, $"Parsed {rowsWithEnergy} Windows SRUM energy row(s).", ShouldTryXmlFallback: false);
    }

    private static SrumParseResult ParseXml(string xml, DateTimeOffset now)
    {
        try
        {
            var document = XDocument.Parse(xml);
            var totals = new Dictionary<string, UsageAccumulator>(StringComparer.OrdinalIgnoreCase);
            int appRows = 0;
            int rowsWithEnergy = 0;

            foreach (XElement element in document.Descendants())
            {
                Dictionary<string, string> fields = GetDirectFields(element);
                if (!TryGetField(fields, out string appId, "appid", "application", "processname", "imagename"))
                {
                    continue;
                }

                appRows++;
                if (TryGetField(fields, out string timestampText, "timestamp", "time", "datetime", "endtime", "starttime")
                    && TryParseTimestamp(timestampText, out DateTimeOffset timestamp)
                    && timestamp < now.AddHours(-24))
                {
                    continue;
                }

                if (IsIgnoredAppId(appId))
                {
                    continue;
                }

                string appName = FriendlyName(appId);
                if (string.IsNullOrWhiteSpace(appName))
                {
                    continue;
                }

                double energy = SumEnergyFields(fields);
                if (energy <= 0)
                {
                    continue;
                }

                rowsWithEnergy++;
                UsageAccumulator accumulator = totals.GetValueOrDefault(appName);
                accumulator.EnergyMilliJoules += energy;
                accumulator.ForegroundMinutes += SumDurationFields(fields, "foreground", "inuse") / 60d;
                accumulator.BackgroundMinutes += SumDurationFields(fields, "background") / 60d;
                totals[appName] = accumulator;
            }

            double totalEnergy = totals.Values.Sum(item => item.EnergyMilliJoules);
            double maxEnergy = totals.Values.Select(item => item.EnergyMilliJoules).DefaultIfEmpty(1).Max();
            if (totalEnergy <= 0)
            {
                string status = appRows == 0
                    ? "Windows SRUM XML export did not contain parsable AppId rows."
                    : $"Windows SRUM XML export had {appRows} app row(s), but no positive energy values.";
                return new SrumParseResult([], status, ShouldTryXmlFallback: false);
            }

            WindowsBatteryUsageInfo[] apps = totals
                .OrderByDescending(pair => pair.Value.EnergyMilliJoules)
                .Select(pair => new WindowsBatteryUsageInfo
                {
                    Name = pair.Key,
                    EnergyMilliJoules = pair.Value.EnergyMilliJoules,
                    Percent = Math.Clamp(pair.Value.EnergyMilliJoules / totalEnergy * 100d, 0, 100),
                    BarPercent = Math.Clamp(pair.Value.EnergyMilliJoules / maxEnergy * 100d, 0, 100),
                    ForegroundMinutes = pair.Value.ForegroundMinutes,
                    BackgroundMinutes = pair.Value.BackgroundMinutes
                })
                .ToArray();
            return new SrumParseResult(apps, $"Parsed {rowsWithEnergy} Windows SRUM XML energy row(s).", ShouldTryXmlFallback: false);
        }
        catch (Exception ex)
        {
            LogService.Error(ex, "Failed to parse Windows SRUM XML.");
            return new SrumParseResult([], $"Windows SRUM XML parse failed: {ex.Message}", ShouldTryXmlFallback: false);
        }
    }

    private static bool LooksLikeHeader(IReadOnlyList<string> fields) =>
        fields.Any(field => NormalizeColumnName(field) == "appid")
        || fields.Any(field => NormalizeColumnName(field).Contains("application", StringComparison.OrdinalIgnoreCase));

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

    private static int[] FindEnergyColumns(IReadOnlyList<string> header)
    {
        int[] totalColumns = FindExactOrContainsColumns(header, "totalenergyconsumption", "totalenergy");
        if (totalColumns.Length > 0)
        {
            return totalColumns;
        }

        return FindMetricColumns(header, "energy", "joule", "mj");
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
            bool isDuration = name.Contains("time", StringComparison.OrdinalIgnoreCase)
                || name.Contains("duration", StringComparison.OrdinalIgnoreCase)
                || name.Contains("seconds", StringComparison.OrdinalIgnoreCase);
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
        if (DateTimeOffset.TryParse(value, CultureInfo.CurrentCulture, DateTimeStyles.AssumeLocal, out timestamp)
            || DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out timestamp))
        {
            return true;
        }

        timestamp = default;
        return false;
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

    private static bool IsIgnoredAppId(string value)
    {
        string name = value.Trim();
        return string.IsNullOrWhiteSpace(name)
            || name.Equals("Unknown", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("EMI_", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("E3_", StringComparison.OrdinalIgnoreCase);
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

    private struct UsageAccumulator
    {
        public double EnergyMilliJoules;
        public double ForegroundMinutes;
        public double BackgroundMinutes;
    }

    private sealed record SrumParseResult(IReadOnlyList<WindowsBatteryUsageInfo> Apps, string StatusText, bool ShouldTryXmlFallback);
}
