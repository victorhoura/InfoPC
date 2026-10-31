namespace InfoPC;

public sealed record CacheLevel(string Name, long SizeBytes, int Instances)
{
    public string Text => Instances > 1 ? $"{Fmt.Bytes(SizeBytes)} × {Instances}" : Fmt.Bytes(SizeBytes);
}

public sealed record CpuDetails(
    string Model, string Vendor, string Architecture, int Sockets, int Cores, int Threads,
    double? MaxMhz, double? MinMhz, double? BaseMhz, double? CurMhz, string? Governor, string? ScalingDriver, string? Epp,
    List<CacheLevel> Caches, string Family, string ModelId, string Stepping, string? Microcode,
    bool Virtualization, string? VirtualizationKind, bool KvmAvailable, string[] Flags,
    Dictionary<string, string> Vulnerabilities);

/// <summary>Processador lido de /proc/cpuinfo e /sys/devices/system/cpu (funciona em x86 e ARM, com e sem cpufreq).</summary>
public static class CpuInfo
{
    private const string CpuDir = "/sys/devices/system/cpu";

    /// <summary>Blocos de /proc/cpuinfo (um por processador lógico), em pares chave/valor.</summary>
    public static List<Dictionary<string, string>> ParseCpuinfo(IEnumerable<string> lines)
    {
        var blocks = new List<Dictionary<string, string>>();
        var cur = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                if (cur.Count > 0) { blocks.Add(cur); cur = new Dictionary<string, string>(StringComparer.Ordinal); }
                continue;
            }
            int i = line.IndexOf(':');
            if (i < 0) continue;
            string k = line[..i].Trim(), v = line[(i + 1)..].Trim();
            cur.TryAdd(k, v);
        }
        if (cur.Count > 0) blocks.Add(cur);
        return blocks;
    }

    public static CpuDetails Read()
    {
        var all = ParseCpuinfo(Sys.Lines("/proc/cpuinfo"));
        var blocks = all.Where(b => b.ContainsKey("processor")).ToList();
        var first = blocks.FirstOrDefault() ?? new Dictionary<string, string>();
        // em ARM, "Hardware", "Model" e "Serial" vêm num bloco final, sem a chave "processor"
        var trailer = all.Where(b => !b.ContainsKey("processor")).SelectMany(b => b).GroupBy(kv => kv.Key).ToDictionary(g => g.Key, g => g.First().Value);
        string V(string k) => first.TryGetValue(k, out var v) ? v : trailer.TryGetValue(k, out var t) ? t : "";

        int threads = Math.Max(blocks.Count, 1);

        // Nome: x86 traz "model name"; ARM não traz (usa o lscpu ou o device-tree)
        string model = V("model name");
        if (model.Length == 0) model = LscpuField("Model name") ?? "";      // ARM: "Cortex-A72"
        if (model.Length == 0) model = V("Hardware");                        // ARM: o SoC, "BCM2711"
        if (model.Length == 0) model = (Sys.Read("/proc/device-tree/model") ?? "").TrimEnd('\0');
        if (model.Length == 0) model = V("CPU implementer").Length > 0 ? $"ARM (implementador {V("CPU implementer")}, modelo {V("CPU part")})" : "Processador desconhecido";

        string vendor = V("vendor_id");
        if (vendor.Length == 0) vendor = V("CPU implementer") is { Length: > 0 } imp ? ArmImplementer(imp) : "";
        vendor = vendor switch { "GenuineIntel" => "Intel", "AuthenticAMD" => "AMD", "HygonGenuine" => "Hygon", var o => o };

        // Topologia: sysfs é mais confiável que o cpuinfo (vale também para ARM e para AMD com SMT)
        var pairs = new HashSet<(string, string)>();
        var pkgs = new HashSet<string>();
        foreach (var c in Sys.ListNatural(CpuDir, "cpu"))
        {
            string? pkg = Sys.Read($"{CpuDir}/{c}/topology/physical_package_id");
            string? core = Sys.Read($"{CpuDir}/{c}/topology/core_id");
            if (pkg == null || core == null) continue;
            pkgs.Add(pkg);
            pairs.Add((pkg, core));
        }
        int sockets = pkgs.Count, cores = pairs.Count;
        if (cores == 0)
        {
            var ids = blocks.Select(b => (b.GetValueOrDefault("physical id", "0"), b.GetValueOrDefault("core id", ""))).Where(t => t.Item2 != "").Distinct().ToList();
            sockets = blocks.Select(b => b.GetValueOrDefault("physical id", "0")).Distinct().Count();
            cores = ids.Count > 0 ? ids.Count : (int)(Num.L(V("cpu cores")) ?? threads) * Math.Max(sockets, 1);
        }
        sockets = Math.Max(sockets, 1);
        cores = Math.Clamp(cores, 1, threads);

        // Frequências (kHz no cpufreq)
        double? max = null, min = null, bse = null; var curs = new List<double>();
        foreach (var c in Sys.ListNatural(CpuDir, "cpu"))
        {
            double? mx = Sys.Double($"{CpuDir}/{c}/cpufreq/cpuinfo_max_freq"), mn = Sys.Double($"{CpuDir}/{c}/cpufreq/cpuinfo_min_freq");
            double? cu = Sys.Double($"{CpuDir}/{c}/cpufreq/scaling_cur_freq");
            if (mx != null) max = Math.Max(max ?? 0, mx.Value / 1000);
            if (mn != null) min = min == null ? mn.Value / 1000 : Math.Min(min.Value, mn.Value / 1000);
            if (cu != null) curs.Add(cu.Value / 1000);
        }
        if (Sys.Double($"{CpuDir}/cpu0/cpufreq/base_frequency") is { } bf) bse = bf / 1000;
        double? cur = curs.Count > 0 ? curs.Average() : null;
        if (cur == null)
        {
            var mh = blocks.Select(b => Num.D(b.GetValueOrDefault("cpu MHz"))).Where(x => x != null).Select(x => x!.Value).ToList();
            if (mh.Count > 0) cur = mh.Average();
        }

        var flags = (V("flags").Length > 0 ? V("flags") : V("Features")).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        bool vmx = flags.Contains("vmx"), svm = flags.Contains("svm");

        return new CpuDetails(
            model, vendor, System.Runtime.InteropServices.RuntimeInformation.OSArchitecture == System.Runtime.InteropServices.Architecture.Arm64 ? "ARM64" : "x86-64",
            sockets, cores, threads, max, min, bse, cur,
            Sys.Read($"{CpuDir}/cpu0/cpufreq/scaling_governor"), Sys.Read($"{CpuDir}/cpu0/cpufreq/scaling_driver"),
            Sys.Read($"{CpuDir}/cpu0/cpufreq/energy_performance_preference"),
            Caches(), V("cpu family"), V("model"), V("stepping"), V("microcode") is { Length: > 0 } mc ? mc : null,
            vmx || svm, vmx ? "Intel VT-x" : svm ? "AMD-V" : null, Sys.Exists("/dev/kvm"), flags, Vulnerabilities());
    }

    /// <summary>Caches por nível, somando as instâncias (um L2 por núcleo, um L3 compartilhado...).</summary>
    public static List<CacheLevel> Caches()
    {
        var byKey = new Dictionary<string, (string Name, long Size, HashSet<string> Shared)>();
        foreach (var c in Sys.ListNatural(CpuDir, "cpu"))
        {
            foreach (var idx in Sys.ListNatural($"{CpuDir}/{c}/cache", "index"))
            {
                string p = $"{CpuDir}/{c}/cache/{idx}";
                string? level = Sys.Read(p + "/level"), type = Sys.Read(p + "/type");
                long? size = SizeToBytes(Sys.Read(p + "/size"));
                if (level == null || type == null || size == null) continue;
                string name = type switch { "Data" => $"L{level} dados", "Instruction" => $"L{level} instruções", _ => $"L{level}" };
                string shared = Sys.Read(p + "/shared_cpu_list") ?? c;
                if (!byKey.TryGetValue(name, out var e)) e = (name, size.Value, new HashSet<string>());
                e.Shared.Add(shared);
                byKey[name] = e;
            }
        }
        return byKey.Values.OrderBy(v => v.Name, StringComparer.Ordinal).Select(v => new CacheLevel(v.Name, v.Size, v.Shared.Count)).ToList();
    }

    /// <summary>"32K", "1024K", "32M" → bytes.</summary>
    public static long? SizeToBytes(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        s = s.Trim().ToUpperInvariant();
        long mult = 1;
        if (s.EndsWith('K')) { mult = 1024; s = s[..^1]; }
        else if (s.EndsWith('M')) { mult = 1024 * 1024; s = s[..^1]; }
        else if (s.EndsWith('G')) { mult = 1024L * 1024 * 1024; s = s[..^1]; }
        return Num.L(s) * mult;
    }

    public static Dictionary<string, string> Vulnerabilities()
    {
        var d = new Dictionary<string, string>();
        foreach (var n in Sys.List($"{CpuDir}/vulnerabilities"))
            if (Sys.Read($"{CpuDir}/vulnerabilities/{n}") is { Length: > 0 } v) d[n] = v;
        return d;
    }

    private static string? LscpuField(string field)
    {
        var r = Shell.Run("lscpu", timeoutMs: 3000);
        if (!r.Ok) return null;
        foreach (var line in r.Out.Split('\n'))
        {
            int i = line.IndexOf(':');
            if (i > 0 && line[..i].Trim() == field) return line[(i + 1)..].Trim();
        }
        return null;
    }

    private static string ArmImplementer(string code) => code.ToLowerInvariant() switch
    {
        "0x41" => "ARM", "0x42" => "Broadcom", "0x43" => "Cavium", "0x46" => "Fujitsu", "0x48" => "HiSilicon", "0x4e" => "NVIDIA",
        "0x50" => "Applied Micro", "0x51" => "Qualcomm", "0x53" => "Samsung", "0x61" => "Apple", "0xc0" => "Ampere", _ => "ARM"
    };
}
