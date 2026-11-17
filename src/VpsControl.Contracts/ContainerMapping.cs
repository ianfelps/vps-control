using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Domain = VpsControl.Core.Containers;
using Proto = VpsControl.Contracts.Agent;

namespace VpsControl.Contracts;

/// <summary>Conversão domínio ↔ gRPC para containers, incluindo mapeamento de erros.</summary>
public static class ContainerMapping
{
    // ---------- domínio → proto (agente) ----------

    public static Proto.ContainerSummary ToProto(this Domain.ContainerSummary c)
    {
        ArgumentNullException.ThrowIfNull(c);
        var p = new Proto.ContainerSummary
        {
            Id = c.Id,
            Name = c.Name,
            Image = c.Image,
            State = c.State,
            Status = c.Status,
            Health = (Proto.ContainerHealth)(int)c.Health,
            CreatedAt = Timestamp.FromDateTimeOffset(c.CreatedAt),
            Protected = c.Protected,
        };
        if (c.ComposeProject is not null)
        {
            p.ComposeProject = c.ComposeProject;
        }

        if (c.ComposeService is not null)
        {
            p.ComposeService = c.ComposeService;
        }

        p.Ports.AddRange(c.Ports.Select(ToProto));
        return p;
    }

    public static Proto.ContainerDetails ToProto(this Domain.ContainerDetails d)
    {
        ArgumentNullException.ThrowIfNull(d);
        var p = new Proto.ContainerDetails
        {
            Summary = d.Summary.ToProto(),
            ExitCode = d.ExitCode,
            RestartCount = d.RestartCount,
            RestartPolicy = d.RestartPolicy,
            Command = d.Command,
        };
        if (d.StartedAt is { } started)
        {
            p.StartedAt = Timestamp.FromDateTimeOffset(started);
        }

        if (d.FinishedAt is { } finished)
        {
            p.FinishedAt = Timestamp.FromDateTimeOffset(finished);
        }

        p.Env.AddRange(d.Env.Select(e => new Proto.EnvVar { Name = e.Name, Value = e.Value, Masked = e.Masked }));
        foreach (var (key, value) in d.Labels)
        {
            p.Labels[key] = value;
        }
        p.Mounts.AddRange(d.Mounts.Select(m => new Proto.MountInfo
        {
            Type = m.Type,
            Source = m.Source,
            Destination = m.Destination,
            Mode = m.Mode,
            ReadWrite = m.ReadWrite,
        }));
        p.Networks.AddRange(d.Networks.Select(n =>
        {
            var net = new Proto.NetworkAttachment { Name = n.Name };
            if (n.IpAddress is not null)
            {
                net.IpAddress = n.IpAddress;
            }

            return net;
        }));
        return p;
    }

    public static Proto.ContainerStats ToProto(this Domain.ContainerStats s)
    {
        ArgumentNullException.ThrowIfNull(s);
        return new Proto.ContainerStats
        {
            Id = s.Id,
            CpuPercent = s.CpuPercent,
            MemoryUsageBytes = s.MemoryUsageBytes,
            MemoryLimitBytes = s.MemoryLimitBytes,
            NetworkRxBytes = s.NetworkRxBytes,
            NetworkTxBytes = s.NetworkTxBytes,
            BlockReadBytes = s.BlockReadBytes,
            BlockWriteBytes = s.BlockWriteBytes,
            Pids = s.Pids,
        };
    }

    public static Proto.LogLine ToProto(this Domain.LogLine l)
    {
        ArgumentNullException.ThrowIfNull(l);
        var p = new Proto.LogLine { Stream = l.Stream, Text = l.Text };
        if (l.Timestamp is { } ts)
        {
            p.Timestamp = Timestamp.FromDateTimeOffset(ts);
        }

        return p;
    }

    public static Proto.ContainerAction ToProto(this Domain.ContainerAction a) => a switch
    {
        Domain.ContainerAction.Start => Proto.ContainerAction.Start,
        Domain.ContainerAction.Stop => Proto.ContainerAction.Stop,
        Domain.ContainerAction.Restart => Proto.ContainerAction.Restart,
        _ => throw new ArgumentOutOfRangeException(nameof(a)),
    };

    // ---------- proto → domínio (API) ----------

