using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using VpsControl.Core.Containers;
using VpsControl.Core.Containers.Docker;

namespace VpsControl.Core.Tests;

public class DockerMapperTests
{
    [Fact]
    public void List_item_maps_compose_labels_health_protection_and_ports()
    {
        const string json = """
            [{
              "Id": "4f1c2d3e4a5b6c7d8e9f00112233445566778899aabbccddeeff001122334455",
              "Names": ["/shop-api-1"],
              "Image": "ghcr.io/example/api:1.0",
              "Created": 1763400000,
              "State": "running",
              "Status": "Up 2 hours (unhealthy)",
              "Labels": {"com.docker.compose.project": "shop", "com.docker.compose.service": "api", "vpsctl.protected": "true"},
              "Ports": [
                {"IP": "127.0.0.1", "PrivatePort": 8080, "PublicPort": 8082, "Type": "tcp"},
                {"IP": "::1", "PrivatePort": 8080, "PublicPort": 8082, "Type": "tcp"},
                {"PrivatePort": 9090, "Type": "tcp"}
              ]
            }]
            """;

        var item = Assert.Single(JsonSerializer.Deserialize(json, DockerJsonContext.Default.ListDockerContainerListItem)!);
        var c = DockerMapper.ToSummary(item);

        Assert.Equal("shop-api-1", c.Name);
        Assert.Equal("4f1c2d3e4a5b", c.ShortId);
        Assert.Equal("shop", c.ComposeProject);
        Assert.Equal("api", c.ComposeService);
        Assert.Equal(ContainerHealth.Unhealthy, c.Health);
        Assert.True(c.Protected);
        Assert.True(c.Running);
        Assert.Equal(2, c.Ports.Count); // IPv4/IPv6 duplicados viram um
        Assert.Null(c.Ports[1].PublicPort);
    }

    [Fact]
    public void Inspect_masks_sensitive_env_and_parses_nanosecond_timestamps()
    {
        const string json = """
            {
              "Id": "abc123abc123abc123",
              "Name": "/blog-db-1",
              "Created": "2026-11-01T10:00:00.123456789Z",
              "RestartCount": 2,
              "State": {"Status": "exited", "Running": false, "ExitCode": 137,
                        "StartedAt": "2026-11-17T08:00:00.987654321Z", "FinishedAt": "2026-11-17T09:00:00Z"},
              "Config": {"Image": "postgres:16", "Env": ["POSTGRES_PASSWORD=s3cr3t", "PGDATA=/data", "DATABASE_URL=postgres://u:p@db/x"],
                         "Cmd": ["postgres"], "Entrypoint": ["docker-entrypoint.sh"], "Labels": {}, "Tty": false},
              "HostConfig": {"RestartPolicy": {"Name": "unless-stopped"}, "PortBindings": {"5432/tcp": [{"HostIp": "127.0.0.1", "HostPort": "5432"}]}},
              "Mounts": [{"Type": "volume", "Source": "/var/lib/docker/volumes/x", "Destination": "/data", "Mode": "z", "RW": true}],
              "NetworkSettings": {"Networks": {"blog_default": {"IPAddress": "172.18.0.2"}}}
            }
            """;

        var details = DockerMapper.ToDetails(JsonSerializer.Deserialize(json, DockerJsonContext.Default.DockerInspect)!, revealEnv: false);

        Assert.Equal("blog-db-1", details.Summary.Name);
        Assert.Equal(137, details.ExitCode);
        Assert.Equal("unless-stopped", details.RestartPolicy);
        Assert.Equal("docker-entrypoint.sh postgres", details.Command);
        Assert.Equal(new DateTimeOffset(2026, 11, 17, 8, 0, 0, TimeSpan.Zero).AddTicks(9876543), details.StartedAt);
        Assert.Equal(EnvMasker.Mask, details.Env.Single(e => e.Name == "POSTGRES_PASSWORD").Value);
        Assert.Equal(EnvMasker.Mask, details.Env.Single(e => e.Name == "DATABASE_URL").Value);
        Assert.Equal("/data", details.Env.Single(e => e.Name == "PGDATA").Value);
        Assert.Equal(5432, Assert.Single(details.Summary.Ports).PublicPort);
        Assert.Equal("172.18.0.2", Assert.Single(details.Networks).IpAddress);
    }

