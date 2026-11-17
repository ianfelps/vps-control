using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using VpsControl.Api.Metrics;
using VpsControl.Core.Containers;

namespace VpsControl.Api.Containers;

/// <summary>Container com as estatísticas mais recentes (null quando parado ou sem amostra).</summary>
public sealed record ContainerView(
    string Id,
    string ShortId,
    string Name,
    string Image,
    string State,
    string Status,
    ContainerHealth Health,
    DateTimeOffset CreatedAt,
    string? ComposeProject,
    string? ComposeService,
    bool Protected,
    IReadOnlyList<PortMapping> Ports,
    ContainerStats? Stats)
{
    public static ContainerView From(ContainerSummary c, ContainerStats? stats) => new(
        c.Id, c.ShortId, c.Name, c.Image, c.State, c.Status, c.Health, c.CreatedAt,
        c.ComposeProject, c.ComposeService, c.Protected, c.Ports, c.Running ? stats : null);
}

public sealed record ContainersSnapshot(
    IReadOnlyList<ContainerView> Items,
    DateTimeOffset? UpdatedAt,
    string? Error);

/// <summary>
/// Cache da lista de containers + stats, atualizado pelo <see cref="ContainerSampler"/> e após ações.
/// Publica cada atualização no hub ("containers").
/// </summary>
public sealed class ContainersService(
    IContainerRuntime runtime,
    IHubContext<MetricsHub> hub,
    TimeProvider time,
    ILogger<ContainersService> logger) : IDisposable
{
    private readonly Lock _gate = new();
    private readonly SemaphoreSlim _refreshing = new(1, 1);
    private IReadOnlyList<ContainerSummary> _containers = [];
    private Dictionary<string, ContainerStats> _stats = new(StringComparer.Ordinal);
    private DateTimeOffset? _updatedAt;
    private string? _error;

    public ContainersSnapshot Snapshot
    {
        get
        {
            lock (_gate)
            {
                return new ContainersSnapshot(
                    _containers.Select(c => ContainerView.From(c, _stats.GetValueOrDefault(c.Id))).ToList(),
                    _updatedAt,
                    _error);
            }
        }
    }

    public IContainerRuntime Runtime => runtime;

    /// <param name="includeStats">Stats são mais caros (~1–2 s); após ações basta a lista.</param>
    public async Task RefreshAsync(bool includeStats, CancellationToken cancellationToken)
    {
        await _refreshing.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var list = await runtime.ListAsync(cancellationToken).ConfigureAwait(false);
            IReadOnlyList<ContainerStats>? stats = includeStats
                ? await runtime.StatsAsync(cancellationToken).ConfigureAwait(false)
                : null;

            lock (_gate)
            {
                _containers = list;
                if (stats is not null)
                {
                    _stats = stats.ToDictionary(s => s.Id, StringComparer.Ordinal);
                }

                _updatedAt = time.GetUtcNow();
                _error = null;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // Falha do Docker/agente vira estado visível, não exceção.
        catch (Exception e)
#pragma warning restore CA1031
        {
            logger.LogWarning(e, "Falha ao atualizar containers");
            lock (_gate)
            {
                _error = "Não foi possível consultar os containers (agente ou Docker indisponível).";
            }
        }
        finally
        {
            _refreshing.Release();
        }

        await hub.Clients.All.SendAsync("containers", Snapshot, cancellationToken).ConfigureAwait(false);
    }

    public void Dispose() => _refreshing.Dispose();
}

public sealed class ContainerSampler(
    ContainersService service,
    IOptions<MetricsOptions> options,
    TimeProvider time) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(1, options.Value.ContainerIntervalSeconds));
        using var timer = new PeriodicTimer(interval, time);
        do
        {
            await service.RefreshAsync(includeStats: true, stoppingToken).ConfigureAwait(false);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }
}
