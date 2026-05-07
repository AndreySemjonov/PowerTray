namespace PowerTray.Models;

public sealed class CommandResult
{
    public bool Success { get; init; }
    public int ExitCode { get; init; }
    public string StandardOutput { get; init; } = string.Empty;
    public string StandardError { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
}
