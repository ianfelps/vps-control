using System.Runtime.CompilerServices;

namespace VpsControl.Core.Containers;

/// <summary>Containers sintéticos para desenvolvimento e testes. Ações alteram o estado em memória.</summary>
public sealed class FakeContainerRuntime : IContainerRuntime
{
    private readonly Lock _gate = new();
    private readonly TimeProvider _time;
    private readonly Dictionary<string, FakeContainer> _containers;

    public FakeContainerRuntime() : this(TimeProvider.System) { }

    public FakeContainerRuntime(TimeProvider time)
    {
        _time = time;
        var now = time.GetUtcNow();
        FakeContainer[] seed =
        [
            new("a1b2c3d4e5f60000000000000000000000000000000000000000000000000001", "blog-web-1", "nginx:1.27-alpine", "blog", "web",
                [new PortMapping(80, 8081, "tcp", "127.0.0.1")], ["NGINX_HOST=blog.example.com"], ContainerHealth.Healthy, 512, 0.8),
            new("a1b2c3d4e5f60000000000000000000000000000000000000000000000000002", "blog-db-1", "postgres:16", "blog", "db",
                [], ["POSTGRES_USER=blog", "POSTGRES_PASSWORD=super-secret", "PGDATA=/var/lib/postgresql/data"], ContainerHealth.Healthy, 1024, 2.1),
            new("a1b2c3d4e5f60000000000000000000000000000000000000000000000000003", "shop-api-1", "ghcr.io/example/shop-api:1.4.2", "shop", "api",
                [new PortMapping(8080, 8082, "tcp", "127.0.0.1")], ["ASPNETCORE_ENVIRONMENT=Production", "ConnectionStrings__Db=Host=db;Password=abc", "JWT_SIGNING_KEY=xyz"], ContainerHealth.Unhealthy, 2048, 14.5),
            new("a1b2c3d4e5f60000000000000000000000000000000000000000000000000004", "shop-worker-1", "ghcr.io/example/shop-worker:1.4.2", "shop", "worker",
                [], ["QUEUE_URL=redis://:pass@redis:6379"], ContainerHealth.None, 2048, 6.3),
            new("a1b2c3d4e5f60000000000000000000000000000000000000000000000000005", "backup-job", "alpine:3.22", null, null,
                [], ["S3_ACCESS_KEY=AKIA..."], ContainerHealth.None, 256, 0) { State = "exited", ExitCode = 1 },
            new("a1b2c3d4e5f60000000000000000000000000000000000000000000000000006", "vps-control-api-1", "vps-control-api:latest", "vps-control", "api",
                [new PortMapping(8080, 8080, "tcp", "127.0.0.1")], ["VPSCTL_Metrics__Source=Agent"], ContainerHealth.Healthy, 256, 1.2) { Protected = true },
        ];
        foreach (var c in seed)
        {
            c.CreatedAt = now.AddDays(-12);
            c.StartedAt = c.State == "running" ? now.AddHours(-30) : now.AddHours(-5);
        }

        _containers = seed.ToDictionary(c => c.Id, StringComparer.Ordinal);
    }

