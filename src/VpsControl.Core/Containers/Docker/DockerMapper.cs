using System.Globalization;

namespace VpsControl.Core.Containers.Docker;

/// <summary>Conversão pura das respostas do Docker para o domínio. Testável sem Docker.</summary>
internal static class DockerMapper
{
    public static ContainerSummary ToSummary(DockerContainerListItem c)
    {
        var labels = c.Labels ?? [];
        return new ContainerSummary(
            c.Id,
            TrimName(c.Names?.FirstOrDefault() ?? c.Id),
            c.Image,
            c.State,
            c.Status,
            ParseHealthFromStatus(c.Status),
            DateTimeOffset.FromUnixTimeSeconds(c.Created),
            labels.GetValueOrDefault(ContainerLabels.ComposeProject),
            labels.GetValueOrDefault(ContainerLabels.ComposeService),
            IsProtected(labels),
            (c.Ports ?? [])
                .Select(p => new PortMapping(p.PrivatePort, p.PublicPort, p.Type, p.IP))
                .DistinctBy(p => (p.PrivatePort, p.PublicPort, p.Type))
                .OrderBy(p => p.PrivatePort)
                .ToList());
    }

    public static ContainerDetails ToDetails(DockerInspect i, bool revealEnv)
    {
        var labels = i.Config?.Labels ?? [];
        var state = i.State ?? new DockerState();
        var startedAt = ParseDockerTime(state.StartedAt);
        var finishedAt = ParseDockerTime(state.FinishedAt);
        var status = state.Running
            ? $"Up desde {startedAt:yyyy-MM-dd HH:mm} UTC"
            : $"{Capitalize(state.Status)} (código {state.ExitCode})";

        var summary = new ContainerSummary(
            i.Id,
            TrimName(i.Name),
            i.Config?.Image ?? string.Empty,
            state.Status,
            status,
            ParseHealth(state.Health?.Status),
            ParseDockerTime(i.Created) ?? DateTimeOffset.UnixEpoch,
            labels.GetValueOrDefault(ContainerLabels.ComposeProject),
            labels.GetValueOrDefault(ContainerLabels.ComposeService),
            IsProtected(labels),
            PortsFromBindings(i.HostConfig?.PortBindings));

        var command = string.Join(' ', (i.Config?.Entrypoint ?? []).Concat(i.Config?.Cmd ?? []));

        return new ContainerDetails(
            summary,
            startedAt,
            finishedAt,
            state.ExitCode,
            i.RestartCount,
            string.IsNullOrEmpty(i.HostConfig?.RestartPolicy?.Name) ? "no" : i.HostConfig!.RestartPolicy!.Name,
            command,
            EnvMasker.Parse(i.Config?.Env, revealEnv),
            labels,
            (i.Mounts ?? [])
                .Select(m => new MountInfo(m.Type, m.Source ?? string.Empty, m.Destination, m.Mode ?? string.Empty, m.RW))
                .ToList(),
            (i.NetworkSettings?.Networks ?? [])
                .Select(n => new NetworkAttachment(n.Key, string.IsNullOrEmpty(n.Value.IPAddress) ? null : n.Value.IPAddress))
                .OrderBy(n => n.Name, StringComparer.Ordinal)
                .ToList());
    }

    /// <summary>Mesma fórmula do <c>docker stats</c>.</summary>
    public static ContainerStats ToStats(string id, DockerStats s)
    {
        double cpu = 0;
        var cur = s.CpuStats;
        var pre = s.PreCpuStats;
        if (cur?.CpuUsage is not null && pre?.CpuUsage is not null
            && cur.CpuUsage.TotalUsage >= pre.CpuUsage.TotalUsage
            && cur.SystemCpuUsage > pre.SystemCpuUsage)
        {
            var cpuDelta = (double)(cur.CpuUsage.TotalUsage - pre.CpuUsage.TotalUsage);
            var systemDelta = (double)(cur.SystemCpuUsage - pre.SystemCpuUsage);
            var cpus = cur.OnlineCpus > 0 ? cur.OnlineCpus : cur.CpuUsage.PerCpuUsage?.Count ?? 1;
            cpu = Math.Round(cpuDelta / systemDelta * cpus * 100.0, 1);
        }

        var mem = s.MemoryStats;
        long usage = 0;
        if (mem is not null)
        {
            // Desconta page cache, como o docker CLI (cgroup v2: inactive_file; v1: total_inactive_file).
            var stats = mem.Stats ?? [];
            var cache = stats.GetValueOrDefault("inactive_file", stats.GetValueOrDefault("total_inactive_file"));
            usage = Math.Max(0, mem.Usage - (cache < mem.Usage ? cache : 0));
        }

        var blk = s.BlkioStats?.IoServiceBytesRecursive ?? [];
        return new ContainerStats(
            id,
            cpu,
            usage,
            mem?.Limit ?? 0,
            s.Networks?.Values.Sum(n => n.RxBytes) ?? 0,
            s.Networks?.Values.Sum(n => n.TxBytes) ?? 0,
            blk.Where(b => b.Op.Equals("read", StringComparison.OrdinalIgnoreCase)).Sum(b => b.Value),
            blk.Where(b => b.Op.Equals("write", StringComparison.OrdinalIgnoreCase)).Sum(b => b.Value),
            s.PidsStats?.Current ?? 0);
    }

