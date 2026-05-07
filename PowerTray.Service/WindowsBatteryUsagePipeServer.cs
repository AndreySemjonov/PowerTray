using System.IO.Pipes;
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
}
