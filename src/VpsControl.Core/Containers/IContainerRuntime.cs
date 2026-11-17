namespace VpsControl.Core.Containers;

/// <summary>
/// Operações permitidas sobre containers. Conjunto fechado: não há execução arbitrária.
/// Implementações: Docker Engine (agente), fake (dev/teste), cliente gRPC (API).
/// </summary>
public interface IContainerRuntime
{
    Task<IReadOnlyList<ContainerSummary>> ListAsync(CancellationToken cancellationToken = default);

    /// <param name="revealEnv">false = valores de variáveis sensíveis mascarados.</param>
    Task<ContainerDetails> InspectAsync(string id, bool revealEnv, CancellationToken cancellationToken = default);

    /// <summary>Estatísticas dos containers em execução.</summary>
    Task<IReadOnlyList<ContainerStats>> StatsAsync(CancellationToken cancellationToken = default);

    IAsyncEnumerable<LogLine> LogsAsync(string id, int tail, bool follow, CancellationToken cancellationToken = default);

    Task ExecuteAsync(string id, ContainerAction action, CancellationToken cancellationToken = default);
}
