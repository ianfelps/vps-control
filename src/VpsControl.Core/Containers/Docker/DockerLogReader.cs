using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Text;

namespace VpsControl.Core.Containers.Docker;

/// <summary>
/// Lê o stream de logs do Docker. Sem TTY, o Docker multiplexa stdout/stderr em frames
/// [stream(1) 0 0 0 tamanho(4, big-endian)] + payload; com TTY, o texto vem cru.
/// </summary>
internal static class DockerLogReader
{
    private const int MaxFrame = 1024 * 1024;

    public static async IAsyncEnumerable<LogLine> ReadAsync(
        Stream stream,
        bool tty,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var pending = new Dictionary<string, StringBuilder>(StringComparer.Ordinal);

        if (tty)
        {
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
            while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
            {
                yield return DockerMapper.ParseLogLine(line, "stdout");
            }

            yield break;
        }

        var header = new byte[8];
        while (true)
        {
            if (!await ReadExactAsync(stream, header, cancellationToken).ConfigureAwait(false))
            {
                break;
            }

            var streamName = header[0] == 2 ? "stderr" : "stdout";
            var size = (int)Math.Min(BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(4)), MaxFrame);
            var payload = new byte[size];
            if (!await ReadExactAsync(stream, payload, cancellationToken).ConfigureAwait(false))
            {
                break;
            }

            if (!pending.TryGetValue(streamName, out var buffer))
            {
                buffer = new StringBuilder();
                pending[streamName] = buffer;
            }

            buffer.Append(Encoding.UTF8.GetString(payload));
            foreach (var line in DrainLines(buffer))
            {
                yield return DockerMapper.ParseLogLine(line, streamName);
            }
        }

        foreach (var (name, buffer) in pending)
        {
            if (buffer.Length > 0)
            {
                yield return DockerMapper.ParseLogLine(buffer.ToString(), name);
            }
        }
    }

    private static List<string> DrainLines(StringBuilder buffer)
    {
        var lines = new List<string>();
        var text = buffer.ToString();
        var start = 0;
        int newline;
        while ((newline = text.IndexOf('\n', start)) >= 0)
        {
            lines.Add(text[start..newline]);
            start = newline + 1;
        }

        buffer.Clear().Append(text, start, text.Length - start);
        return lines;
    }

    private static async Task<bool> ReadExactAsync(Stream stream, byte[] buffer, CancellationToken ct)
    {
        var read = 0;
        while (read < buffer.Length)
        {
            var n = await stream.ReadAsync(buffer.AsMemory(read), ct).ConfigureAwait(false);
            if (n == 0)
            {
                return false;
            }

            read += n;
        }

        return true;
    }
}
