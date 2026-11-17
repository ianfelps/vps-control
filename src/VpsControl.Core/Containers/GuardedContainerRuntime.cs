using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace VpsControl.Core.Containers;

/// <summary>
/// Barreira aplicada no componente privilegiado: valida identificadores, limita tail/taxa de logs
/// e recusa parar/reiniciar containers protegidos.
/// </summary>
public sealed partial class GuardedContainerRuntime(IContainerRuntime inner) : IContainerRuntime
{
    public const int MaxTail = 2000;
    public const int MaxLineLength = 4096;
    public const int MaxLinesPerSecond = 200;

    // ID hex do Docker (12–64) ou nome de container ([a-zA-Z0-9][a-zA-Z0-9_.-]+).
    [GeneratedRegex("^(?:[a-f0-9]{12,64}|[a-zA-Z0-9][a-zA-Z0-9_.-]{0,127})$")]
    private static partial Regex ValidId();

    public static bool IsValidId(string? id) => id is not null && ValidId().IsMatch(id);

    public Task<IReadOnlyList<ContainerSummary>> ListAsync(CancellationToken cancellationToken = default) =>
        inner.ListAsync(cancellationToken);

    public Task<ContainerDetails> InspectAsync(string id, bool revealEnv, CancellationToken cancellationToken = default)
    {
        EnsureValid(id);
        return inner.InspectAsync(id, revealEnv, cancellationToken);
    }

    public Task<IReadOnlyList<ContainerStats>> StatsAsync(CancellationToken cancellationToken = default) =>
        inner.StatsAsync(cancellationToken);

    public async IAsyncEnumerable<LogLine> LogsAsync(
        string id,
        int tail,
        bool follow,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        EnsureValid(id);
        var windowStart = Environment.TickCount64;
        var inWindow = 0;
        var dropped = 0;

        await foreach (var line in inner.LogsAsync(id, Math.Clamp(tail, 0, MaxTail), follow, cancellationToken)
                           .ConfigureAwait(false))
        {
            var now = Environment.TickCount64;
            if (now - windowStart >= 1000)
            {
                if (dropped > 0)
                {
                    yield return new LogLine(null, "system", $"[vps-control] {dropped} linhas descartadas (limite de {MaxLinesPerSecond}/s)");
                }

                windowStart = now;
                inWindow = 0;
                dropped = 0;
            }

            if (follow && ++inWindow > MaxLinesPerSecond)
            {
                dropped++;
                continue;
            }

            yield return line.Text.Length > MaxLineLength
                ? line with { Text = string.Concat(line.Text.AsSpan(0, MaxLineLength), "…") }
                : line;
        }
    }

    public async Task ExecuteAsync(string id, ContainerAction action, CancellationToken cancellationToken = default)
    {
        EnsureValid(id);
        if (!Enum.IsDefined(action))
        {
            throw new ArgumentOutOfRangeException(nameof(action));
        }

        if (action is ContainerAction.Stop or ContainerAction.Restart)
        {
            var details = await inner.InspectAsync(id, revealEnv: false, cancellationToken).ConfigureAwait(false);
            if (details.Summary.Protected)
            {
                throw new ContainerProtectedException(details.Summary.Name);
            }
        }

        await inner.ExecuteAsync(id, action, cancellationToken).ConfigureAwait(false);
    }

    private static void EnsureValid(string id)
    {
        if (!IsValidId(id))
        {
            throw new InvalidContainerIdException(id);
        }
    }
}
