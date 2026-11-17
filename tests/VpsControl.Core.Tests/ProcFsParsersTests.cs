using VpsControl.Core.Metrics.ProcFs;

namespace VpsControl.Core.Tests;

public class ProcFsParsersTests
{
    [Fact]
    public void ParseCpuTimes_sums_first_eight_fields_and_counts_iowait_as_idle()
    {
        const string stat = """
            cpu  100 0 50 800 50 0 0 0 10 0
            cpu0 50 0 25 400 25 0 0 0 5 0
            intr 12345
            """;

        var times = ProcFsParsers.ParseCpuTimes(stat);

        Assert.Equal(850UL, times.Idle);
        Assert.Equal(1000UL, times.Total);
    }

    [Fact]
    public void CpuPercent_computes_busy_share_of_delta()
    {
        var previous = new ProcFsParsers.CpuTimes(Idle: 800, Total: 1000);
        var current = new ProcFsParsers.CpuTimes(Idle: 875, Total: 1100);

        Assert.Equal(25.0, ProcFsParsers.CpuPercent(previous, current));
    }

    [Fact]
    public void CpuPercent_is_zero_when_counters_do_not_advance()
    {
        var sample = new ProcFsParsers.CpuTimes(800, 1000);
        Assert.Equal(0, ProcFsParsers.CpuPercent(sample, sample));
    }

    [Fact]
    public void ParseMemInfo_converts_kb_to_bytes()
    {
        const string memInfo = """
            MemTotal:        8000000 kB
            MemFree:          500000 kB
            MemAvailable:    3000000 kB
            SwapTotal:       2000000 kB
            SwapFree:        1500000 kB
            """;

        var mem = ProcFsParsers.ParseMemInfo(memInfo);

        Assert.Equal(8000000L * 1024, mem.TotalBytes);
        Assert.Equal(3000000L * 1024, mem.AvailableBytes);
        Assert.Equal(5000000L * 1024, mem.UsedBytes);
        Assert.Equal(500000L * 1024, mem.SwapUsedBytes);
    }

    [Fact]
    public void ParseNetDev_reads_rx_and_tx_bytes()
    {
        const string netDev = """
            Inter-|   Receive                                                |  Transmit
             face |bytes    packets errs drop fifo frame compressed multicast|bytes    packets errs drop fifo colls carrier compressed
                lo:  1000      10    0    0    0     0          0         0     1000      10    0    0    0     0       0          0
              eth0: 123456    100    0    0    0     0          0         0    654321     90    0    0    0     0       0          0
            veth1a2b: 5 1 0 0 0 0 0 0 6 1 0 0 0 0 0 0
            """;

        var counters = ProcFsParsers.ParseNetDev(netDev);
        var relevant = counters.Where(c => ProcFsParsers.IsRelevantInterface(c.Interface)).ToList();

        Assert.Equal(3, counters.Count);
        var eth0 = Assert.Single(relevant);
        Assert.Equal("eth0", eth0.Interface);
        Assert.Equal(123456, eth0.RxBytes);
        Assert.Equal(654321, eth0.TxBytes);
    }

    [Fact]
    public void ParseMounts_keeps_physical_filesystems_once_per_device()
    {
        const string mounts = """
            /dev/vda1 / ext4 rw,relatime 0 0
            proc /proc proc rw 0 0
            tmpfs /run tmpfs rw 0 0
            /dev/vda15 /boot/efi vfat rw 0 0
            /dev/vda1 /var/lib/docker/volumes/x ext4 rw 0 0
            overlay /var/lib/docker/overlay2/abc/merged overlay rw 0 0
            /dev/vdb1 /mnt/my\040data xfs rw 0 0
            """;

        var result = ProcFsParsers.ParseMounts(mounts);

        Assert.Equal(["/", "/boot/efi", "/mnt/my data"], result.Select(m => m.MountPoint));
    }

    [Fact]
    public void ParseLoadAvg_uptime_and_os_release()
    {
        Assert.Equal((0.5, 0.25, 0.1), ProcFsParsers.ParseLoadAvg("0.50 0.25 0.10 1/234 5678"));
        Assert.Equal(TimeSpan.FromSeconds(3600.5), ProcFsParsers.ParseUptime("3600.50 7000.00"));
        Assert.Equal("Ubuntu 24.04.3 LTS", ProcFsParsers.ParseOsPrettyName("NAME=\"Ubuntu\"\nPRETTY_NAME=\"Ubuntu 24.04.3 LTS\"\n"));
    }
}