    public Task<IReadOnlyList<ContainerSummary>> ListAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            IReadOnlyList<ContainerSummary> list = _containers.Values.Select(Summary).ToList();
            return Task.FromResult(list);
        }
    }

    public Task<ContainerDetails> InspectAsync(string id, bool revealEnv, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            var c = Find(id);
            return Task.FromResult(new ContainerDetails(
                Summary(c),
                c.StartedAt,
                c.State == "running" ? null : c.StartedAt.AddMinutes(3),
                c.ExitCode,
                c.RestartCount,
                "unless-stopped",
                "/docker-entrypoint.sh",
                EnvMasker.Parse(c.Env, revealEnv),
                Labels(c),
                [new MountInfo("volume", $"{c.Name}_data", "/data", "z", true)],
                [new NetworkAttachment($"{c.Project ?? "bridge"}_default", "172.20.0.10")]));
        }
    }

    public Task<IReadOnlyList<ContainerStats>> StatsAsync(CancellationToken cancellationToken = default)
    {
        var t = _time.GetUtcNow().ToUnixTimeMilliseconds() / 1000.0;
        lock (_gate)
        {
            IReadOnlyList<ContainerStats> stats = _containers.Values
                .Where(c => c.State == "running")
                .Select((c, i) => new ContainerStats(
                    c.Id,
                    Math.Round(Math.Max(0, c.BaseCpu * (1 + 0.4 * Math.Sin(t / 7 + i))), 1),
                    (long)(c.MemoryMiB * 1024L * 1024 * (0.45 + 0.1 * Math.Sin(t / 30 + i))),
                    c.MemoryMiB * 1024L * 1024,
                    (long)(1e8 * (i + 1) + t * 1000),
                    (long)(4e7 * (i + 1) + t * 400),
                    50_000_000L * (i + 1),
                    10_000_000L * (i + 1),
                    4 + i))
                .ToList();
            return Task.FromResult(stats);
        }
    }

    public async IAsyncEnumerable<LogLine> LogsAsync(
        string id,
        int tail,
        bool follow,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        string name;
        lock (_gate)
        {
            name = Find(id).Name;
        }

        var now = _time.GetUtcNow();
        var count = Math.Min(tail, 50);
        for (var i = count; i > 0; i--)
        {
            yield return Line(name, now.AddSeconds(-i * 5), count - i);
        }

        if (!follow)
        {
            yield break;
        }

        var n = count;
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(1500), _time, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                yield break;
            }

            yield return Line(name, _time.GetUtcNow(), n++);
        }
    }

    public Task ExecuteAsync(string id, ContainerAction action, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            var c = Find(id);
            switch (action)
            {
                case ContainerAction.Start:
                case ContainerAction.Restart:
                    if (action == ContainerAction.Restart || c.State != "running")
                    {
                        c.StartedAt = _time.GetUtcNow();
                    }

                    c.State = "running";
                    c.ExitCode = 0;
                    if (action == ContainerAction.Restart)
                    {
                        c.RestartCount++;
                    }

                    break;
                case ContainerAction.Stop:
                    c.State = "exited";
                    c.ExitCode = 0;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(action));
            }
        }

        return Task.CompletedTask;
    }

    private FakeContainer Find(string id) =>
        _containers.Values.FirstOrDefault(c =>
            c.Id.StartsWith(id, StringComparison.Ordinal) || c.Name.Equals(id, StringComparison.Ordinal))
        ?? throw new ContainerNotFoundException(id);

    private ContainerSummary Summary(FakeContainer c)
    {
        var running = c.State == "running";
        var uptime = _time.GetUtcNow() - c.StartedAt;
        var status = running
            ? $"Up {(uptime.TotalHours >= 1 ? $"{(int)uptime.TotalHours} hours" : $"{(int)uptime.TotalMinutes} minutes")}"
              + c.Health switch
              {
                  ContainerHealth.Healthy => " (healthy)",
                  ContainerHealth.Unhealthy => " (unhealthy)",
                  _ => string.Empty,
              }
            : $"Exited ({c.ExitCode})";
        return new ContainerSummary(
            c.Id, c.Name, c.Image, c.State, status, running ? c.Health : ContainerHealth.None, c.CreatedAt,
            c.Project, c.Service, c.Protected, c.Ports);
    }

    private static Dictionary<string, string> Labels(FakeContainer c)
    {
        var labels = new Dictionary<string, string>(StringComparer.Ordinal) { ["org.opencontainers.image.source"] = "https://example.com" };
        if (c.Project is not null)
        {
            labels[ContainerLabels.ComposeProject] = c.Project;
            labels[ContainerLabels.ComposeService] = c.Service!;
        }

        if (c.Protected)
        {
            labels[ContainerLabels.Protected] = "true";
        }

        return labels;
    }

    private static LogLine Line(string name, DateTimeOffset at, int n) =>
        n % 11 == 7
            ? new LogLine(at, "stderr", $"WARN {name}: tempo de resposta acima de 500ms (req #{n})")
            : new LogLine(at, "stdout", $"INFO {name}: GET /health 200 em {3 + n % 9}ms (req #{n})");

    private sealed record FakeContainer(
        string Id,
        string Name,
        string Image,
        string? Project,
        string? Service,
        IReadOnlyList<PortMapping> Ports,
        IReadOnlyList<string> Env,
        ContainerHealth Health,
        int MemoryMiB,
        double BaseCpu)
    {
        public string State { get; set; } = "running";
        public int ExitCode { get; set; }
        public int RestartCount { get; set; }
        public bool Protected { get; init; }
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset StartedAt { get; set; }
    }
}
