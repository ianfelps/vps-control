using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace VpsControl.Core.Containers.Docker;

/// <summary>
/// Cliente mínimo da Docker Engine API sobre Unix socket. Sem dependências e compatível com
/// Native AOT (System.Text.Json com source generator). Usado apenas pelo agente.
/// </summary>
public sealed class DockerEngineContainerRuntime : IContainerRuntime, IDisposable
{
    private const int StatsParallelism = 8;
    private readonly HttpClient _http;

    public DockerEngineContainerRuntime(string socketPath = "/var/run/docker.sock")
    {
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
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        };
        _http = new HttpClient(handler) { BaseAddress = new Uri("http://docker"), Timeout = Timeout.InfiniteTimeSpan };
    }

    public async Task<IReadOnlyList<ContainerSummary>> ListAsync(CancellationToken cancellationToken = default)
    {
        var json = await GetStringAsync("/containers/json?all=true", null, cancellationToken).ConfigureAwait(false);
        return DockerJson.Deserialize(json, DockerJsonContext.Default.ListDockerContainerListItem)
            .Select(DockerMapper.ToSummary)
            .OrderBy(c => c.ComposeProject ?? "~")
            .ThenBy(c => c.Name, StringComparer.Ordinal)
            .ToList();
    }

    public async Task<ContainerDetails> InspectAsync(string id, bool revealEnv, CancellationToken cancellationToken = default) =>
        DockerMapper.ToDetails(await InspectRawAsync(id, cancellationToken).ConfigureAwait(false), revealEnv);

    public async Task<IReadOnlyList<ContainerStats>> StatsAsync(CancellationToken cancellationToken = default)
    {
        var running = (await ListAsync(cancellationToken).ConfigureAwait(false)).Where(c => c.Running).ToList();
        var results = new List<ContainerStats>(running.Count);
        using var gate = new SemaphoreSlim(StatsParallelism);

        var tasks = running.Select(async c =>
        {
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                // stream=false: o Docker coleta duas amostras (~1 s) e preenche precpu_stats.
                var json = await GetStringAsync($"/containers/{Escape(c.Id)}/stats?stream=false", c.Id, cancellationToken)
                    .ConfigureAwait(false);
                return DockerMapper.ToStats(c.Id, DockerJson.Deserialize(json, DockerJsonContext.Default.DockerStats));
            }
            catch (ContainerNotFoundException)
            {
                return null; // removido entre a listagem e a coleta
            }
            finally
            {
                gate.Release();
            }
        });

        foreach (var stat in await Task.WhenAll(tasks).ConfigureAwait(false))
        {
            if (stat is not null)
            {
                results.Add(stat);
            }
        }

        return results;
    }

    public async IAsyncEnumerable<LogLine> LogsAsync(
        string id,
        int tail,
        bool follow,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var inspect = await InspectRawAsync(id, cancellationToken).ConfigureAwait(false);
        var tty = inspect.Config?.Tty ?? false;
        var url = string.Create(
            CultureInfo.InvariantCulture,
            $"/containers/{Escape(id)}/logs?stdout=true&stderr=true&timestamps=true&tail={tail}&follow={(follow ? "true" : "false")}");

        using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        await EnsureSuccessAsync(response, id, cancellationToken).ConfigureAwait(false);
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);

        await foreach (var line in DockerLogReader.ReadAsync(stream, tty, cancellationToken).ConfigureAwait(false))
        {
            yield return line;
        }
    }

    public async Task ExecuteAsync(string id, ContainerAction action, CancellationToken cancellationToken = default)
    {
        var path = action switch
        {
            ContainerAction.Start => $"/containers/{Escape(id)}/start",
            ContainerAction.Stop => $"/containers/{Escape(id)}/stop?t=10",
            ContainerAction.Restart => $"/containers/{Escape(id)}/restart?t=10",
            _ => throw new ArgumentOutOfRangeException(nameof(action)),
        };

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(60));
        using var response = await _http.PostAsync(path, content: null, timeout.Token).ConfigureAwait(false);

        // 304 = já estava no estado pedido (start/stop): não é erro.
        if (response.StatusCode != HttpStatusCode.NotModified)
        {
            await EnsureSuccessAsync(response, id, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<DockerInspect> InspectRawAsync(string id, CancellationToken ct)
    {
        var json = await GetStringAsync($"/containers/{Escape(id)}/json", id, ct).ConfigureAwait(false);
        return DockerJson.Deserialize(json, DockerJsonContext.Default.DockerInspect);
    }

    private async Task<string> GetStringAsync(string path, string? id, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        using var response = await _http.GetAsync(path, timeout.Token).ConfigureAwait(false);
        await EnsureSuccessAsync(response, id, ct).ConfigureAwait(false);
        return await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, string? id, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        if (response.StatusCode == HttpStatusCode.NotFound && id is not null)
        {
            throw new ContainerNotFoundException(id);
        }

        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        string? message = null;
        try
        {
            message = JsonSerializer.Deserialize(body, DockerJsonContext.Default.DockerError)?.Message;
        }
        catch (JsonException)
        {
            // corpo não-JSON
        }

        throw new HttpRequestException($"Docker respondeu {(int)response.StatusCode}: {message ?? body}", null, response.StatusCode);
    }

    private static string Escape(string id) => Uri.EscapeDataString(id);

    public void Dispose() => _http.Dispose();
}