    /// <summary>"2026-11-17T21:33:15.123456789Z texto" → (timestamp, texto).</summary>
    public static LogLine ParseLogLine(string raw, string stream)
    {
        var line = raw.TrimEnd('\r');
        var space = line.IndexOf(' ', StringComparison.Ordinal);
        if (space > 0 && ParseDockerTime(line[..space]) is { } ts)
        {
            return new LogLine(ts, stream, line[(space + 1)..]);
        }

        return new LogLine(null, stream, line);
    }

    public static ContainerHealth ParseHealth(string? status) => status switch
    {
        "healthy" => ContainerHealth.Healthy,
        "unhealthy" => ContainerHealth.Unhealthy,
        "starting" => ContainerHealth.Starting,
        _ => ContainerHealth.None,
    };

    public static ContainerHealth ParseHealthFromStatus(string status)
    {
        if (status.Contains("(unhealthy)", StringComparison.Ordinal))
        {
            return ContainerHealth.Unhealthy;
        }

        if (status.Contains("(healthy)", StringComparison.Ordinal))
        {
            return ContainerHealth.Healthy;
        }

        return status.Contains("health: starting", StringComparison.Ordinal) ? ContainerHealth.Starting : ContainerHealth.None;
    }

    public static DateTimeOffset? ParseDockerTime(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.StartsWith("0001-01-01", StringComparison.Ordinal))
        {
            return null;
        }

        // Docker usa nanossegundos (9 dígitos); .NET aceita até 7.
        var dot = value.IndexOf('.', StringComparison.Ordinal);
        var z = value.IndexOfAny(['Z', '+', '-'], dot < 0 ? 19 : dot);
        if (dot > 0 && z > dot && z - dot - 1 > 7)
        {
            value = string.Concat(value.AsSpan(0, dot + 8), value.AsSpan(z));
        }

        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var result)
            ? result.ToUniversalTime()
            : null;
    }

    private static List<PortMapping> PortsFromBindings(Dictionary<string, List<DockerPortBinding>?>? bindings)
    {
        var result = new List<PortMapping>();
        foreach (var (key, hosts) in bindings ?? [])
        {
            var parts = key.Split('/');
            if (!int.TryParse(parts[0], CultureInfo.InvariantCulture, out var privatePort))
            {
                continue;
            }

            var type = parts.Length > 1 ? parts[1] : "tcp";
            if (hosts is null || hosts.Count == 0)
            {
                result.Add(new PortMapping(privatePort, null, type, null));
                continue;
            }

            foreach (var h in hosts)
            {
                int? publicPort = int.TryParse(h.HostPort, CultureInfo.InvariantCulture, out var p) ? p : null;
                result.Add(new PortMapping(privatePort, publicPort, type, string.IsNullOrEmpty(h.HostIp) ? null : h.HostIp));
            }
        }

        return result.DistinctBy(p => (p.PrivatePort, p.PublicPort, p.Type)).OrderBy(p => p.PrivatePort).ToList();
    }

    private static bool IsProtected(Dictionary<string, string> labels) =>
        labels.TryGetValue(ContainerLabels.Protected, out var v) && v.Equals("true", StringComparison.OrdinalIgnoreCase);

    private static string TrimName(string name) => name.TrimStart('/');

    private static string Capitalize(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];
}
