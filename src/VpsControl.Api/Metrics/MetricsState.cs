using VpsControl.Core.Metrics;

namespace VpsControl.Api.Metrics;

/// <summary>Ponto compacto do histórico para gráficos.</summary>
public sealed record MetricsPoint(
    DateTimeOffset Timestamp,
    double CpuPercent,
    double MemoryPercent,
    double RxBytesPerSecond,
    double TxBytesPerSecond);

public sealed record SourceStatus(
    string Source,
    bool Available,
    DateTimeOffset? LastSuccessAt,
    string? LastError);

/// <summary>
/// Último snapshot + ring buffer em memória (~1 h). Histórico persistente fica para depois do MVP.
/// Thread-safe: escrito pelo sampler, lido pelos endpoints.
/// </summary>
public sealed class MetricsState(int capacity, string source)
{
    private readonly Lock _gate = new();
    private readonly MetricsPoint[] _buffer = new MetricsPoint[Math.Max(1, capacity)];
    private int _start;
    private int _count;
    private HostSnapshot? _latest;
    private DateTimeOffset? _lastSuccessAt;
    private string? _lastError;

    public HostSnapshot? Latest
    {
        get
        {
            lock (_gate)
            {
                return _latest;
            }
        }
    }

    public SourceStatus Status
    {
        get
        {
            lock (_gate)
            {
                return new SourceStatus(source, _lastError is null && _latest is not null, _lastSuccessAt, _lastError);
            }
        }
    }

    public MetricsPoint Record(HostSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var point = ToPoint(snapshot);
        lock (_gate)
        {
            _latest = snapshot;
            _lastSuccessAt = snapshot.Timestamp;
            _lastError = null;
            var index = (_start + _count) % _buffer.Length;
            _buffer[index] = point;
            if (_count < _buffer.Length)
            {
                _count++;
            }
            else
            {
                _start = (_start + 1) % _buffer.Length;
            }
        }

        return point;
    }

    public void RecordFailure(string error)
    {
        lock (_gate)
        {
            _lastError = error;
        }
    }

    public IReadOnlyList<MetricsPoint> History()
    {
        lock (_gate)
        {
            var result = new MetricsPoint[_count];
            for (var i = 0; i < _count; i++)
            {
                result[i] = _buffer[(_start + i) % _buffer.Length];
            }

            return result;
        }
    }

    internal static MetricsPoint ToPoint(HostSnapshot s)
    {
        var mem = s.Memory.TotalBytes > 0 ? 100.0 * s.Memory.UsedBytes / s.Memory.TotalBytes : 0;
        return new MetricsPoint(
            s.Timestamp,
            s.Cpu.UsagePercent,
            Math.Round(mem, 1),
            s.Networks.Sum(n => n.RxBytesPerSecond),
            s.Networks.Sum(n => n.TxBytesPerSecond));
    }
}
