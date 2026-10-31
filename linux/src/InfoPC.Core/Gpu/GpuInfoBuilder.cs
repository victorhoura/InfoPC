namespace InfoPC;

/// <summary>Páginas "estilo GPU-Z": identificação, unidades, memória, clocks, barramento, energia, driver e tecnologias.</summary>
public sealed class GpuReportData
{
    public GpuDevice Device { get; init; } = null!;
    public string Subtitle { get; set; } = "";
    public List<InfoNode> Sections { get; } = new();
    public ulong VramBytes { get; set; }
}

public static class GpuInfoBuilder
{
    public static GpuReportData Build(GpuDevice g)
    {
        var data = new GpuReportData { Device = g };
        var spec = GpuSpecDb.Find(g.VendorId, g.DeviceId, g.Name);
        IntPtr h = g.Nvml;
        bool nv = g.Vendor == GpuVendor.Nvidia;
        bool asleep = g.Suspended; // não acorda a GPU de um notebook só para ler detalhes
        InfoNode Section(string title) { var n = new InfoNode { Header = title }; data.Sections.Add(n); return n; }

        // ---- IDENTIFICAÇÃO ----
        var id = Section("IDENTIFICAÇÃO");
        id.Add("Nome", g.Name);
        id.Add("Tipo", g.IsVirtual ? "Virtual (emulada pelo hipervisor da máquina virtual)" : g.IsIntegrated ? "Integrada (compartilha a memória do sistema)" : "Dedicada");
        id.Add("Chip", spec?.Chip ?? "—");
        id.Add("Arquitetura", spec?.Arch ?? "—");
        id.Add("Revisão", Fmt.Dash(g.Revision));
        id.Add("Fabricante da placa", g.SubVendorId.Length > 0 && g.SubVendorId != "0000" ? GpuSpecDb.SubvendorName(g.SubVendorId) : "—");
        id.Add("Device ID", $"{g.VendorId} {g.DeviceId}" + (g.SubVendorId.Length > 0 ? $" · {g.SubVendorId} {g.SubDeviceId}" : ""));
        id.Add("Endereço PCI", Fmt.Dash(g.PciSlot));
        id.Add("Processo", spec?.Process ?? "—");
        id.Add("Área do die", spec != null ? $"{spec.DieMm2} mm²" : "—");
        id.Add("Transistores", spec != null ? $"{spec.TransistorsBi:0.#} bilhões" : "—");
        id.Add("Lançamento", spec?.Release ?? "—");

        // ---- UNIDADES ----
        uint? cores = h != IntPtr.Zero && !asleep ? NvmlLib.Cores(h) : null;
        int shaders = (int)(cores ?? (uint)(spec?.Shaders ?? 0));
        var units = Section("UNIDADES DE PROCESSAMENTO");
        if (shaders > 0)
        {
            int perUnit = nv ? 128 : 64;
            units.Add(nv ? "CUDA cores" : "Stream processors", $"{shaders:N0}");
            units.Add(nv ? "SMs" : "Compute units", $"{shaders / perUnit}");
            if (nv && spec != null && (spec.Arch is "Blackwell" or "Ada Lovelace" or "Ampere"))
            {
                units.Add("Tensor cores", $"{shaders / perUnit * 4}");
                units.Add("RT cores", $"{shaders / perUnit}");
            }
            else if (!nv && spec != null && spec.Arch.StartsWith("RDNA", StringComparison.Ordinal))
                units.Add("Ray accelerators", $"{shaders / perUnit}");
        }
        if (spec != null)
        {
            units.Add("ROPs / TMUs", $"{spec.Rops} / {spec.Tmus}");
            units.Add("Pixel fillrate", $"{spec.Rops * spec.BoostMHz / 1000.0:0.0} GPixel/s");
            units.Add("Texture fillrate", $"{spec.Tmus * spec.BoostMHz / 1000.0:0.0} GTexel/s");
            units.Add("FP32 (teórico)", $"{(shaders > 0 ? shaders : spec.Shaders) * 2.0 * spec.BoostMHz / 1e6:0.00} TFLOPS");
        }
        if (units.Children.Count == 0) units.Add("Sem dados", "GPU fora do banco de especificações");

        // ---- MEMÓRIA ----
        var mem = Section("MEMÓRIA");
        ulong vram = 0;
        if (h != IntPtr.Zero && !asleep && NvmlLib.MemoryInfo(h) is { } nm) vram = nm.Total;
        if (vram == 0 && Sys.Long(g.SysDir + "/mem_info_vram_total") is { } sv and > 0) vram = (ulong)sv;
        data.VramBytes = vram;
        bool igpu = g.IsIntegrated || spec?.MemType.StartsWith("Memória do sistema") == true;
        if (vram == 0) mem.Add("Tamanho", igpu ? "Compartilhada com a RAM do sistema" : "—");
        else if (igpu || vram < 1UL << 30) mem.Add(igpu ? "Reservada (dedicada)" : "Tamanho", Fmt.Bytes(vram));
        else
        {
            // o driver reserva um pouco: 12227 MiB reportados = placa de 12 GB (como mostra o GPU-Z)
            double gib = vram / (double)(1UL << 30);
            mem.Add("Tamanho", $"{Math.Ceiling(gib - 0.15):0} GB ({vram >> 20:N0} MB utilizáveis)");
        }
        if (Sys.Long(g.SysDir + "/mem_info_gtt_total") is { } gtt and > 0) mem.Add("Compartilhada do sistema (GTT)", Fmt.Bytes(gtt));
        mem.Add("Tipo", spec?.MemType ?? "—");
        if (Sys.Read(g.SysDir + "/mem_info_vram_vendor") is { Length: > 0 } mv) mem.Add("Fabricante da memória", mv);
        uint? bus = h != IntPtr.Zero && !asleep ? NvmlLib.BusWidth(h) : null;
        uint? memClock = h != IntPtr.Zero && !asleep ? NvmlLib.MaxClock(h, NvmlLib.ClockMem) : null;
        if (memClock == null && g.Vendor == GpuVendor.Amd && GpuSource.DpmMax(g.SysDir + "/pp_dpm_mclk") is { } amc) memClock = (uint)amc;
        if (bus is uint b) mem.Add("Barramento", $"{b} bits");
        if (memClock is uint mc && nv)
        {
            // a NVML reporta metade da taxa efetiva (GDDR6/6X/7): 14001 MHz → 28 Gbps
            double gbps = mc * 2 / 1000.0;
            mem.Add("Clock da memória", $"{mc} MHz ({gbps:0.0} Gbps efetivos)");
            if (bus is uint bw) mem.Add("Largura de banda", $"{gbps * bw / 8:0.0} GB/s");
        }
        else if (memClock is uint amdMc) mem.Add("Clock máximo da memória", $"{amdMc} MHz");
        if (h != IntPtr.Zero && !asleep && NvmlLib.Bar1(h) is { } bar)
            mem.Add("Resizable BAR", bar.Total > 256UL * 1024 * 1024 ? $"Ativado (BAR1 {Fmt.Bytes(bar.Total)})" : "Desativado");

        // ---- CLOCKS ----
        var clk = Section("CLOCKS");
        if (spec != null)
        {
            clk.Add("Base (referência)", $"{spec.BaseMHz} MHz");
            clk.Add("Boost (referência)", $"{spec.BoostMHz} MHz");
        }
        if (h != IntPtr.Zero && !asleep)
        {
            if (NvmlLib.MaxClock(h, NvmlLib.ClockGraphics) is uint gc) clk.Add("Máximo (curva V/F)", $"{gc} MHz");
            if (NvmlLib.MaxClock(h, NvmlLib.ClockVideo) is uint vc) clk.Add("Video engine máx.", $"{vc} MHz");
        }
        else if (g.Vendor == GpuVendor.Amd && GpuSource.DpmMax(g.SysDir + "/pp_dpm_sclk") is { } asc) clk.Add("Máximo do núcleo (DPM)", $"{asc:0} MHz");
        else if (g.Vendor == GpuVendor.Intel)
        {
            double? max = Sys.Double($"/sys/class/drm/{g.Card}/gt_max_freq_mhz") ?? Sys.Double($"/sys/class/drm/{g.Card}/gt/gt0/rps_max_freq_mhz");
            double? rp0 = Sys.Double($"/sys/class/drm/{g.Card}/gt_RP0_freq_mhz") ?? Sys.Double($"/sys/class/drm/{g.Card}/gt/gt0/rps_RP0_freq_mhz");
            if (rp0 != null) clk.Add("Máximo do chip (RP0)", $"{rp0:0} MHz");
            if (max != null) clk.Add("Máximo configurado", $"{max:0} MHz");
        }
        if (clk.Children.Count == 0) clk.Add("Sem dados", "—");

        // ---- BARRAMENTO ----
        var pcie = Section("BARRAMENTO");
        uint? maxGen = h != IntPtr.Zero && !asleep ? NvmlLib.MaxPcieGen(h) : GenFromSpeed(Sys.Read(g.SysDir + "/max_link_speed"));
        uint? maxW = h != IntPtr.Zero && !asleep ? NvmlLib.MaxPcieWidth(h) : (uint?)Sys.Long(g.SysDir + "/max_link_width");
        uint? curGen = h != IntPtr.Zero && !asleep ? NvmlLib.CurPcieGen(h) : GenFromSpeed(Sys.Read(g.SysDir + "/current_link_speed"));
        uint? curW = h != IntPtr.Zero && !asleep ? NvmlLib.CurPcieWidth(h) : (uint?)Sys.Long(g.SysDir + "/current_link_width");
        if (maxGen is uint mg && maxW is uint mw) pcie.Add("Interface", $"{PcieName(mg)} x{mw}");
        if (curGen is uint cg && curW is uint cw)
            pcie.Add("Em uso agora", $"{PcieName(cg)} x{cw}" + (maxGen > curGen ? " (economia de energia em repouso)" : ""));
        if (g.PciSlot.Length > 0) pcie.Add("Localização", g.PciSlot);
        if (pcie.Children.Count == 0) pcie.Add("Sem dados", asleep ? "GPU em repouso (suspensa pelo sistema)" : "—");

        // ---- ENERGIA ----
        Hwmon? hw = FindHwmon(g);
        if (h != IntPtr.Zero || spec?.TdpW > 0 || hw?.Cap != null)
        {
            var pwr = Section("ENERGIA");
            if (spec?.TdpW > 0) pwr.Add("TDP (referência)", $"{spec.TdpW} W");
            if (h != IntPtr.Zero && !asleep)
            {
                if (NvmlLib.PowerLimitMw(h) is uint lim) pwr.Add("Limite atual", $"{lim / 1000.0:0} W");
                if (NvmlLib.PowerDefaultMw(h) is uint def) pwr.Add("Limite padrão da placa", $"{def / 1000.0:0} W");
                if (NvmlLib.PowerConstraintsMw(h) is { } pc) pwr.Add("Faixa ajustável", $"{pc.Min / 1000.0:0} – {pc.Max / 1000.0:0} W");
            }
            else if (hw != null)
            {
                if (hw.Cap != null) pwr.Add("Limite atual", $"{hw.Cap / 1e6:0} W");
                if (hw.CapDefault != null) pwr.Add("Limite padrão da placa", $"{hw.CapDefault / 1e6:0} W");
                if (hw.CapMin != null && hw.CapMax != null) pwr.Add("Faixa ajustável", $"{hw.CapMin / 1e6:0} – {hw.CapMax / 1e6:0} W");
            }
        }

        // ---- DRIVER E BIOS ----
        var drv = Section("DRIVER E BIOS");
        drv.Add("Driver do kernel em uso", g.Driver ?? "nenhum (sem aceleração)");
        string? vbios = h != IntPtr.Zero && !asleep ? NvmlLib.Vbios(h) : Sys.Read(g.SysDir + "/vbios_version");
        drv.Add("BIOS (VBIOS)", vbios ?? "—");
        if (h != IntPtr.Zero && !asleep && NvmlLib.DriverVersion() is string nvVer) drv.Add("Versão do driver NVIDIA", nvVer);
        else if (nv && NvidiaProcVersion() is string pv) drv.Add("Versão do driver NVIDIA", pv);
        else if (g.Driver != null && Sys.Read($"/sys/module/{g.Driver}/version") is { Length: > 0 } mver) drv.Add("Versão do módulo", mver);
        if (h != IntPtr.Zero && !asleep)
        {
            if (NvmlLib.CudaVersion() is string cuda) drv.Add("Versão CUDA", cuda);
            if (NvmlLib.ComputeCapability(h) is string cc) drv.Add("Compute capability", cc);
        }
        if (nv && !g.HasNvml && g.Driver == "nouveau") drv.Add("Aviso", "Driver livre nouveau: sem sensores completos nem CUDA. Instale o driver proprietário na página Drivers.");

        // ---- TECNOLOGIAS ----
        var tech = Section("TECNOLOGIAS");
        var apis = new List<string>();
        if (nv && LibExists("libcuda.so.1")) apis.Add("CUDA");
        if (AnyFile("/etc/OpenCL/vendors", ".icd")) apis.Add("OpenCL");
        if (AnyFile("/usr/share/vulkan/icd.d", ".json") || AnyFile("/etc/vulkan/icd.d", ".json")) apis.Add("Vulkan");
        if (Sys.Exists("/dev/dri") && Sys.List("/dev/dri").Any(n => n.StartsWith("renderD", StringComparison.Ordinal))) apis.Add("OpenGL (render node)");
        if (LibExists("libva.so.2")) apis.Add("VA-API");
        if (LibExists("libvdpau.so.1")) apis.Add("VDPAU");
        if (nv && LibExists("libnvidia-encode.so.1")) apis.Add("NVENC");
        tech.Add("APIs", apis.Count > 0 ? string.Join(" · ", apis) : "—");
        if (spec != null && GpuSpecDb.ArchFeatures.TryGetValue(spec.Arch, out var feats)) tech.Add("Recursos da arquitetura", feats);

        string memLabel = spec?.MemType.StartsWith("GDDR") == true ? spec.MemType : "";
        data.Subtitle = string.Join("  ·  ", new[]
        {
            spec?.Chip, spec?.Arch, spec?.Process,
            vram >= 1UL << 30 && !igpu ? $"{Math.Ceiling(vram / (double)(1UL << 30) - 0.15):0} GB {memLabel}".Trim() : null,
            g.SubVendorId.Length > 0 && g.SubVendorId != "0000" ? GpuSpecDb.SubvendorName(g.SubVendorId) : null
        }.Where(s => !string.IsNullOrEmpty(s)));
        return data;
    }

