namespace InfoPC;

public enum GpuVendor { Nvidia, Amd, Intel, Other }

/// <summary>Uma GPU (dedicada ou integrada) vista pelo DRM/sysfs, com o handle da NVML quando é NVIDIA com driver proprietário.</summary>
public sealed class GpuDevice
{
    public int Index { get; init; }
    public GpuVendor Vendor { get; init; }
    public string Name { get; set; } = "";
    /// <summary>Nome na lista de sensores: o nome da placa, com o endereço PCI quando há duas iguais.</summary>
    public string Title { get; set; } = "";
    /// <summary>Códigos PCI em hexadecimal maiúsculo, sem 0x ("10DE", "2786").</summary>
    public string VendorId { get; init; } = "";
    public string DeviceId { get; init; } = "";
    public string SubVendorId { get; init; } = "";
    public string SubDeviceId { get; init; } = "";
    public string Revision { get; init; } = "";
    public string PciSlot { get; init; } = "";
    public string? Driver { get; init; }
    public string Card { get; init; } = "";
    /// <summary>Pasta do dispositivo, ex.: /sys/class/drm/card0/device.</summary>
    public string SysDir { get; init; } = "";
    public bool IsPrimary { get; init; }
    public bool IsIntegrated { get; set; }
    public IntPtr Nvml { get; set; }
    /// <summary>Endereço PCI no formato da NVML ("00000000:01:00.0"), aceito pelo nvidia-smi em -i.</summary>
    public string? NvmlBusId { get; set; }

    public bool HasNvml => Nvml != IntPtr.Zero;

    /// <summary>GPU emulada por hipervisor (Hyper-V, QEMU, VMware, VirtualBox, virtio).</summary>
    public bool IsVirtual => VirtualNames.ContainsKey(PciId);

    public static readonly Dictionary<string, string> VirtualNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["1414:0006"] = "Microsoft Hyper-V Video", ["1414:5353"] = "Microsoft Hyper-V Video", ["1234:1111"] = "QEMU Standard VGA",
        ["15AD:0405"] = "VMware SVGA II", ["80EE:BEEF"] = "VirtualBox Graphics Adapter", ["1AF4:1050"] = "Virtio GPU", ["1B36:0100"] = "QEMU QXL",
    };

    /// <summary>GPU dormindo (notebook híbrido): ler sensores a acordaria e gastaria bateria.</summary>
    public bool Suspended => Sys.Read(SysDir + "/power/runtime_status") == "suspended";

    public string PciId => $"{VendorId}:{DeviceId}";

    public override string ToString() => $"{Name} ({PciId})";
}

public static class GpuCollector
{
    private static List<GpuDevice>? _cache;

    /// <summary>Detecta as GPUs uma vez (a lista não muda com o PC ligado).</summary>
    public static List<GpuDevice> Detect(bool refresh = false)
    {
        if (!refresh && _cache != null) return _cache;
        try { _cache = DetectCore(); }
        catch { _cache = new List<GpuDevice>(); }
        return _cache;
    }

    public static GpuDevice? Main(IReadOnlyList<GpuDevice> list) =>
        list.FirstOrDefault(g => !g.IsIntegrated && g.Vendor != GpuVendor.Other) ?? list.FirstOrDefault(g => g.IsPrimary) ?? list.FirstOrDefault();

    private static List<GpuDevice> DetectCore()
    {
        List<(IntPtr Handle, NvmlLib.PciInfo Pci)> nvml = NvmlLib.Available ? NvmlLib.Devices() : new();
        var result = new List<GpuDevice>();
        foreach (var card in Sys.ListNatural("/sys/class/drm", "card"))
        {
            string dev = $"/sys/class/drm/{card}/device";
            string? ven = Sys.Read(dev + "/vendor"), did = Sys.Read(dev + "/device"), cls = Sys.Read(dev + "/class");
            if (ven == null || did == null) continue;                      // sem PCI (GPU de SoC): fora desta lista
            if (cls != null && !cls.StartsWith("0x03", StringComparison.Ordinal)) continue; // 0x03xxxx = controlador de vídeo
            string vid = Hex4(ven), dvid = Hex4(did);
            string slot = (Sys.RealPath(dev) ?? "").Split('/').LastOrDefault() ?? "";
            var vendor = vid switch { "10DE" => GpuVendor.Nvidia, "1002" => GpuVendor.Amd, "8086" => GpuVendor.Intel, _ => GpuVendor.Other };

            var match = nvml.FirstOrDefault(n => slot.Length > 0 && NvmlLib.SysfsSlot(n.Pci.BusId).Equals(slot, StringComparison.OrdinalIgnoreCase));
            string? name = match.Handle != IntPtr.Zero ? NvmlLib.Name(match.Handle) : null;
            if (string.IsNullOrWhiteSpace(name)) name = Sys.Read(dev + "/product_name");
            if (string.IsNullOrWhiteSpace(name) && GpuDevice.VirtualNames.TryGetValue($"{vid}:{dvid}", out var vn)) name = vn;
            if (string.IsNullOrWhiteSpace(name)) name = PciIds.GpuName(vid, dvid);

            var g = new GpuDevice
            {
                Index = result.Count, Vendor = vendor, Name = name,
                VendorId = vid, DeviceId = dvid, SubVendorId = Hex4(Sys.Read(dev + "/subsystem_vendor")), SubDeviceId = Hex4(Sys.Read(dev + "/subsystem_device")),
                Revision = (Sys.Read(dev + "/revision") ?? "").Replace("0x", "").ToUpperInvariant(),
                PciSlot = slot, Driver = Sys.LinkName(dev + "/driver"), Card = card, SysDir = dev,
                IsPrimary = Sys.Read(dev + "/boot_vga") == "1", Nvml = match.Handle, NvmlBusId = match.Handle != IntPtr.Zero ? match.Pci.BusId : null,
            };
            g.IsIntegrated = IsIntegrated(g);
            g.Title = g.Name;
            result.Add(g);
        }
        foreach (var grp in result.GroupBy(x => x.Name).Where(x => x.Count() > 1))
            foreach (var g in grp) g.Title = $"{g.Name} ({g.PciSlot})";
        return result;
    }

    /// <summary>GPU integrada: Intel (exceto Arc), ou AMD com pouca memória dedicada (APU).</summary>
    private static bool IsIntegrated(GpuDevice g)
    {
        if (g.Vendor == GpuVendor.Intel) return !g.Name.Contains("Arc", StringComparison.OrdinalIgnoreCase);
        if (g.Vendor == GpuVendor.Amd)
        {
            long vram = Sys.Long(g.SysDir + "/mem_info_vram_total") ?? 0;
            return (vram > 0 && vram <= 2L << 30) || g.Name.Contains("Radeon Graphics", StringComparison.OrdinalIgnoreCase);
        }
        return false;
    }

    private static string Hex4(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";
        s = s.Trim();
        if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) s = s[2..];
        return s.PadLeft(4, '0').ToUpperInvariant();
    }
}
