using Google.Protobuf.WellKnownTypes;
using Domain = VpsControl.Core.Metrics;
using Proto = VpsControl.Contracts.Agent;

namespace VpsControl.Contracts;

/// <summary>Conversão entre os modelos de domínio (Core) e as mensagens gRPC.</summary>
public static class AgentMapping
{
    public const string TokenHeader = "x-agent-token";

    public static Proto.HostSnapshotReply ToProto(this Domain.HostSnapshot s)
    {
        ArgumentNullException.ThrowIfNull(s);
        var reply = new Proto.HostSnapshotReply
        {
            Timestamp = Timestamp.FromDateTimeOffset(s.Timestamp),
            Info = new Proto.HostInfo
            {
                Hostname = s.Info.Hostname,
                OsName = s.Info.OsName,
                KernelVersion = s.Info.KernelVersion,
                Uptime = Duration.FromTimeSpan(s.Info.Uptime),
                LogicalCores = s.Info.LogicalCores,
            },
            Cpu = new Proto.CpuUsage
            {
                UsagePercent = s.Cpu.UsagePercent,
                Load1 = s.Cpu.Load1,
                Load5 = s.Cpu.Load5,
                Load15 = s.Cpu.Load15,
            },
            Memory = new Proto.MemoryUsage
            {
                TotalBytes = s.Memory.TotalBytes,
                AvailableBytes = s.Memory.AvailableBytes,
                SwapTotalBytes = s.Memory.SwapTotalBytes,
                SwapFreeBytes = s.Memory.SwapFreeBytes,
            },
        };
        reply.Disks.AddRange(s.Disks.Select(d => new Proto.DiskUsage
        {
            MountPoint = d.MountPoint,
            FileSystem = d.FileSystem,
            TotalBytes = d.TotalBytes,
            FreeBytes = d.FreeBytes,
        }));
        reply.Networks.AddRange(s.Networks.Select(n => new Proto.NetworkUsage
        {
            Interface = n.Interface,
            RxBytesTotal = n.RxBytesTotal,
            TxBytesTotal = n.TxBytesTotal,
            RxBytesPerSecond = n.RxBytesPerSecond,
            TxBytesPerSecond = n.TxBytesPerSecond,
        }));
        return reply;
    }

    public static Domain.HostSnapshot ToDomain(this Proto.HostSnapshotReply r)
    {
        ArgumentNullException.ThrowIfNull(r);
        return new Domain.HostSnapshot(
            r.Timestamp.ToDateTimeOffset(),
            new Domain.HostInfo(
                r.Info.Hostname,
                r.Info.OsName,
                r.Info.KernelVersion,
                r.Info.Uptime.ToTimeSpan(),
                r.Info.LogicalCores),
            new Domain.CpuUsage(r.Cpu.UsagePercent, r.Cpu.Load1, r.Cpu.Load5, r.Cpu.Load15),
            new Domain.MemoryUsage(
                r.Memory.TotalBytes,
                r.Memory.AvailableBytes,
                r.Memory.SwapTotalBytes,
                r.Memory.SwapFreeBytes),
            r.Disks.Select(d => new Domain.DiskUsage(d.MountPoint, d.FileSystem, d.TotalBytes, d.FreeBytes)).ToList(),
            r.Networks.Select(n => new Domain.NetworkUsage(
                n.Interface,
                n.RxBytesTotal,
                n.TxBytesTotal,
                n.RxBytesPerSecond,
                n.TxBytesPerSecond)).ToList());
    }
}