    /// <summary>Leituras instantâneas da NVIDIA (P-state, PerfCap, consumo, encoder...) para o painel ao vivo.</summary>
    public static List<KeyValuePair<string, string>> LiveExtras(GpuDevice g)
    {
        var list = new List<KeyValuePair<string, string>>();
        if (!g.HasNvml || g.Suspended) return list;
        var h = g.Nvml;
        if (NvmlLib.PState(h) is int p) list.Add(new("Estado de desempenho", $"P{p}"));
        if (NvmlLib.ThrottleReasons(h) is ulong r) list.Add(new("PerfCap (limitador)", NvmlLib.PerfCap(r)));
        if (NvmlLib.PowerUsageMw(h) is uint pw && NvmlLib.PowerLimitMw(h) is uint lim && lim > 0)
            list.Add(new("Consumo / limite", $"{pw / 1000.0:0.0} W · {100.0 * pw / lim:0} %"));
        if (NvmlLib.Util(h) is { } u) list.Add(new("Controlador de memória", $"{u.Memory} %"));
        if (NvmlLib.EncoderUtil(h) is uint enc) list.Add(new("Encoder (NVENC)", $"{enc} %"));
        if (NvmlLib.DecoderUtil(h) is uint dec) list.Add(new("Decoder (NVDEC)", $"{dec} %"));
        if (NvmlLib.CurPcieGen(h) is uint gen && NvmlLib.CurPcieWidth(h) is uint w) list.Add(new("PCIe em uso", $"{PcieName(gen)} x{w}"));
        return list;
    }

