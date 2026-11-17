using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using VpsControl.Api.Containers;
using VpsControl.Core.Containers;

namespace VpsControl.Api.Metrics;

/// <summary>
/// Canal de tempo real. Servidor → cliente: "snapshot", "status", "containers".
/// Cliente → servidor: stream "StreamLogs".
/// </summary>
[Authorize]
public sealed class MetricsHub(ContainersService containers) : Hub
{
    public const string Path = "/hubs/metrics";

    /// <summary>Logs em tempo real (tail inicial + follow). Encerra quando o cliente cancela.</summary>
    public async IAsyncEnumerable<LogLine> StreamLogs(
        string id,
        int tail,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (!GuardedContainerRuntime.IsValidId(id))
        {
            throw new HubException("Identificador de container inválido.");
        }

        var lines = containers.Runtime.LogsAsync(id, Math.Clamp(tail, 0, GuardedContainerRuntime.MaxTail), follow: true, cancellationToken);
        await using var enumerator = lines.GetAsyncEnumerator(cancellationToken);
        while (true)
        {
            LogLine current;
            try
            {
                if (!await enumerator.MoveNextAsync().ConfigureAwait(false))
                {
                    yield break;
                }

                current = enumerator.Current;
            }
            catch (ContainerNotFoundException)
            {
                throw new HubException("Container não encontrado.");
            }
            catch (OperationCanceledException)
            {
                yield break;
            }

            yield return current;
        }
    }
}
