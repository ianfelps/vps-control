namespace VpsControl.Core.Metrics;

/// <summary>
/// Gera métricas sintéticas plausíveis. Usado em desenvolvimento (Windows/macOS) e testes,
/// onde /proc e o agente não existem.
/// </summary>
public sealed class FakeHostMetricsProvider(TimeProvider timeProvider) : IHostMetricsProvider
{
    private const long GiB = 1024L * 1024 * 1024;
    private readonly DateTimeOffset _bootTime = timeProvider.GetUtcNow().AddDays(-3).AddHours(-7);
    private long _rx = 12 * GiB;
    private long _tx = 3 * GiB;

    public FakeHostMetricsProvider() : this(TimeProvider.System) { }

    public Task<HostSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow();
        var t = now.ToUnixTimeMilliseconds() / 1000.0;

        // Ondas suaves para os gráficos parecerem vivos sem depender de aleatoriedade.
        var cpu = Clamp(25 + 18 * Math.Sin(t / 20) + 7 * Math.Sin(t / 3.7));
        var memAvailable = (long)((3.2 + 0.6 * Math.Sin(t / 45)) * GiB);
        var rxRate = Math.Max(0, 180_000 + 120_000 * Math.Sin(t / 9));
        var txRate = Math.Max(0, 60_000 + 40_000 * Math.Cos(t / 11));
        _rx += (long)(rxRate * 2);
        _tx += (long)(txRate * 2);

        var snapshot = new HostSnapshot(
            now,
            new HostInfo("vps-dev", "Ubuntu 24.04.3 LTS (fake)", "6.8.0-fake", now - _bootTime, 4),
            new CpuUsage(Math.Round(cpu, 1), 0.82, 0.65, 0.51),
            new MemoryUsage(8 * GiB, memAvailable, 2 * GiB, (long)(1.8 * GiB)),
            [
                new DiskUsage("/", "ext4", 80 * GiB, 47 * GiB),
                new DiskUsage("/boot", "ext4", 2 * GiB, (long)(1.6 * GiB)),
            ],
            [new NetworkUsage("eth0", _rx, _tx, rxRate, txRate)]);

        return Task.FromResult(snapshot);
    }

    private static double Clamp(double value) => Math.Clamp(value, 0, 100);
}
