using System.Reflection;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using VpsControl.Contracts;
using VpsControl.Contracts.Agent;
using VpsControl.Core.Metrics;

namespace VpsControl.Agent.Services;

public sealed class AgentGrpcService(IHostMetricsProvider metrics, TimeProvider time) : AgentService.AgentServiceBase
{
    private static readonly string Version =
        typeof(AgentGrpcService).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? "0.0.0";

    public override Task<PingReply> Ping(PingRequest request, ServerCallContext context) =>
        Task.FromResult(new PingReply
        {
            AgentVersion = Version,
            ServerTime = Timestamp.FromDateTimeOffset(time.GetUtcNow()),
        });

    public override async Task<HostSnapshotReply> GetHostSnapshot(HostSnapshotRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var snapshot = await metrics.GetSnapshotAsync(context.CancellationToken).ConfigureAwait(false);
        return snapshot.ToProto();
    }
}
