using Xunit;

namespace InfoPC.Tests;

public class LocaleTests
{
    [Fact]
    public void Num_reads_invariant_even_with_ptbr_culture()
    {
        Locale.Init();
        Assert.Equal(3.5, Num.D("3.5"));
        Assert.Equal(1056L, Num.L("1056"));
        Assert.Null(Num.D("3,5x"));
        Assert.Equal(3200, Num.Leading("3200 MT/s"));
        Assert.Equal(1.2, Num.Leading("1.2 V"));
        Assert.Equal(0xf8, Num.Hex("0xf8"));
    }

    [Fact]
    public void Fmt_uses_comma_decimals()
    {
        Locale.Init();
        Assert.Equal("1,5 GB", Fmt.Bytes(1.5 * 1024 * 1024 * 1024));
        Assert.Equal("500 GB", Fmt.Capacity(500_107_862_016));
        Assert.Equal("1 TB", Fmt.Capacity(1_000_204_886_016).Replace(",0", ""));
        Assert.Equal("1 d 2 h 3 min", Fmt.Uptime(new TimeSpan(1, 2, 3, 0)));
    }
}

public class CpuInfoTests
{
    [Fact]
    public void Intel_desktop_topology_caches_and_frequencies()
    {
        using var _ = Fx.Use("intel-desktop");
        var c = CpuInfo.Read();
        Assert.Equal("Intel(R) Core(TM) i3-10100 CPU @ 3.60GHz", c.Model);
        Assert.Equal("Intel", c.Vendor);
        Assert.Equal(1, c.Sockets);
        Assert.Equal(4, c.Cores);
        Assert.Equal(8, c.Threads);
        Assert.Equal(4300, c.MaxMhz);
        Assert.Equal(800, c.MinMhz);
        Assert.Equal(3600, c.BaseMhz);
        Assert.Equal("powersave", c.Governor);
        Assert.Equal("intel_pstate", c.ScalingDriver);
        Assert.True(c.Virtualization);
        Assert.Equal("Intel VT-x", c.VirtualizationKind);
        Assert.Equal("0xf8", c.Microcode);

        var l1 = c.Caches.Single(x => x.Name == "L1 dados");
        Assert.Equal(32 * 1024, l1.SizeBytes);
        Assert.Equal(4, l1.Instances);                                    // um por núcleo
        Assert.Equal(1, c.Caches.Single(x => x.Name == "L3").Instances);  // compartilhado
        Assert.Equal(6 * 1024 * 1024, c.Caches.Single(x => x.Name == "L3").SizeBytes);
        Assert.Equal(4, c.Vulnerabilities.Count);
        Assert.StartsWith("Vulnerable", c.Vulnerabilities["srbds"]);
    }

    [Fact]
    public void Amd_laptop_uses_svm_and_cpuinfo_when_no_cache_dir()
    {
        using var _ = Fx.Use("amd-laptop");
        var c = CpuInfo.Read();
        Assert.Equal("AMD", c.Vendor);
        Assert.Equal("AMD-V", c.VirtualizationKind);
        Assert.Equal(5100, c.MaxMhz);
        Assert.Equal(2, c.Threads);
        Assert.Equal(1, c.Cores);                      // os dois threads do fixture dividem o core 0
        Assert.Equal(2650, c.CurMhz);                  // média de 2000 e 3300 MHz
    }

    [Fact]
    public void Arm_without_model_name_falls_back_to_hardware()
    {
        using var _ = Fx.Use("raspberry");
        var c = CpuInfo.Read();
        Assert.Equal("BCM2711", c.Model);
        Assert.Equal("ARM", c.Vendor);
        Assert.Equal(4, c.Threads);
        Assert.Equal(4, c.Cores);
        Assert.False(c.Virtualization);
    }

    [Theory]
    [InlineData("32K", 32768)]
    [InlineData("1024K", 1048576)]
    [InlineData("32M", 33554432)]
    [InlineData("1G", 1073741824)]
    public void Cache_sizes_parse(string text, long bytes) => Assert.Equal(bytes, CpuInfo.SizeToBytes(text));
}

public class MemOsDmiTests
{
    [Fact]
    public void MemInfo_reads_available_and_swap()
    {
        using var _ = Fx.Use("intel-desktop");
        var m = MemInfo.Read()!;
        Assert.Equal(16318412, m.TotalKb);
        Assert.Equal(11234567, m.AvailableKb);
        Assert.Equal(16318412 - 11234567, m.UsedKb);
        Assert.InRange(m.UsedPct, 31, 32);
        Assert.Equal(0, m.SwapUsedKb);
        Assert.Single(MemInfo.Swaps());
        Assert.False(MemInfo.Swaps()[0].IsZram);
    }

    [Fact]
    public void MemInfo_estimates_available_on_old_kernels()
    {
        using var _ = Fx.Use("raspberry");
        var m = MemInfo.Read()!;
        Assert.Equal(200000 + 100000 + 1000000, m.AvailableKb) ;
    }

    [Fact]
    public void Os_release_kernel_uptime_and_boot_mode()
    {
        using var _ = Fx.Use("intel-desktop");
        var o = OsInfo.Read();
        Assert.Equal("Ubuntu 22.04.4 LTS", o.Name);
        Assert.Equal("ubuntu", o.Id);
        Assert.Equal("debian", o.IdLike);
        Assert.Equal("6.5.0-35-generic", o.Kernel);
        Assert.Equal("ofic-pc", o.Hostname);
        Assert.Equal(93784, (int)o.Uptime.TotalSeconds);
        Assert.Equal("UEFI", o.BootMode);
        Assert.Null(o.SecureBoot);   // sem efivars no fixture
    }

