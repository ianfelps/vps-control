namespace VpsControl.Core.Metrics;

/// <summary>Fonte de métricas do host (agente real, /proc local ou fake de desenvolvimento).</summary>
public interface IHostMetricsProvider
{
    Task<HostSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default);
}
