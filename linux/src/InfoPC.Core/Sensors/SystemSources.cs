using System.Diagnostics;

namespace InfoPC;

/// <summary>Carga (de /proc/stat) e clock (cpufreq) da CPU, por thread e no total.</summary>
public sealed class CpuSource : ISensorSource
{
    private Dictionary<string, (long Total, long Idle)> _prev = new();

    /// <summary>Carga acumulada por linha de /proc/stat: chave "cpu" (total) e "cpuN".</summary>
    public static Dictionary<string, (long Total, long Idle)> ReadStat()
    {
        var d = new Dictionary<string, (long, long)>();
        foreach (var line in Sys.Lines("/proc/stat"))
        {
            if (!line.StartsWith("cpu", StringComparison.Ordinal)) break;
            var f = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (f.Length < 5) continue;
            long total = 0;
            for (int i = 1; i < f.Length && i <= 8; i++) total += Num.L(f[i]) ?? 0; // user..steal (guest já está dentro de user)
            long idle = (Num.L(f[4]) ?? 0) + (f.Length > 5 ? Num.L(f[5]) ?? 0 : 0);   // idle + iowait
            d[f[0]] = (total, idle);
        }
        return d;
    }

    public static float? Load((long Total, long Idle) a, (long Total, long Idle) b)
    {
        long dt = b.Total - a.Total, di = b.Idle - a.Idle;
        if (dt <= 0) return null;
        return (float)Math.Clamp(100.0 * (dt - di) / dt, 0, 100);
    }

    public IEnumerable<SensorSample> Read()
    {
        var now = ReadStat();
        float? maxCore = null;
        var rows = new List<SensorSample>();
        foreach (var (key, cur) in now)
        {
            if (!_prev.TryGetValue(key, out var old)) continue;
            float? load = Load(old, cur);
            if (key == "cpu")
                rows.Add(new SensorSample("cpu/load/total", "Processador", HwKind.Cpu, SensorKind.Load, "CPU Total", load, "cpu.load", 3));
            else
            {
                string n = key[3..];
                rows.Add(new SensorSample("cpu/load/" + n, "Processador", HwKind.Cpu, SensorKind.Load, "CPU Thread #" + n, load));
                if (load != null && (maxCore == null || load > maxCore)) maxCore = load;
            }
        }
        _prev = now;
        if (maxCore != null)
            rows.Add(new SensorSample("cpu/load/max", "Processador", HwKind.Cpu, SensorKind.Load, "CPU Thread mais ocupada", maxCore, "cpu.maxcore", 3));

        // clocks: cpufreq (kHz); em máquinas sem cpufreq (VM) usa o MHz de /proc/cpuinfo
        var clocks = new List<(string N, double Mhz)>();
        foreach (var c in Sys.ListNatural("/sys/devices/system/cpu", "cpu"))
            if (Sys.Double($"/sys/devices/system/cpu/{c}/cpufreq/scaling_cur_freq") is { } khz) clocks.Add((c[3..], khz / 1000));
        if (clocks.Count == 0)
            foreach (var b in CpuInfo.ParseCpuinfo(Sys.Lines("/proc/cpuinfo")))
                if (Num.D(b.GetValueOrDefault("cpu MHz")) is { } mhz) clocks.Add((b.GetValueOrDefault("processor", "0"), mhz));
        foreach (var (n, mhz) in clocks)
            rows.Add(new SensorSample("cpu/clock/" + n, "Processador", HwKind.Cpu, SensorKind.Clock, "CPU Thread #" + n, (float)mhz));
        if (clocks.Count > 0)
        {
            rows.Add(new SensorSample("cpu/clock/avg", "Processador", HwKind.Cpu, SensorKind.Clock, "CPU Clock médio", (float)clocks.Average(c => c.Mhz), "cpu.clock", 3));
            rows.Add(new SensorSample("cpu/clock/max", "Processador", HwKind.Cpu, SensorKind.Clock, "CPU Clock máximo (thread)", (float)clocks.Max(c => c.Mhz), "cpu.clockmax", 3));
        }
        return rows;
    }
}

