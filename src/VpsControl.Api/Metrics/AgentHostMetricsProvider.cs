using System.Net.Sockets;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.Extensions.Options;
using VpsControl.Contracts;
using VpsControl.Contracts.Agent;
using VpsControl.Core.Metrics;

namespace VpsControl.Api.Metrics;

/// <summary>Busca métricas no agente do host via gRPC (Unix socket em produção, TCP em dev).</summary>
public sealed class AgentHostMetricsProvider : IHostMetricsProvider, IDisposable
{
    private readonly GrpcChannel _channel;
    private readonly AgentService.AgentServiceClient _client;
    private readonly string? _tokenFile;
    private string? _token;

    public AgentHostMetricsProvider(IOptions<MetricsOptions> options)
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

    public async Task<HostSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        var reply = await _client.GetHostSnapshotAsync(
            new HostSnapshotRequest(),
            headers: Headers(),
            deadline: DateTime.UtcNow.AddSeconds(5),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        return reply.ToDomain();
    }

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
