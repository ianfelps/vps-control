using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using VpsControl.Api.Containers;
using VpsControl.Api.Metrics;
using VpsControl.Core.Containers;

namespace VpsControl.Api.Alerts;

public enum AlertSeverity
{
    Warning,
    Critical,
}

public sealed record Alert(string Id, AlertSeverity Severity, string Source, string Title, string Detail);

public sealed class AlertOptions
{
    public const string Section = "Alerts";

    public double CpuWarningPercent { get; set; } = 80;
    public double CpuCriticalPercent { get; set; } = 95;
    public double MemoryWarningPercent { get; set; } = 80;
    public double MemoryCriticalPercent { get; set; } = 90;
    public double DiskWarningPercent { get; set; } = 80;
    public double DiskCriticalPercent { get; set; } = 90;
    public double SwapWarningPercent { get; set; } = 50;

    /// <summary>CPU é avaliada pela média deste período, para não alertar por picos.</summary>
    public int CpuWindowSeconds { get; set; } = 60;
}

/// <summary>
/// Avalia indicadores de alerta sob demanda a partir do estado atual (sem histórico persistente).
/// Base para notificações futuras.
/// </summary>
public sealed partial class AlertEvaluator(MetricsState metrics, ContainersService containers, IOptionsMonitor<AlertOptions> options)
{
    [GeneratedRegex(@"^Exited \((\d+)\)")]
    private static partial Regex ExitedPattern();

    public IReadOnlyList<Alert> Evaluate()
    {
        var o = options.CurrentValue;
        var alerts = new List<Alert>();

        var status = metrics.Status;
        if (!status.Available)
        {
            alerts.Add(new Alert("metrics.unavailable", AlertSeverity.Critical, "host", "Métricas indisponíveis",
                status.LastError ?? "Sem resposta da fonte de métricas."));
        }

        if (metrics.Latest is { } s)
        {
            var cutoff = s.Timestamp.AddSeconds(-o.CpuWindowSeconds);
            var window = metrics.History().Where(p => p.Timestamp >= cutoff).ToList();
            var cpu = window.Count > 0 ? window.Average(p => p.CpuPercent) : s.Cpu.UsagePercent;
            Threshold(alerts, "host.cpu", "host", "CPU alta", cpu, o.CpuWarningPercent, o.CpuCriticalPercent,
                v => $"Média de {v:0.#}% no último minuto.");

            var mem = s.Memory.TotalBytes > 0 ? 100.0 * s.Memory.UsedBytes / s.Memory.TotalBytes : 0;
            Threshold(alerts, "host.memory", "host", "Memória alta", mem, o.MemoryWarningPercent, o.MemoryCriticalPercent,
                v => $"{v:0.#}% em uso.");

            if (s.Memory.SwapTotalBytes > 0)
            {
                var swap = 100.0 * s.Memory.SwapUsedBytes / s.Memory.SwapTotalBytes;
                if (swap >= o.SwapWarningPercent)
                {
                    alerts.Add(new Alert("host.swap", AlertSeverity.Warning, "host", "Uso de swap elevado",
                        string.Create(CultureInfo.InvariantCulture, $"{swap:0.#}% do swap em uso.")));
                }
            }

            foreach (var d in s.Disks)
            {
                var pct = d.TotalBytes > 0 ? 100.0 * d.UsedBytes / d.TotalBytes : 0;
                Threshold(alerts, $"host.disk:{d.MountPoint}", "host", $"Disco {d.MountPoint} quase cheio", pct,
                    o.DiskWarningPercent, o.DiskCriticalPercent, v => $"{v:0.#}% usado.");
            }
        }

        var snapshot = containers.Snapshot;
        if (snapshot.Error is not null)
        {
            alerts.Add(new Alert("containers.unavailable", AlertSeverity.Warning, "docker", "Containers indisponíveis", snapshot.Error));
        }

        foreach (var c in snapshot.Items)
        {
            if (c.Health == ContainerHealth.Unhealthy)
            {
                alerts.Add(new Alert($"container.unhealthy:{c.Id}", AlertSeverity.Critical, "docker",
                    $"{c.Name} não saudável", "Healthcheck falhando."));
            }
            else if (c.State == "restarting")
            {
                alerts.Add(new Alert($"container.restarting:{c.Id}", AlertSeverity.Warning, "docker",
                    $"{c.Name} reiniciando", c.Status));
            }
            else if (ExitedPattern().Match(c.Status) is { Success: true } m && m.Groups[1].Value != "0")
            {
                alerts.Add(new Alert($"container.exited:{c.Id}", AlertSeverity.Warning, "docker",
                    $"{c.Name} encerrou com erro", $"Código de saída {m.Groups[1].Value}."));
            }
        }

        return alerts
            .OrderByDescending(a => a.Severity)
            .ThenBy(a => a.Id, StringComparer.Ordinal)
            .ToList();
    }

    private static void Threshold(
        List<Alert> alerts, string id, string source, string title, double value,
        double warning, double critical, Func<double, FormattableString> detail)
    {
        if (value >= critical)
        {
            alerts.Add(new Alert(id, AlertSeverity.Critical, source, title, FormattableString.Invariant(detail(value))));
        }
        else if (value >= warning)
        {
            alerts.Add(new Alert(id, AlertSeverity.Warning, source, title, FormattableString.Invariant(detail(value))));
        }
    }
}