/// <summary>Memória e swap (de /proc/meminfo).</summary>
public sealed class MemorySource : ISensorSource
{
    public IEnumerable<SensorSample> Read()
    {
        var m = MemInfo.Read();
        if (m == null) yield break;
        const string hw = "Memória";
        yield return new SensorSample("mem/load", hw, HwKind.Memory, SensorKind.Load, "Memória", (float)m.UsedPct, "ram.pct", 3);
        yield return new SensorSample("mem/used", hw, HwKind.Memory, SensorKind.Data, "Memória usada", m.UsedKb / 1048576f, "ram.used", 3);
        yield return new SensorSample("mem/avail", hw, HwKind.Memory, SensorKind.Data, "Memória disponível", m.AvailableKb / 1048576f);
        yield return new SensorSample("mem/cache", hw, HwKind.Memory, SensorKind.Data, "Cache e buffers", (m.CachedKb + m.BuffersKb) / 1048576f);
        if (m.SwapTotalKb > 0)
        {
            yield return new SensorSample("swap/load", hw, HwKind.Memory, SensorKind.Load, "Swap", 100f * m.SwapUsedKb / m.SwapTotalKb);
            yield return new SensorSample("swap/used", hw, HwKind.Memory, SensorKind.Data, "Swap usada", m.SwapUsedKb / 1048576f);
        }
    }
}

/// <summary>Zonas térmicas genéricas (/sys/class/thermal). As que repetem o hwmon (x86_pkg_temp com coretemp) ficam de fora.</summary>
public sealed class ThermalSource : ISensorSource
{
    public IEnumerable<SensorSample> Read()
    {
        bool hasCoretemp = Sys.List("/sys/class/hwmon").Any(h => Sys.Read($"/sys/class/hwmon/{h}/name") is "coretemp" or "k10temp");
        var seen = new Dictionary<string, int>();
        foreach (var z in Sys.ListNatural("/sys/class/thermal", "thermal_zone"))
        {
            string type = Sys.Read($"/sys/class/thermal/{z}/type") ?? z;
            if (hasCoretemp && type == "x86_pkg_temp") continue;
            if (Sys.Double($"/sys/class/thermal/{z}/temp") is not { } t || t <= -100000 || t > 300000) continue;
            seen[type] = seen.GetValueOrDefault(type) + 1;
            string name = seen[type] > 1 ? $"{type} #{seen[type]}" : type;
            bool cpuLike = type is "cpu-thermal" or "soc-thermal" or "cpu_thermal" or "x86_pkg_temp";
            yield return new SensorSample($"thermal/{z}", "Zonas térmicas (ACPI)", cpuLike ? HwKind.Cpu : HwKind.Board, SensorKind.Temperature, name, (float)(t / 1000),
                                          cpuLike ? "cpu.temp" : null, 1);
        }
    }
}

/// <summary>Consumo da CPU e da memória pelo RAPL (/sys/class/powercap). Em kernels recentes só o root lê energy_uj.</summary>
public sealed class RaplSource : ISensorSource
{
    private readonly Dictionary<string, (long Energy, long Ticks)> _prev = new();

    /// <summary>Verdadeiro se há zonas RAPL mas a leitura foi negada (precisa de root).</summary>
    public bool Blocked { get; private set; }

    public IEnumerable<SensorSample> Read()
    {
        const string dir = "/sys/class/powercap";
        Blocked = false;
        var list = new List<SensorSample>();
        foreach (var z in Sys.List(dir).Where(n => n.StartsWith("intel-rapl:", StringComparison.Ordinal) || n.StartsWith("amd-rapl:", StringComparison.Ordinal)))
        {
            string zn = Sys.Read($"{dir}/{z}/name") ?? z;
            string? raw = Sys.Read($"{dir}/{z}/energy_uj");
            if (raw == null) { if (Sys.FileExists($"{dir}/{z}/energy_uj")) Blocked = true; continue; }
            long e = Num.L(raw) ?? 0, now = Stopwatch.GetTimestamp();
            long range = Sys.Long($"{dir}/{z}/max_energy_range_uj") ?? long.MaxValue;
            float? watts = null;
            if (_prev.TryGetValue(z, out var p))
            {
                double secs = (now - p.Ticks) / (double)Stopwatch.Frequency;
                if (secs > 0.2)
                {
                    long delta = e >= p.Energy ? e - p.Energy : range - p.Energy + e; // contador deu a volta
                    watts = (float)(delta / 1e6 / secs);
                    _prev[z] = (e, now);
                }
            }
            else _prev[z] = (e, now);
            bool pkg = zn.StartsWith("package", StringComparison.Ordinal);
            string label = pkg ? "CPU Package" : zn switch { "core" => "CPU Cores", "uncore" => "CPU Uncore / GPU integrada", "dram" => "Memória (DRAM)", _ => zn };
            list.Add(new SensorSample("rapl/" + z, "Processador", HwKind.Cpu, SensorKind.Power, label, watts, pkg ? "cpu.power" : null, 2));
        }
        return list;
    }
}