    [Fact]
    public void Stats_follow_docker_cli_formula()
    {
        const string json = """
            {
              "cpu_stats": {"cpu_usage": {"total_usage": 2000000000}, "system_cpu_usage": 20000000000, "online_cpus": 4},
              "precpu_stats": {"cpu_usage": {"total_usage": 1000000000}, "system_cpu_usage": 10000000000, "online_cpus": 4},
              "memory_stats": {"usage": 300000000, "limit": 1000000000, "stats": {"inactive_file": 100000000}},
              "networks": {"eth0": {"rx_bytes": 1000, "tx_bytes": 500}, "eth1": {"rx_bytes": 10, "tx_bytes": 5}},
              "blkio_stats": {"io_service_bytes_recursive": [{"op": "read", "value": 4096}, {"op": "write", "value": 8192}]},
              "pids_stats": {"current": 7}
            }
            """;

        var s = DockerMapper.ToStats("x", JsonSerializer.Deserialize(json, DockerJsonContext.Default.DockerStats)!);

        Assert.Equal(40.0, s.CpuPercent); // 1e9/1e10 * 4 * 100
        Assert.Equal(200_000_000, s.MemoryUsageBytes);
        Assert.Equal(20.0, s.MemoryPercent);
        Assert.Equal(1010, s.NetworkRxBytes);
        Assert.Equal(505, s.NetworkTxBytes);
        Assert.Equal(4096, s.BlockReadBytes);
        Assert.Equal(8192, s.BlockWriteBytes);
        Assert.Equal(7, s.Pids);
    }

    [Theory]
    [InlineData("Up 5 minutes (healthy)", ContainerHealth.Healthy)]
    [InlineData("Up 5 minutes (unhealthy)", ContainerHealth.Unhealthy)]
    [InlineData("Up 3 seconds (health: starting)", ContainerHealth.Starting)]
    [InlineData("Exited (0) 2 hours ago", ContainerHealth.None)]
    public void Health_is_parsed_from_list_status(string status, ContainerHealth expected) =>
        Assert.Equal(expected, DockerMapper.ParseHealthFromStatus(status));
}

public class DockerLogReaderTests
{
    private static byte[] Frame(byte stream, string text)
    {
        var payload = Encoding.UTF8.GetBytes(text);
        var frame = new byte[8 + payload.Length];
        frame[0] = stream;
        BinaryPrimitives.WriteUInt32BigEndian(frame.AsSpan(4), (uint)payload.Length);
        payload.CopyTo(frame, 8);
        return frame;
    }

    [Fact]
    public async Task Demultiplexes_frames_and_joins_lines_split_across_frames()
    {
        var bytes = Frame(1, "2026-11-17T10:00:00.000000001Z hello\n2026-11-17T10:00:01Z par")
            .Concat(Frame(2, "2026-11-17T10:00:02Z oops\n"))
            .Concat(Frame(1, "tial line\n"))
            .ToArray();

        var lines = new List<LogLine>();
        await foreach (var l in DockerLogReader.ReadAsync(new MemoryStream(bytes), tty: false, CancellationToken.None))
        {
            lines.Add(l);
        }

        Assert.Equal(3, lines.Count);
        Assert.Equal(("stdout", "hello"), (lines[0].Stream, lines[0].Text));
        Assert.Equal(("stderr", "oops"), (lines[1].Stream, lines[1].Text));
        Assert.Equal(("stdout", "partial line"), (lines[2].Stream, lines[2].Text));
        Assert.Equal(new DateTimeOffset(2026, 11, 17, 10, 0, 1, TimeSpan.Zero), lines[2].Timestamp);
    }