    public static Domain.ContainerSummary ToDomain(this Proto.ContainerSummary p)
    {
        ArgumentNullException.ThrowIfNull(p);
        return new Domain.ContainerSummary(
            p.Id,
            p.Name,
            p.Image,
            p.State,
            p.Status,
            (Domain.ContainerHealth)(int)p.Health,
            p.CreatedAt.ToDateTimeOffset(),
            p.HasComposeProject ? p.ComposeProject : null,
            p.HasComposeService ? p.ComposeService : null,
            p.Protected,
            p.Ports.Select(x => new Domain.PortMapping(
                x.PrivatePort,
                x.HasPublicPort ? x.PublicPort : null,
                x.Type,
                x.HasHostIp ? x.HostIp : null)).ToList());
    }

    public static Domain.ContainerDetails ToDomain(this Proto.ContainerDetails p)
    {
        ArgumentNullException.ThrowIfNull(p);
        return new Domain.ContainerDetails(
            p.Summary.ToDomain(),
            p.StartedAt?.ToDateTimeOffset(),
            p.FinishedAt?.ToDateTimeOffset(),
            p.ExitCode,
            p.RestartCount,
            p.RestartPolicy,
            p.Command,
            p.Env.Select(e => new Domain.EnvVar(e.Name, e.Value, e.Masked)).ToList(),
            p.Labels.ToDictionary(StringComparer.Ordinal),
            p.Mounts.Select(m => new Domain.MountInfo(m.Type, m.Source, m.Destination, m.Mode, m.ReadWrite)).ToList(),
            p.Networks.Select(n => new Domain.NetworkAttachment(n.Name, n.HasIpAddress ? n.IpAddress : null)).ToList());
    }

    public static Domain.ContainerStats ToDomain(this Proto.ContainerStats p)
    {
        ArgumentNullException.ThrowIfNull(p);
        return new Domain.ContainerStats(
            p.Id, p.CpuPercent, p.MemoryUsageBytes, p.MemoryLimitBytes,
            p.NetworkRxBytes, p.NetworkTxBytes, p.BlockReadBytes, p.BlockWriteBytes, p.Pids);
    }

    public static Domain.LogLine ToDomain(this Proto.LogLine p)
    {
        ArgumentNullException.ThrowIfNull(p);
        return new Domain.LogLine(p.Timestamp?.ToDateTimeOffset(), p.Stream, p.Text);
    }

    public static Domain.ContainerAction ToDomain(this Proto.ContainerAction a) => a switch
    {
        Proto.ContainerAction.Start => Domain.ContainerAction.Start,
        Proto.ContainerAction.Stop => Domain.ContainerAction.Stop,
        Proto.ContainerAction.Restart => Domain.ContainerAction.Restart,
        _ => throw new RpcException(new Status(StatusCode.InvalidArgument, "ação inválida")),
    };

    // ---------- erros ----------

    /// <summary>Exceção de domínio → status gRPC (lado agente).</summary>
    public static RpcException ToRpcException(Exception e) => e switch
    {
        Domain.ContainerNotFoundException nf => new RpcException(new Status(StatusCode.NotFound, nf.Message)),
        Domain.InvalidContainerIdException inv => new RpcException(new Status(StatusCode.InvalidArgument, inv.Message)),
        Domain.ContainerProtectedException pr => new RpcException(new Status(StatusCode.FailedPrecondition, pr.Message)),
        ArgumentOutOfRangeException ar => new RpcException(new Status(StatusCode.InvalidArgument, ar.Message)),
        _ => new RpcException(new Status(StatusCode.Internal, "Falha ao falar com o Docker.")),
    };

    /// <summary>Status gRPC → exceção de domínio (lado API).</summary>
    public static Exception FromRpcException(RpcException e, string id)
    {
        ArgumentNullException.ThrowIfNull(e);
        return e.StatusCode switch
        {
            StatusCode.NotFound => new Domain.ContainerNotFoundException(id),
            StatusCode.InvalidArgument => new Domain.InvalidContainerIdException(id),
            StatusCode.FailedPrecondition => new Domain.ContainerProtectedException(id),
            _ => e,
        };
    }

    private static Proto.PortMapping ToProto(Domain.PortMapping m)
    {
        var p = new Proto.PortMapping { PrivatePort = m.PrivatePort, Type = m.Type };
        if (m.PublicPort is { } pub)
        {
            p.PublicPort = pub;
        }

        if (m.HostIp is not null)
        {
            p.HostIp = m.HostIp;
        }

        return p;
    }
}