/// <summary>Leitura e escrita dos discos (/proc/diskstats): taxa e atividade, só dos discos físicos.</summary>
public sealed class DiskIoSource : ISensorSource
{
    private Dictionary<string, (long Rd, long Wr, long Ms)> _prev = new();
    private long _ticks;

    public IEnumerable<SensorSample> Read()
    {
        var now = new Dictionary<string, (long, long, long)>();
        foreach (var line in Sys.Lines("/proc/diskstats"))
        {
            var f = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (f.Length < 14) continue;
            string name = f[2];
            if (!Sys.DirExists("/sys/block/" + name) || name.StartsWith("loop") || name.StartsWith("ram") || name.StartsWith("zram") || name.StartsWith("dm-") || name.StartsWith("sr")) continue;
            now[name] = ((Num.L(f[5]) ?? 0) * 512, (Num.L(f[9]) ?? 0) * 512, Num.L(f[12]) ?? 0);
        }
        long t = Stopwatch.GetTimestamp();
        double secs = _ticks == 0 ? 0 : (t - _ticks) / (double)Stopwatch.Frequency;
        var rows = new List<SensorSample>();
        foreach (var (n, c) in now)
        {
            string hw = "Disco " + n;
            float? rd = null, wr = null, busy = null;
            if (secs > 0.2 && _prev.TryGetValue(n, out var p))
            {
                rd = (float)((c.Item1 - p.Rd) / secs); wr = (float)((c.Item2 - p.Wr) / secs);
                busy = (float)Math.Clamp((c.Item3 - p.Ms) / secs / 10.0, 0, 100);
            }
            rows.Add(new SensorSample($"disk/{n}/read", hw, HwKind.Storage, SensorKind.Throughput, "Leitura", rd));
            rows.Add(new SensorSample($"disk/{n}/write", hw, HwKind.Storage, SensorKind.Throughput, "Escrita", wr));
            rows.Add(new SensorSample($"disk/{n}/busy", hw, HwKind.Storage, SensorKind.Load, "Atividade", busy));
        }
        if (secs > 0.2 || _ticks == 0) { _prev = now; _ticks = t; }
        return rows;
    }
}

/// <summary>Tráfego das interfaces de rede físicas (/proc/net/dev).</summary>
public sealed class NetSource : ISensorSource
{
    private Dictionary<string, (long Rx, long Tx)> _prev = new();
    private long _ticks;

    public IEnumerable<SensorSample> Read()
    {
        var now = new Dictionary<string, (long, long)>();
        foreach (var line in Sys.Lines("/proc/net/dev").Skip(2))
        {
            int i = line.IndexOf(':');
            if (i < 0) continue;
            string name = line[..i].Trim();
            // só interfaces com hardware (device/) ou sem fio; ignora lo, docker, bridges e túneis
            if (!Sys.DirExists($"/sys/class/net/{name}/device") && !Sys.DirExists($"/sys/class/net/{name}/wireless")) continue;
            var f = line[(i + 1)..].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (f.Length >= 9) now[name] = (Num.L(f[0]) ?? 0, Num.L(f[8]) ?? 0);
        }
        long t = Stopwatch.GetTimestamp();
        double secs = _ticks == 0 ? 0 : (t - _ticks) / (double)Stopwatch.Frequency;
        var rows = new List<SensorSample>();
        foreach (var (n, c) in now)
        {
            float? rx = null, tx = null;
            if (secs > 0.2 && _prev.TryGetValue(n, out var p)) { rx = (float)((c.Item1 - p.Rx) / secs); tx = (float)((c.Item2 - p.Tx) / secs); }
            rows.Add(new SensorSample($"net/{n}/down", "Rede " + n, HwKind.Network, SensorKind.Throughput, "Download", rx));
            rows.Add(new SensorSample($"net/{n}/up", "Rede " + n, HwKind.Network, SensorKind.Throughput, "Upload", tx));
        }
        if (secs > 0.2 || _ticks == 0) { _prev = now; _ticks = t; }
        return rows;
    }
}
