namespace VpsControl.Core.Metrics;

/// <summary>Instantâneo das métricas do host em um momento.</summary>
public sealed record HostSnapshot(
    DateTimeOffset Timestamp,
    HostInfo Info,
    CpuUsage Cpu,
    MemoryUsage Memory,
    IReadOnlyList<DiskUsage> Disks,
    IReadOnlyList<NetworkUsage> Networks);

public sealed record HostInfo(
    string Hostname,
    string OsName,
    string KernelVersion,
    TimeSpan Uptime,
    int LogicalCores)
{
    public long UptimeSeconds => (long)Uptime.TotalSeconds;
}

/// <param name="UsagePercent">Uso total de CPU (0–100) desde a amostra anterior.</param>
/// <param name="Load1">Load average de 1 minuto.</param>
public sealed record CpuUsage(double UsagePercent, double Load1, double Load5, double Load15);

public sealed record MemoryUsage(long TotalBytes, long AvailableBytes, long SwapTotalBytes, long SwapFreeBytes)
{
    public long UsedBytes => TotalBytes - AvailableBytes;
    public long SwapUsedBytes => SwapTotalBytes - SwapFreeBytes;
}

public sealed record DiskUsage(string MountPoint, string FileSystem, long TotalBytes, long FreeBytes)
{
    public long UsedBytes => TotalBytes - FreeBytes;
}

/// <param name="RxBytesPerSecond">Taxa de recebimento desde a amostra anterior.</param>
public sealed record NetworkUsage(
    string Interface,
    long RxBytesTotal,
    long TxBytesTotal,
    double RxBytesPerSecond,
    double TxBytesPerSecond);