    [Fact]
    public void Os_release_with_quoted_and_bare_values()
    {
        using var _ = Fx.Use("amd-laptop");
        var o = OsInfo.Read();
        Assert.Equal("Fedora Linux 40 (Workstation Edition)", o.Name);
        Assert.Equal("40", o.VersionId);
    }

    [Fact]
    public void Dmi_reads_board_and_chassis()
    {
        using var _ = Fx.Use("intel-desktop");
        var d = Dmi.Read();
        Assert.Equal("Dell Inc.", d.SysVendor);
        Assert.Equal("0W0JH2", d.BoardName);
        Assert.Equal("Desktop", d.ChassisType);
        Assert.False(d.IsLaptop);
        Assert.True(d.HasData);

        using var __ = Fx.Use("amd-laptop");
        Assert.True(Dmi.Read().IsLaptop);
    }

    [Fact]
    public void System_tree_has_the_six_windows_categories()
    {
        using var _ = Fx.Use("intel-desktop");
        var tree = SystemInfoCollector.Collect(null, new List<GpuDevice>(), new List<PhysicalDisk>());
        Assert.Equal(new[] { "Sistema operacional", "Placa-mãe e BIOS", "Processador", "Memória RAM", "Placa de vídeo", "Armazenamento" },
                     tree.Select(n => n.Header).ToArray());
        var cpu = tree.Single(n => n.Header == "Processador");
        Assert.Contains(cpu.Children, c => c.Header == "Núcleos físicos" && c.Value == "4");
        Assert.Contains(cpu.Children, c => c.Header == "Cache L1 dados" && c.Value.Contains("× 4"));
        var os = tree.Single(n => n.Header == "Sistema operacional");
        Assert.Contains(os.Children, c => c.Header == "Nome" && c.Value == "Ubuntu 22.04.4 LTS");
        Assert.DoesNotContain(tree.SelectMany(Flatten), n => n.Header.Contains("Erro ao ler"));
    }

    private static IEnumerable<InfoNode> Flatten(InfoNode n) => new[] { n }.Concat(n.Children.SelectMany(Flatten));
}

public class RamModuleTests
{
    private const string Sample = @"# dmidecode 3.5
Getting SMBIOS data from sysfs.
SMBIOS 3.3.0 present.

Handle 0x0031, DMI type 16, 23 bytes
Physical Memory Array
	Location: System Board Or Motherboard
	Use: System Memory
	Error Correction Type: None
	Maximum Capacity: 128 GB
	Error Information Handle: 0x0030
	Number Of Devices: 4

Handle 0x0033, DMI type 17, 92 bytes
Memory Device
	Array Handle: 0x0031
	Total Width: 64 bits
	Data Width: 64 bits
	Size: 16 GB
	Form Factor: DIMM
	Locator: DIMM_A1
	Bank Locator: P0 CHANNEL A
	Type: DDR4
	Speed: 3200 MT/s
	Manufacturer: Corsair
	Serial Number: 00000000
	Part Number: CMK32GX4M2B3200C16
	Rank: 1
	Configured Memory Speed: 2666 MT/s
	Minimum Voltage: 1.2 V
	Maximum Voltage: 1.2 V
	Configured Voltage: 1.2 V

Handle 0x0034, DMI type 17, 92 bytes
Memory Device
	Size: No Module Installed
	Locator: DIMM_A2
	Bank Locator: P0 CHANNEL A
	Type: Unknown
	Speed: Unknown
	Manufacturer: Not Specified

Handle 0x0035, DMI type 17, 92 bytes
Memory Device
	Size: 16384 MB
	Form Factor: DIMM
	Locator: DIMM_B1
	Bank Locator: P0 CHANNEL B
	Type: DDR4
	Speed: 3200 MT/s
	Configured Memory Speed: 2666 MT/s
	Manufacturer: Corsair
	Part Number: CMK32GX4M2B3200C16
";

    [Fact]
    public void Parses_modules_array_and_skips_empty_slots()
    {
        Locale.Init();
        var r = RamModules.Parse(Sample);
        Assert.Equal(3, r.Modules.Count);
        Assert.Equal(2, r.Modules.Count(m => m.Populated));
        var m0 = r.Modules[0];
        Assert.Equal(16384, m0.SizeMb);
        Assert.Equal("DDR4", m0.Type);
        Assert.Equal(3200, m0.SpeedMts);
        Assert.Equal(2666, m0.ConfiguredMts);
        Assert.Equal(1.2, m0.CfgV);
        Assert.Equal("CMK32GX4M2B3200C16", m0.PartNumber);
        Assert.Equal("", m0.Serial);                       // "00000000" é preenchimento do fabricante
        Assert.Equal(16384, r.Modules[2].SizeMb);          // "16384 MB"
        Assert.Equal(128 * 1024, r.Array!.MaxCapacityMb);
        Assert.Equal(4, r.Array.Slots);
    }

    [Fact]
    public void System_tree_warns_when_memory_runs_below_rated_speed()
    {
        using var _ = Fx.Use("intel-desktop");
        var tree = SystemInfoCollector.Collect(RamModules.Parse(Sample), new List<GpuDevice>(), new List<PhysicalDisk>());
        var ram = tree.Single(n => n.Header == "Memória RAM");
        Assert.Equal("32 GB", ram.Children.Single(c => c.Header == "Total instalado").Value);
        var sum = ram.Children.Single(c => c.Header == "Resumo");
        Assert.Equal("2 de 4 slots", sum.Children.Single(c => c.Header == "Módulos instalados").Value);
        Assert.Contains("XMP", sum.Children.Single(c => c.Header == "Aviso").Value);
        Assert.Equal("2", sum.Children.Single(c => c.Header == "Canais ativos (estimado)").Value);
    }
}