    private sealed record Hwmon(double? Cap, double? CapDefault, double? CapMin, double? CapMax);

    private static Hwmon? FindHwmon(GpuDevice g)
    {
        foreach (var h in Sys.List(g.SysDir + "/hwmon"))
        {
            string d = $"{g.SysDir}/hwmon/{h}";
            var cap = Sys.Double(d + "/power1_cap");
            if (cap != null) return new Hwmon(cap, Sys.Double(d + "/power1_cap_default"), Sys.Double(d + "/power1_cap_min"), Sys.Double(d + "/power1_cap_max"));
        }
        return null;
    }

    /// <summary>"16.0 GT/s PCIe" → 4.</summary>
    public static uint? GenFromSpeed(string? s)
    {
        double? gt = Num.Leading(s);
        return gt switch { null => null, <= 2.5 => 1, <= 5 => 2, <= 8 => 3, <= 16 => 4, <= 32 => 5, _ => 6 };
    }

    public static string PcieName(uint gen) => gen switch
    {
        1 => "PCIe 1.1", 2 => "PCIe 2.0", 3 => "PCIe 3.0", 4 => "PCIe 4.0", 5 => "PCIe 5.0", 6 => "PCIe 6.0", _ => $"PCIe gen {gen}"
    };

    private static string? NvidiaProcVersion()
    {
        // "NVRM version: NVIDIA UNIX x86_64 Kernel Module  550.54.14  Thu Feb 22 ..."
        foreach (var line in Sys.Lines("/proc/driver/nvidia/version"))
        {
            int i = line.IndexOf("Kernel Module", StringComparison.Ordinal);
            if (i < 0) continue;
            var parts = line[(i + 13)..].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length > 0) return parts[0];
        }
        return null;
    }

    private static bool AnyFile(string dir, string ext) => Sys.List(dir).Any(n => n.EndsWith(ext, StringComparison.Ordinal));

    private static readonly string[] LibDirs =
    {
        "/usr/lib", "/usr/lib64", "/lib", "/lib64", "/usr/lib/x86_64-linux-gnu", "/usr/lib/aarch64-linux-gnu", "/lib/x86_64-linux-gnu", "/lib/aarch64-linux-gnu",
        "/run/opengl-driver/lib", "/usr/lib/wsl/lib"
    };

    private static bool LibExists(string name) => LibDirs.Any(d => Sys.FileExists($"{d}/{name}"));
}
