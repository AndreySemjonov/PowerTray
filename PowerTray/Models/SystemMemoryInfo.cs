namespace PowerTray.Models;

public sealed class SystemMemoryInfo
{
    public ulong TotalBytes { get; init; }
    public ulong AvailableBytes { get; init; }
    public ulong UsedBytes => TotalBytes > AvailableBytes ? TotalBytes - AvailableBytes : 0;
    public double UsedPercent => TotalBytes == 0 ? 0 : UsedBytes * 100d / TotalBytes;

    public string UsedText => $"{UsedBytes / 1024d / 1024d / 1024d:N1} GB";
    public string TotalText => $"{TotalBytes / 1024d / 1024d / 1024d:N1} GB";
}
