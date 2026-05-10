using System.IO.Pipes;
using System.Diagnostics;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using PowerTray.Models;
using PowerTray.Services;

namespace PowerTray.Service;

public sealed class WindowsBatteryUsagePipeServer : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly WindowsBatteryUsageService _batteryUsageService = new();
    private readonly List<Task> _clientTasks = [];

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        LogService.Info("PowerTray battery impact helper pipe server started.");
        while (!cancellationToken.IsCancellationRequested)
        {
            NamedPipeServerStream? pipe = null;
            try
            {
                pipe = CreatePipe();
                await pipe.WaitForConnectionAsync(cancellationToken);
                Task task = HandleClientAsync(pipe, cancellationToken);
                pipe = null;
                lock (_clientTasks)
                {
                    _clientTasks.RemoveAll(item => item.IsCompleted);
                    _clientTasks.Add(task);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                pipe?.Dispose();
                break;
            }
            catch (Exception ex)
            {
                pipe?.Dispose();
                LogService.Error(ex, "PowerTray battery impact pipe server failed.");
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
            }
        }
    }

    public void Dispose()
    {
        Task[] tasks;
        lock (_clientTasks)
        {
            tasks = _clientTasks.ToArray();
            _clientTasks.Clear();
        }

        try
        {
            Task.WaitAll(tasks, TimeSpan.FromSeconds(2));
        }
        catch
        {
            // Clients can be abandoned while the service is stopping.
        }
    }

    private async Task HandleClientAsync(NamedPipeServerStream pipe, CancellationToken cancellationToken)
    {
        await using (pipe.ConfigureAwait(false))
        {
            try
            {
                WindowsBatteryUsageRequest? request = await ReadMessageAsync<WindowsBatteryUsageRequest>(pipe, cancellationToken);
                if (request?.RequestType == WindowsBatteryUsageIpc.CctkReadbackRequestType)
                {
                    CommandResult result = await GetCctkReadbackAsync(request);
                    await WriteMessageAsync(pipe, result, cancellationToken);
                    pipe.WaitForPipeDrain();
                    return;
                }

                WindowsBatteryUsageSnapshot snapshot = request?.RangeStart is { } start && request.RangeEnd is { } end
                    ? _batteryUsageService.GetSnapshot(start, end)
                    : _batteryUsageService.GetSnapshot();

                await WriteMessageAsync(pipe, snapshot, cancellationToken);
                pipe.WaitForPipeDrain();
            }
            catch (Exception ex) when (ex is IOException or JsonException)
            {
                LogService.Error(ex, "PowerTray battery impact client request failed.");
            }
        }
    }

    private static async Task WriteMessageAsync<T>(Stream stream, T value, CancellationToken cancellationToken)
    {
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions);
        byte[] length = BitConverter.GetBytes(payload.Length);
        await stream.WriteAsync(length, cancellationToken);
        await stream.WriteAsync(payload, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    private static async Task<T?> ReadMessageAsync<T>(Stream stream, CancellationToken cancellationToken)
    {
        byte[] lengthBuffer = new byte[sizeof(int)];
        await stream.ReadExactlyAsync(lengthBuffer, cancellationToken);
        int length = BitConverter.ToInt32(lengthBuffer);
        if (length <= 0 || length > WindowsBatteryUsageIpc.MaxMessageBytes)
        {
            throw new IOException($"Invalid client request length: {length}.");
        }

        byte[] payload = new byte[length];
        await stream.ReadExactlyAsync(payload, cancellationToken);
        return JsonSerializer.Deserialize<T>(payload, JsonOptions);
    }

    private static NamedPipeServerStream CreatePipe()
    {
        PipeSecurity security = new();
        SecurityIdentifier system = new(WellKnownSidType.LocalSystemSid, null);
        SecurityIdentifier admins = new(WellKnownSidType.BuiltinAdministratorsSid, null);
        SecurityIdentifier users = new(WellKnownSidType.AuthenticatedUserSid, null);
        security.AddAccessRule(new PipeAccessRule(system, PipeAccessRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(admins, PipeAccessRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(users, PipeAccessRights.ReadWrite, AccessControlType.Allow));

        return NamedPipeServerStreamAcl.Create(
            WindowsBatteryUsageIpc.PipeName,
            PipeDirection.InOut,
            maxNumberOfServerInstances: 4,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous,
            inBufferSize: 16 * 1024,
            outBufferSize: 16 * 1024,
            pipeSecurity: security);
    }

    private static async Task<CommandResult> GetCctkReadbackAsync(WindowsBatteryUsageRequest request)
    {
        if (!IsValidCctkPath(request.CctkPath))
        {
            string message = "Dell Command | Configure cctk.exe was not found.";
            return new CommandResult { Success = false, ExitCode = -1, Message = message, StandardError = message };
        }

        return request.CctkReadback switch
        {
            var value when value.Equals(WindowsBatteryUsageIpc.PrimaryBatteryChargeReadback, StringComparison.OrdinalIgnoreCase)
                => await RunCctkAsync(request.CctkPath, "--PrimaryBattChargeCfg"),
            var value when value.Equals(WindowsBatteryUsageIpc.ThermalManagementReadback, StringComparison.OrdinalIgnoreCase)
                => await ReadThermalManagementAsync(request.CctkPath),
            _ => new CommandResult { Success = false, ExitCode = -1, Message = "Unsupported CCTK readback request." }
        };
    }

    private static async Task<CommandResult> ReadThermalManagementAsync(string cctkPath)
    {
        CommandResult result = await RunCctkAsync(cctkPath, "--thermalmanagement");
        if (result.Success)
        {
            return result;
        }

        if (!RequiresThermalExportReadback(result))
        {
            return result;
        }

        string exportPath = Path.Combine(Path.GetTempPath(), "PowerTray", $"{Guid.NewGuid():N}.ini");
        Directory.CreateDirectory(Path.GetDirectoryName(exportPath)!);
        try
        {
            result = await RunCctkAsync(cctkPath, $"-o {Quote(exportPath)}");
            if (!result.Success)
            {
                return result;
            }

            string? thermalValue = ReadIniValue(exportPath, WindowsBatteryUsageIpc.ThermalManagementReadback);
            if (string.IsNullOrWhiteSpace(thermalValue))
            {
                return new CommandResult
                {
                    Success = false,
                    ExitCode = -1,
                    Message = "Dell thermal setting was not found in the CCTK export."
                };
            }

            string output = $"{WindowsBatteryUsageIpc.ThermalManagementReadback}={thermalValue.Trim()}";
            return new CommandResult
            {
                Success = true,
                ExitCode = 0,
                StandardOutput = output,
                Message = output
            };
        }
        finally
        {
            try
            {
                if (File.Exists(exportPath))
                {
                    File.Delete(exportPath);
                }
            }
            catch (Exception ex)
            {
                LogService.Error(ex, "Failed to delete temporary service CCTK export.");
            }
        }
    }

    private static async Task<CommandResult> RunCctkAsync(string cctkPath, string argument)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = cctkPath,
                Arguments = argument,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using Process process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start cctk.exe.");
            string stdout = await process.StandardOutput.ReadToEndAsync();
            string stderr = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            string message = string.IsNullOrWhiteSpace(stderr) ? stdout.Trim() : stderr.Trim();
            if (string.IsNullOrWhiteSpace(message))
            {
                message = process.ExitCode == 0 ? "Command completed." : $"Command failed with exit code {process.ExitCode}.";
            }

            var result = new CommandResult
            {
                Success = process.ExitCode == 0,
                ExitCode = process.ExitCode,
                StandardOutput = stdout,
                StandardError = stderr,
                Message = message
            };
            LogService.Info($"helper cctk {argument} -> exit {result.ExitCode}. stdout: {stdout.Trim()} stderr: {stderr.Trim()}");
            return result;
        }
        catch (Exception ex)
        {
            LogService.Error(ex, "Failed to run helper cctk.exe.");
            return new CommandResult { Success = false, ExitCode = -1, Message = ex.Message, StandardError = ex.ToString() };
        }
    }

    private static bool IsValidCctkPath(string path)
    {
        if (!File.Exists(path) || !Path.GetFileName(path).Equals("cctk.exe", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string fullPath = Path.GetFullPath(path);
        string[] allowedPaths =
        [
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"Dell\Command Configure\X86_64\cctk.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Dell\Command Configure\X86_64\cctk.exe")
        ];

        return allowedPaths
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate))
            .Select(Path.GetFullPath)
            .Any(candidate => candidate.Equals(fullPath, StringComparison.OrdinalIgnoreCase));
    }

    private static bool RequiresThermalExportReadback(CommandResult result) =>
        result.ExitCode == 65 &&
        result.Message.Contains("ThermalManagement", StringComparison.OrdinalIgnoreCase) &&
        result.Message.Contains("requires an argument", StringComparison.OrdinalIgnoreCase);

    private static string? ReadIniValue(string path, string key)
    {
        foreach (string line in File.ReadLines(path))
        {
            string trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith(';') || trimmed.StartsWith('['))
            {
                continue;
            }

            int separator = trimmed.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            string name = trimmed[..separator].Trim();
            if (name.Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                return trimmed[(separator + 1)..].Trim();
            }
        }

        return null;
    }

    private static string Quote(string value) => "\"" + value.Replace("\"", "\\\"") + "\"";
}
