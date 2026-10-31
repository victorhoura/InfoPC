namespace InfoPC;

/// <summary>Memória do sistema em kB, como em /proc/meminfo.</summary>
public sealed record MemStats(long TotalKb, long FreeKb, long AvailableKb, long BuffersKb, long CachedKb, long SwapTotalKb, long SwapFreeKb, long ShmemKb)
{
    public long UsedKb => Math.Max(0, TotalKb - AvailableKb);
    public double UsedPct => TotalKb > 0 ? 100.0 * UsedKb / TotalKb : 0;
    public long SwapUsedKb => Math.Max(0, SwapTotalKb - SwapFreeKb);
}

public sealed record SwapDevice(string Name, string Type, long SizeKb, long UsedKb)
{
    public bool IsZram => Name.Contains("zram", StringComparison.Ordinal);
}

public static class MemInfo
{
    public static MemStats? Read()
    {
        var m = Sys.KeyValues("/proc/meminfo");
        long K(string k) => Num.L(m.GetValueOrDefault(k, "").Replace("kB", "").Trim()) ?? 0;
        long total = K("MemTotal");
        if (total <= 0) return null;
        long free = K("MemFree");
        long avail = m.ContainsKey("MemAvailable") ? K("MemAvailable") : free + K("Buffers") + K("Cached"); // kernels < 3.14
        return new MemStats(total, free, avail, K("Buffers"), K("Cached"), K("SwapTotal"), K("SwapFree"), K("Shmem"));
    }

    public static List<SwapDevice> Swaps()
    {
        var list = new List<SwapDevice>();
        foreach (var line in Sys.Lines("/proc/swaps").Skip(1))
        {
            var f = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (f.Length >= 4) list.Add(new SwapDevice(f[0], f[1], Num.L(f[2]) ?? 0, Num.L(f[3]) ?? 0));
        }
        return list;
    }
}
