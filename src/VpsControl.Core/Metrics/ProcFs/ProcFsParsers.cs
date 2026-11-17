using System.Globalization;

namespace VpsControl.Core.Metrics.ProcFs;

/// <summary>Parsers puros dos arquivos de /proc. Sem IO, para serem testáveis em qualquer SO.</summary>
internal static class ProcFsParsers
{
    private static readonly char[] Whitespace = [' ', '\t'];

    internal readonly record struct CpuTimes(ulong Idle, ulong Total);

    internal readonly record struct NetCounters(string Interface, long RxBytes, long TxBytes);

    internal readonly record struct Mount(string Device, string MountPoint, string FileSystem);

    /// <summary>Lê a linha agregada "cpu" de /proc/stat.</summary>
    internal static CpuTimes ParseCpuTimes(string procStat)
    {
        foreach (var line in procStat.AsSpan().EnumerateLines())
        {
            if (!line.StartsWith("cpu ", StringComparison.Ordinal))
            {
                continue;
            }

            var fields = line.ToString().Split(Whitespace, StringSplitOptions.RemoveEmptyEntries);
            // user nice system idle iowait irq softirq steal [guest guest_nice] — guest já está contido em user.
            var values = fields.Skip(1).Take(8).Select(v => ulong.Parse(v, CultureInfo.InvariantCulture)).ToArray();
            var idle = values[3] + (values.Length > 4 ? values[4] : 0);
            ulong total = 0;
            foreach (var v in values)
            {
                total += v;
            }

            return new CpuTimes(idle, total);
        }

        throw new FormatException("Linha 'cpu' não encontrada em /proc/stat.");
    }

    internal static double CpuPercent(CpuTimes previous, CpuTimes current)
    {
        var totalDelta = current.Total - previous.Total;
        if (current.Total <= previous.Total || totalDelta == 0)
        {
            return 0;
        }

        var idleDelta = current.Idle >= previous.Idle ? current.Idle - previous.Idle : 0;
        var busy = 1.0 - ((double)idleDelta / totalDelta);
        return Math.Round(Math.Clamp(busy * 100, 0, 100), 1);
    }

    /// <summary>Retorna (MemTotal, MemAvailable, SwapTotal, SwapFree) em bytes.</summary>
    internal static MemoryUsage ParseMemInfo(string memInfo)
    {
        var values = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var raw in memInfo.AsSpan().EnumerateLines())
        {
            var line = raw.ToString();
            var colon = line.IndexOf(':', StringComparison.Ordinal);
            if (colon <= 0)
            {
                continue;
            }

            var parts = line[(colon + 1)..].Split(Whitespace, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0 || !long.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var v))
            {
                continue;
            }

            var isKb = parts.Length > 1 && parts[1].Equals("kB", StringComparison.OrdinalIgnoreCase);
            values[line[..colon]] = isKb ? v * 1024 : v;
        }

        long Get(string key) => values.TryGetValue(key, out var v) ? v : 0;
        var available = values.ContainsKey("MemAvailable")
            ? Get("MemAvailable")
            : Get("MemFree") + Get("Buffers") + Get("Cached");
        return new MemoryUsage(Get("MemTotal"), available, Get("SwapTotal"), Get("SwapFree"));
    }

    internal static IReadOnlyList<NetCounters> ParseNetDev(string netDev)
    {
        var result = new List<NetCounters>();
        foreach (var raw in netDev.AsSpan().EnumerateLines())
        {
            var line = raw.ToString();
            var colon = line.IndexOf(':', StringComparison.Ordinal);
            if (colon <= 0)
            {
                continue; // cabeçalhos
            }

            var name = line[..colon].Trim();
            var f = line[(colon + 1)..].Split(Whitespace, StringSplitOptions.RemoveEmptyEntries);
            if (f.Length < 9)
            {
                continue;
            }

            result.Add(new NetCounters(
                name,
                long.Parse(f[0], CultureInfo.InvariantCulture),
                long.Parse(f[8], CultureInfo.InvariantCulture)));
        }

        return result;
    }

    /// <summary>Interfaces relevantes: ignora loopback e pares veth dos containers.</summary>
    internal static bool IsRelevantInterface(string name) =>
        name != "lo" && !name.StartsWith("veth", StringComparison.Ordinal);

    internal static (double Load1, double Load5, double Load15) ParseLoadAvg(string loadAvg)
    {
        var f = loadAvg.Split(Whitespace, StringSplitOptions.RemoveEmptyEntries);
        return (
            double.Parse(f[0], CultureInfo.InvariantCulture),
            double.Parse(f[1], CultureInfo.InvariantCulture),
            double.Parse(f[2], CultureInfo.InvariantCulture));
    }

    internal static TimeSpan ParseUptime(string uptime)
    {
        var first = uptime.Split(Whitespace, StringSplitOptions.RemoveEmptyEntries)[0];
        return TimeSpan.FromSeconds(double.Parse(first, CultureInfo.InvariantCulture));
    }

    internal static string ParseOsPrettyName(string osRelease)
    {
        foreach (var raw in osRelease.AsSpan().EnumerateLines())
        {
            var line = raw.ToString();
            if (line.StartsWith("PRETTY_NAME=", StringComparison.Ordinal))
            {
                return line["PRETTY_NAME=".Length..].Trim('"');
            }
        }

        return "Linux";
    }

    private static readonly HashSet<string> PhysicalFileSystems = new(StringComparer.Ordinal)
    {
        "ext2", "ext3", "ext4", "xfs", "btrfs", "zfs", "vfat", "f2fs",
    };

    /// <summary>Montagens de sistemas de arquivos reais, sem duplicar o mesmo dispositivo.</summary>
    internal static IReadOnlyList<Mount> ParseMounts(string procMounts)
    {
        var seenDevices = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<Mount>();
        foreach (var raw in procMounts.AsSpan().EnumerateLines())
        {
            var f = raw.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (f.Length < 3 || !PhysicalFileSystems.Contains(f[2]))
            {
                continue;
            }

            // Bind mounts (ex.: volumes Docker) repetem o dispositivo; mantém o primeiro.
            if (!seenDevices.Add(f[0]))
            {
                continue;
            }

            // /proc/mounts escapa espaço como \040.
            var mountPoint = f[1].Replace("\\040", " ", StringComparison.Ordinal);
            result.Add(new Mount(f[0], mountPoint, f[2]));
        }

        return result;
    }
}
