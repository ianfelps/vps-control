using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using VpsControl.Core.Metrics;

namespace VpsControl.Api.Metrics;

/// <summary>Coleta métricas periodicamente, alimenta o histórico e publica via SignalR.</summary>
public sealed class MetricsSampler(
    IHostMetricsProvider provider,
    MetricsState state,
    IHubContext<MetricsHub> hub,
    IOptions<MetricsOptions> options,
    TimeProvider time,
    ILogger<MetricsSampler> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(0.5, options.Value.SampleIntervalSeconds));
        using var timer = new PeriodicTimer(interval, time);
        var wasFailing = false;

        do
        {
            try
            {
                var snapshot = await provider.GetSnapshotAsync(stoppingToken).ConfigureAwait(false);
                var point = state.Record(snapshot);
                if (wasFailing)
                {
                    logger.LogInformation("Fonte de métricas recuperada.");
                    wasFailing = false;
                }

                await hub.Clients.All.SendAsync("snapshot", snapshot, point, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
#pragma warning disable CA1031 // Falha da fonte não pode derrubar o sampler.
            catch (Exception e)
#pragma warning restore CA1031
            {
                // Mensagem curta para a UI; o detalhe técnico fica no log.
                state.RecordFailure(e switch
                {
                    Grpc.Core.RpcException { StatusCode: Grpc.Core.StatusCode.Unauthenticated } =>
                        "Agente recusou o token (verifique /etc/vps-control/agent.token).",
                    Grpc.Core.RpcException { StatusCode: Grpc.Core.StatusCode.Unavailable or Grpc.Core.StatusCode.DeadlineExceeded } =>
                        "Agente inacessível (serviço parado ou sem permissão no socket).",
                    Grpc.Core.RpcException r => $"Erro do agente: {r.StatusCode}.",
                    _ => e.Message,
                });
                if (!wasFailing)
                {
                    logger.LogWarning(e, "Falha ao coletar métricas.");
                    wasFailing = true;
                }

                await hub.Clients.All.SendAsync("status", state.Status, stoppingToken).ConfigureAwait(false);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }
}
