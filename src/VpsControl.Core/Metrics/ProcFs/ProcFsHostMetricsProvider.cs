using System.Runtime.Versioning;

namespace VpsControl.Core.Metrics.ProcFs;

/// <summary>
/// Coleta métricas lendo /proc e /sys. Somente Linux. Mantém a amostra anterior para calcular
/// taxas (CPU %, bytes/s), por isso deve ser registrado como singleton.
/// </summary>
[SupportedOSPlatform("linux")]
public sealed class ProcFsHostMetricsProvider(TimeProvider timeProvider, string procRoot = "/proc", string etcRoot = "/etc")
    : IHostMetricsProvider
{
    private readonly Lock _gate = new();
    private ProcFsParsers.CpuTimes? _lastCpu;
    private Dictionary<string, ProcFsParsers.NetCounters> _lastNet = new(StringComparer.Ordinal);
    private DateTimeOffset _lastSampleAt;

    public ProcFsHostMetricsProvider() : this(TimeProvider.System) { }

    public async Task<HostSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        var statTask = Read("stat", cancellationToken);
        var memTask = Read("meminfo", cancellationToken);
        var netTask = Read("net/dev", cancellationToken);
        var loadTask = Read("loadavg", cancellationToken);
        var uptimeTask = Read("uptime", cancellationToken);
        var mountsTask = Read("mounts", cancellationToken);
        var kernelTask = Read("sys/kernel/osrelease", cancellationToken);
        var osTask = ReadOptional(Path.Combine(etcRoot, "os-release"), cancellationToken);
        await Task.WhenAll((Task)statTask, memTask, netTask, loadTask, uptimeTask, mountsTask, kernelTask, osTask)
            .ConfigureAwait(false);

        var now = timeProvider.GetUtcNow();
        var cpuNow = ProcFsParsers.ParseCpuTimes(statTask.Result);
        var netNow = ProcFsParsers.ParseNetDev(netTask.Result)
            .Where(n => ProcFsParsers.IsRelevantInterface(n.Interface))
            .ToList();

        double cpuPercent;
        List<NetworkUsage> networks;
        lock (_gate)
        {
            cpuPercent = _lastCpu is { } prev ? ProcFsParsers.CpuPercent(prev, cpuNow) : 0;
            var seconds = _lastCpu is null ? 0 : (now - _lastSampleAt).TotalSeconds;
            networks = netNow.Select(n =>
            {
                double rx = 0, tx = 0;
                if (seconds > 0 && _lastNet.TryGetValue(n.Interface, out var old))
                {
                    rx = Math.Max(0, (n.RxBytes - old.RxBytes) / seconds);
                    tx = Math.Max(0, (n.TxBytes - old.TxBytes) / seconds);
                }

                return new NetworkUsage(n.Interface, n.RxBytes, n.TxBytes, Math.Round(rx), Math.Round(tx));
            }).ToList();

            _lastCpu = cpuNow;
            _lastNet = netNow.ToDictionary(n => n.Interface, StringComparer.Ordinal);
            _lastSampleAt = now;
        }

        var (l1, l5, l15) = ProcFsParsers.ParseLoadAvg(loadTask.Result);
        var info = new HostInfo(
            Environment.MachineName,
            osTask.Result is { } os ? ProcFsParsers.ParseOsPrettyName(os) : "Linux",
            kernelTask.Result.Trim(),
            ProcFsParsers.ParseUptime(uptimeTask.Result),
            Environment.ProcessorCount);

        return new HostSnapshot(
            now,
            info,
            new CpuUsage(cpuPercent, l1, l5, l15),
            ProcFsParsers.ParseMemInfo(memTask.Result),
            ReadDisks(mountsTask.Result),
            networks);
    }

    private static List<DiskUsage> ReadDisks(string mounts)
    {
        var disks = new List<DiskUsage>();
        foreach (var m in ProcFsParsers.ParseMounts(mounts))
        {
            try
            {
                var drive = new DriveInfo(m.MountPoint);
                if (drive.TotalSize > 0)
                {
                    disks.Add(new DiskUsage(m.MountPoint, m.FileSystem, drive.TotalSize, drive.AvailableFreeSpace));
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Montagem inacessível (ex.: sandbox do systemd); ignora.
            }
        }

        return disks;
    }

    private Task<string> Read(string relative, CancellationToken ct) =>
        File.ReadAllTextAsync(Path.Combine(procRoot, relative), ct);

    private static async Task<string?> ReadOptional(string path, CancellationToken ct) =>
        File.Exists(path) ? await File.ReadAllTextAsync(path, ct).ConfigureAwait(false) : null;
}
