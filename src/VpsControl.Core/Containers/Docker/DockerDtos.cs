using System.Text.Json;
using System.Text.Json.Serialization;

namespace VpsControl.Core.Containers.Docker;

// Subconjunto da Docker Engine API usado pelo painel. Nomes seguem o JSON do Docker.

internal sealed class DockerContainerListItem
{
    public string Id { get; set; } = string.Empty;
    public List<string>? Names { get; set; }
    public string Image { get; set; } = string.Empty;
    public long Created { get; set; }
    public string State { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public Dictionary<string, string>? Labels { get; set; }
    public List<DockerPort>? Ports { get; set; }
}

internal sealed class DockerPort
{
    public string? IP { get; set; }
    public int PrivatePort { get; set; }
    public int? PublicPort { get; set; }
    public string Type { get; set; } = "tcp";
}

internal sealed class DockerInspect
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Created { get; set; } = string.Empty;
    public int RestartCount { get; set; }
    public DockerState? State { get; set; }
    public DockerConfig? Config { get; set; }
    public DockerHostConfig? HostConfig { get; set; }
    public List<DockerMount>? Mounts { get; set; }
    public DockerNetworkSettings? NetworkSettings { get; set; }
}

internal sealed class DockerState
{
    public string Status { get; set; } = string.Empty;
    public bool Running { get; set; }
    public int ExitCode { get; set; }
    public string? StartedAt { get; set; }
    public string? FinishedAt { get; set; }
    public DockerHealth? Health { get; set; }
}

internal sealed class DockerHealth
{
    public string Status { get; set; } = string.Empty;
}

internal sealed class DockerConfig
{
    public string Image { get; set; } = string.Empty;
    public List<string>? Env { get; set; }
    public List<string>? Cmd { get; set; }
    public List<string>? Entrypoint { get; set; }
    public Dictionary<string, string>? Labels { get; set; }
    public bool Tty { get; set; }
}

internal sealed class DockerHostConfig
{
    public DockerRestartPolicy? RestartPolicy { get; set; }
    public Dictionary<string, List<DockerPortBinding>?>? PortBindings { get; set; }
}

internal sealed class DockerPortBinding
{
    public string? HostIp { get; set; }
    public string? HostPort { get; set; }
}

internal sealed class DockerRestartPolicy
{
    public string Name { get; set; } = string.Empty;
}

internal sealed class DockerMount
{
    public string Type { get; set; } = string.Empty;
    public string? Source { get; set; }
    public string Destination { get; set; } = string.Empty;
    public string? Mode { get; set; }
    public bool RW { get; set; }
}

internal sealed class DockerNetworkSettings
{
    public Dictionary<string, DockerEndpoint>? Networks { get; set; }
}

internal sealed class DockerEndpoint
{
    public string? IPAddress { get; set; }
}

internal sealed class DockerStats
{
    [JsonPropertyName("cpu_stats")] public DockerCpuStats? CpuStats { get; set; }
    [JsonPropertyName("precpu_stats")] public DockerCpuStats? PreCpuStats { get; set; }
    [JsonPropertyName("memory_stats")] public DockerMemoryStats? MemoryStats { get; set; }
    [JsonPropertyName("networks")] public Dictionary<string, DockerNetStats>? Networks { get; set; }
    [JsonPropertyName("blkio_stats")] public DockerBlkioStats? BlkioStats { get; set; }
    [JsonPropertyName("pids_stats")] public DockerPidsStats? PidsStats { get; set; }
}

internal sealed class DockerCpuStats
{
    [JsonPropertyName("cpu_usage")] public DockerCpuUsage? CpuUsage { get; set; }
    [JsonPropertyName("system_cpu_usage")] public ulong SystemCpuUsage { get; set; }
    [JsonPropertyName("online_cpus")] public int OnlineCpus { get; set; }
}

internal sealed class DockerCpuUsage
{
    [JsonPropertyName("total_usage")] public ulong TotalUsage { get; set; }
    [JsonPropertyName("percpu_usage")] public List<ulong>? PerCpuUsage { get; set; }
}

internal sealed class DockerMemoryStats
{
    [JsonPropertyName("usage")] public long Usage { get; set; }
    [JsonPropertyName("limit")] public long Limit { get; set; }
    [JsonPropertyName("stats")] public Dictionary<string, long>? Stats { get; set; }
}

internal sealed class DockerNetStats
{
    [JsonPropertyName("rx_bytes")] public long RxBytes { get; set; }
    [JsonPropertyName("tx_bytes")] public long TxBytes { get; set; }
}

internal sealed class DockerBlkioStats
{
    [JsonPropertyName("io_service_bytes_recursive")] public List<DockerBlkioEntry>? IoServiceBytesRecursive { get; set; }
}

internal sealed class DockerBlkioEntry
{
    [JsonPropertyName("op")] public string Op { get; set; } = string.Empty;
    [JsonPropertyName("value")] public long Value { get; set; }
}

internal sealed class DockerPidsStats
{
    [JsonPropertyName("current")] public int Current { get; set; }
}

internal sealed class DockerError
{
    public string? Message { get; set; }
}

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true, NumberHandling = JsonNumberHandling.AllowReadingFromString)]
[JsonSerializable(typeof(List<DockerContainerListItem>))]
[JsonSerializable(typeof(DockerInspect))]
[JsonSerializable(typeof(DockerStats))]
[JsonSerializable(typeof(DockerError))]
internal sealed partial class DockerJsonContext : JsonSerializerContext;

internal static class DockerJson
{
    public static T Deserialize<T>(string json, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> info) =>
        JsonSerializer.Deserialize(json, info) ?? throw new JsonException("Resposta vazia do Docker.");
}
