using System.Net.Sockets;
using System.Runtime.CompilerServices;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.Extensions.Options;
using VpsControl.Contracts;
using VpsControl.Contracts.Agent;
using VpsControl.Core.Metrics;
using Domain = VpsControl.Core.Containers;

namespace VpsControl.Api.Metrics;

/// <summary>
/// Cliente do agente do host via gRPC (Unix socket em produção, TCP em dev).
/// Implementa métricas e containers sobre o mesmo canal.
/// </summary>
public sealed class AgentClient : IHostMetricsProvider, Domain.IContainerRuntime, IDisposable
{
    private static readonly TimeSpan DefaultDeadline = TimeSpan.FromSeconds(10);
    private readonly GrpcChannel _channel;
    private readonly AgentService.AgentServiceClient _client;
    private readonly string? _tokenFile;
    private string? _token;

    public AgentClient(IOptions<MetricsOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var o = options.Value;
        _tokenFile = o.AgentTokenFile;

        if (!string.IsNullOrWhiteSpace(o.AgentUrl))
        {
            _channel = GrpcChannel.ForAddress(o.AgentUrl);
        }
        else
        {
            var socketPath = o.AgentSocketPath
                ?? throw new InvalidOperationException("Metrics:AgentSocketPath ou Metrics:AgentUrl é obrigatório.");
            var handler = new SocketsHttpHandler
            {
                ConnectCallback = async (_, ct) =>
                {
                    var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                    try
                    {
                        await socket.ConnectAsync(new UnixDomainSocketEndPoint(socketPath), ct).ConfigureAwait(false);
                        return new NetworkStream(socket, ownsSocket: true);
                    }
                    catch
                    {
                        socket.Dispose();
                        throw;
                    }
                },
            };
            // O host da URL é ignorado; a conexão real vai para o socket.
            _channel = GrpcChannel.ForAddress("http://agent", new GrpcChannelOptions { HttpHandler = handler });
        }

        _client = new AgentService.AgentServiceClient(_channel);
    }

    // ---------- Métricas ----------

    public async Task<HostSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        var reply = await _client.GetHostSnapshotAsync(new HostSnapshotRequest(), Options(cancellationToken))
            .ConfigureAwait(false);
        return reply.ToDomain();
    }

    // ---------- Containers ----------

    public async Task<IReadOnlyList<Domain.ContainerSummary>> ListAsync(CancellationToken cancellationToken = default)
    {
        var reply = await _client.ListContainersAsync(new ListContainersRequest(), Options(cancellationToken))
            .ConfigureAwait(false);
        return reply.Containers.Select(c => c.ToDomain()).ToList();
    }

    public async Task<Domain.ContainerDetails> InspectAsync(string id, bool revealEnv, CancellationToken cancellationToken = default)
    {
        try
        {
            var reply = await _client.InspectContainerAsync(
                new InspectContainerRequest { Id = id, RevealEnv = revealEnv },
                Options(cancellationToken)).ConfigureAwait(false);
            return reply.ToDomain();
        }
        catch (RpcException e)
        {
            throw ContainerMapping.FromRpcException(e, id);
        }
    }

    public async Task<IReadOnlyList<Domain.ContainerStats>> StatsAsync(CancellationToken cancellationToken = default)
    {
        // Coleta de stats leva ~1–2 s no Docker (duas amostras por container).
        var reply = await _client.GetContainerStatsAsync(
            new ContainerStatsRequest(),
            Options(cancellationToken, TimeSpan.FromSeconds(20))).ConfigureAwait(false);
        return reply.Stats.Select(s => s.ToDomain()).ToList();
    }

    public async IAsyncEnumerable<Domain.LogLine> LogsAsync(
        string id,
        int tail,
        bool follow,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var callOptions = new CallOptions(
            headers: Headers(),
            deadline: follow ? null : DateTime.UtcNow.Add(DefaultDeadline),
            cancellationToken: cancellationToken);
        using var call = _client.StreamContainerLogs(new ContainerLogsRequest { Id = id, Tail = tail, Follow = follow }, callOptions);

        var stream = call.ResponseStream;
        while (true)
        {
            bool hasNext;
            try
            {
                hasNext = await stream.MoveNext(cancellationToken).ConfigureAwait(false);
            }
            catch (RpcException e) when (e.StatusCode == StatusCode.Cancelled && cancellationToken.IsCancellationRequested)
            {
                yield break;
            }
            catch (RpcException e)
            {
                throw ContainerMapping.FromRpcException(e, id);
            }

            if (!hasNext)
            {
                yield break;
            }

            yield return stream.Current.ToDomain();
        }
    }

    public async Task ExecuteAsync(string id, Domain.ContainerAction action, CancellationToken cancellationToken = default)
    {
        try
        {
            await _client.ExecuteContainerActionAsync(
                new ContainerActionRequest { Id = id, Action = action.ToProto() },
                Options(cancellationToken, TimeSpan.FromSeconds(70))).ConfigureAwait(false);
        }
        catch (RpcException e)
        {
            throw ContainerMapping.FromRpcException(e, id);
        }
    }

    private CallOptions Options(CancellationToken ct, TimeSpan? deadline = null) =>
        new(headers: Headers(), deadline: DateTime.UtcNow.Add(deadline ?? DefaultDeadline), cancellationToken: ct);

    private Metadata Headers()
    {
        var metadata = new Metadata();
        var token = _token ??= ReadToken();
        if (token is not null)
        {
            metadata.Add(AgentMapping.TokenHeader, token);
        }

        return metadata;
    }

    private string? ReadToken() =>
        !string.IsNullOrWhiteSpace(_tokenFile) && File.Exists(_tokenFile) ? File.ReadAllText(_tokenFile).Trim() : null;

    public void Dispose() => _channel.Dispose();
}
