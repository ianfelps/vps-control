using System.Reflection;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using VpsControl.Contracts;
using VpsControl.Contracts.Agent;
using VpsControl.Core.Metrics;
using IContainerRuntime = VpsControl.Core.Containers.IContainerRuntime;

namespace VpsControl.Agent.Services;

public sealed class AgentGrpcService(
    IHostMetricsProvider metrics,
    IContainerRuntime containers,
    TimeProvider time,
    ILogger<AgentGrpcService> logger) : AgentService.AgentServiceBase
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

    public override Task<ListContainersReply> ListContainers(ListContainersRequest request, ServerCallContext context) =>
        Guard(async () =>
        {
            var reply = new ListContainersReply();
            reply.Containers.AddRange((await containers.ListAsync(context.CancellationToken).ConfigureAwait(false))
                .Select(c => c.ToProto()));
            return reply;
        });

    public override Task<ContainerDetails> InspectContainer(InspectContainerRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Guard(async () =>
        {
            if (request.RevealEnv)
            {
                logger.LogWarning("Variáveis de ambiente reveladas para o container {Id}", request.Id);
            }

            return (await containers.InspectAsync(request.Id, request.RevealEnv, context.CancellationToken).ConfigureAwait(false))
                .ToProto();
        });
    }

    public override Task<ContainerStatsReply> GetContainerStats(ContainerStatsRequest request, ServerCallContext context) =>
        Guard(async () =>
        {
            var reply = new ContainerStatsReply();
            reply.Stats.AddRange((await containers.StatsAsync(context.CancellationToken).ConfigureAwait(false))
                .Select(s => s.ToProto()));
            return reply;
        });

    public override async Task StreamContainerLogs(
        ContainerLogsRequest request,
        IServerStreamWriter<LogLine> responseStream,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(responseStream);
        ArgumentNullException.ThrowIfNull(context);
        try
        {
            await foreach (var line in containers.LogsAsync(request.Id, request.Tail, request.Follow, context.CancellationToken)
                               .ConfigureAwait(false))
            {
                await responseStream.WriteAsync(line.ToProto(), context.CancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
        {
            // cliente desconectou
        }
        catch (Exception e) when (e is not RpcException)
        {
            throw Translate(e);
        }
    }

    public override Task<ContainerActionReply> ExecuteContainerAction(ContainerActionRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Guard(async () =>
        {
            var action = request.Action.ToDomain();
            logger.LogInformation("Executando {Action} em {Id}", action, request.Id);
            await containers.ExecuteAsync(request.Id, action, context.CancellationToken).ConfigureAwait(false);
            return new ContainerActionReply();
        });
    }

    private async Task<T> Guard<T>(Func<Task<T>> body)
    {
        try
        {
            return await body().ConfigureAwait(false);
        }
        catch (Exception e) when (e is not RpcException and not OperationCanceledException)
        {
            throw Translate(e);
        }
    }

    private RpcException Translate(Exception e)
    {
        var rpc = ContainerMapping.ToRpcException(e);
        if (rpc.StatusCode == StatusCode.Internal)
        {
            logger.LogError(e, "Erro ao acessar o Docker");
        }

        return rpc;
    }
}
