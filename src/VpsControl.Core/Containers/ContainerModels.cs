namespace VpsControl.Core.Containers;

public enum ContainerAction
{
    Start,
    Stop,
    Restart,
}

/// <summary>Estado de saúde derivado do healthcheck do Docker.</summary>
public enum ContainerHealth
{
    None,
    Starting,
    Healthy,
    Unhealthy,
}

public sealed record PortMapping(int PrivatePort, int? PublicPort, string Type, string? HostIp);

/// <summary>Resumo de um container, como na listagem.</summary>
public sealed record ContainerSummary(
    string Id,
    string Name,
    string Image,
    string State,
    string Status,
    ContainerHealth Health,
    DateTimeOffset CreatedAt,
    string? ComposeProject,
    string? ComposeService,
    bool Protected,
    IReadOnlyList<PortMapping> Ports)
{
    public string ShortId => Id.Length > 12 ? Id[..12] : Id;

    public bool Running => State == "running";
}

public sealed record EnvVar(string Name, string Value, bool Masked);

public sealed record MountInfo(string Type, string Source, string Destination, string Mode, bool ReadWrite);

public sealed record NetworkAttachment(string Name, string? IpAddress);

public sealed record ContainerDetails(
    ContainerSummary Summary,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt,
    int ExitCode,
    int RestartCount,
    string RestartPolicy,
    string Command,
    IReadOnlyList<EnvVar> Env,
    IReadOnlyDictionary<string, string> Labels,
    IReadOnlyList<MountInfo> Mounts,
    IReadOnlyList<NetworkAttachment> Networks);

public sealed record ContainerStats(
    string Id,
    double CpuPercent,
    long MemoryUsageBytes,
    long MemoryLimitBytes,
    long NetworkRxBytes,
    long NetworkTxBytes,
    long BlockReadBytes,
    long BlockWriteBytes,
    int Pids)
{
    public double MemoryPercent => MemoryLimitBytes > 0 ? Math.Round(100.0 * MemoryUsageBytes / MemoryLimitBytes, 1) : 0;
}

public sealed record LogLine(DateTimeOffset? Timestamp, string Stream, string Text);

public sealed class ContainerNotFoundException(string id) : Exception($"Container '{id}' não encontrado.")
{
    public string ContainerId { get; } = id;
}

public sealed class ContainerProtectedException(string id)
    : Exception($"Container '{id}' é protegido (label {ContainerLabels.Protected}=true) e não pode ser parado ou reiniciado pelo painel.")
{
    public string ContainerId { get; } = id;
}

public sealed class InvalidContainerIdException(string id) : Exception("Identificador de container inválido.")
{
    public string ContainerId { get; } = id;
}

public static class ContainerLabels
{
    public const string Protected = "vpsctl.protected";
    public const string ComposeProject = "com.docker.compose.project";
    public const string ComposeService = "com.docker.compose.service";
}
