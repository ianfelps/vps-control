using VpsControl.Core.Metrics.ProcFs;

namespace VpsControl.Core.Tests;

/// <summary>Lê o /proc real. Só tem efeito em Linux (CI e containers); no Windows passa sem verificar.</summary>
public class ProcFsProviderLinuxTests
{
    [Fact]
    public async Task Reads_real_proc_and_computes_rates_on_second_sample()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var provider = new ProcFsHostMetricsProvider();

        var first = await provider.GetSnapshotAsync();
        await Task.Delay(300);
        var second = await provider.GetSnapshotAsync();

        Assert.True(first.Memory.TotalBytes > 0);
        Assert.True(first.Info.Uptime > TimeSpan.Zero);
        Assert.False(string.IsNullOrWhiteSpace(first.Info.KernelVersion));
        Assert.InRange(second.Cpu.UsagePercent, 0, 100);
        Assert.All(second.Networks, n => Assert.True(n.RxBytesPerSecond >= 0));
    }
}