    [Fact]
    public async Task Tty_stream_is_read_as_plain_text()
    {
        var bytes = Encoding.UTF8.GetBytes("2026-11-17T10:00:00Z a\r\n2026-11-17T10:00:01Z b\n");
        var lines = new List<LogLine>();
        await foreach (var l in DockerLogReader.ReadAsync(new MemoryStream(bytes), tty: true, CancellationToken.None))
        {
            lines.Add(l);
        }

        Assert.Equal(["a", "b"], lines.Select(l => l.Text));
    }
}

public class EnvMaskerTests
{
    [Theory]
    [InlineData("DB_PASSWORD", true)]
    [InlineData("JWT_SIGNING_KEY", true)]
    [InlineData("GITHUB_TOKEN", true)]
    [InlineData("AWS_SECRET_ACCESS_KEY", true)]
    [InlineData("PATH", false)]
    [InlineData("PWD", false)]
    [InlineData("TZ", false)]
    public void Detects_sensitive_names(string name, bool sensitive) => Assert.Equal(sensitive, EnvMasker.IsSensitive(name));

    [Fact]
    public void Reveal_returns_original_values()
    {
        var env = EnvMasker.Parse(["API_KEY=abc", "EMPTY"], reveal: true);

        Assert.Equal("abc", env[0].Value);
        Assert.False(env[0].Masked);
        Assert.Equal(string.Empty, env[1].Value);
    }
}

public class GuardedContainerRuntimeTests
{
    private readonly GuardedContainerRuntime _runtime = new(new FakeContainerRuntime());

    [Theory]
    [InlineData("a1b2c3d4e5f6")]
    [InlineData("blog-web-1")]
    [InlineData("my_app.worker")]
    public void Accepts_ids_and_names(string id) => Assert.True(GuardedContainerRuntime.IsValidId(id));

    [Theory]
    [InlineData("")]
    [InlineData("../../etc/passwd")]
    [InlineData("abc/json?all=1")]
    [InlineData("-rf")]
    [InlineData("a b")]
    public void Rejects_path_traversal_and_injection(string id) => Assert.False(GuardedContainerRuntime.IsValidId(id));

    [Fact]
    public async Task Invalid_id_never_reaches_runtime()
    {
        await Assert.ThrowsAsync<InvalidContainerIdException>(() => _runtime.ExecuteAsync("../x", ContainerAction.Stop));
        await Assert.ThrowsAsync<InvalidContainerIdException>(() => _runtime.InspectAsync("a/b", false));
    }

    [Fact]
    public async Task Protected_container_cannot_be_stopped_or_restarted_but_can_be_started()
    {
        await Assert.ThrowsAsync<ContainerProtectedException>(() => _runtime.ExecuteAsync("vps-control-api-1", ContainerAction.Stop));
        await Assert.ThrowsAsync<ContainerProtectedException>(() => _runtime.ExecuteAsync("vps-control-api-1", ContainerAction.Restart));
        await _runtime.ExecuteAsync("vps-control-api-1", ContainerAction.Start);
    }

    [Fact]
    public async Task Stop_and_start_change_state()
    {
        await _runtime.ExecuteAsync("blog-web-1", ContainerAction.Stop);
        Assert.Equal("exited", (await _runtime.InspectAsync("blog-web-1", false)).Summary.State);

        await _runtime.ExecuteAsync("blog-web-1", ContainerAction.Start);
        Assert.Equal("running", (await _runtime.InspectAsync("blog-web-1", false)).Summary.State);
    }

    [Fact]
    public async Task Tail_is_clamped()
    {
        var count = 0;
        await foreach (var _ in _runtime.LogsAsync("blog-web-1", 1_000_000, follow: false))
        {
            count++;
        }

        Assert.InRange(count, 1, GuardedContainerRuntime.MaxTail);
    }
}
