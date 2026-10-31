using Xunit;

namespace InfoPC.Tests;

public class SensorTests
{
    [Fact]
    public void Hwmon_intel_desktop_reads_units_and_roles()
    {
        using var _ = Fx.Use("intel-desktop");
        var hub = new SensorHub(new ISensorSource[] { new HwmonSource() });
        hub.Refresh();

        Assert.Equal(45f, hub.CpuTemperature);                                    // Package id 0 (prioridade 3) vence os núcleos
        var rows = hub.Rows.ToList();
        Assert.Contains(rows, r => r.Hardware == "Processador (Intel)" && r.Name == "Core 2" && r.RawValue == 45f);
        Assert.Equal("45 °C", rows.Single(r => r.Name == "Package id 0").Value);
        Assert.Equal(1200f, rows.Single(r => r.Kind == SensorKind.Fan && r.RawValue == 1200f).RawValue);
        Assert.Equal(1.056f, rows.Single(r => r.Kind == SensorKind.Voltage).RawValue!.Value, 3);   // mV → V
        Assert.Equal(50f, rows.Single(r => r.Kind == SensorKind.Control).RawValue!.Value, 0);      // pwm 128/255
        Assert.Contains(rows, r => r.Hardware == "NVMe · Samsung SSD 980 PRO 1TB" && r.Name == "Composite" && r.RawValue == 38.85f);
        Assert.Contains(rows, r => r.HwKind == HwKind.Board && r.Hardware.Contains("nct6798"));
    }

    [Fact]
    public void Hwmon_amd_picks_tdie_tctl_and_assigns_gpu_roles()
    {
        using var _ = Fx.Use("amd-laptop");
        var gpu = new GpuDevice { Index = 0, Vendor = GpuVendor.Amd, Name = "AMD Radeon 780M", Title = "AMD Radeon 780M", PciSlot = "0000:c4:00.0", VendorId = "1002", DeviceId = "15BF" };
        var hub = new SensorHub(new ISensorSource[] { new HwmonSource(new[] { gpu }) });
        hub.Refresh();
        Assert.Equal(62f, hub.CpuTemperature);          // só Tctl existe: prioridade 2
        // sem link simbólico no fixture o chip amdgpu não casa com a GPU: entra pelo nome do driver
        Assert.Contains(hub.Rows, r => r.Hardware == "Placa de vídeo (AMD)" && r.Name == "edge");
        Assert.Equal(1800f, hub.Rows.Single(r => r.Name == "sclk").RawValue);          // Hz → MHz
        Assert.Equal(15f, hub.Rows.Single(r => r.Kind == SensorKind.Power).RawValue);  // µW → W
    }

    [Fact]
    public void Cpu_load_between_two_snapshots()
    {
        var a = (Total: 1000L, Idle: 800L);
        var b = (Total: 1200L, Idle: 850L);
        Assert.Equal(75f, CpuSource.Load(a, b));        // 150 de 200 ocupados
        Assert.Null(CpuSource.Load(a, a));              // sem tempo decorrido
        Assert.Equal(0f, CpuSource.Load((0, 0), (100, 100)));
    }

    [Fact]
    public void Cpu_stat_parser_sums_busy_and_idle()
    {
        using var _ = Fx.Use("intel-desktop");
        var s = CpuSource.ReadStat();
        Assert.Equal(3, s.Count);                       // cpu, cpu0, cpu1
        Assert.Equal(1000 + 0 + 500 + 8000 + 100 + 0 + 20 + 0, s["cpu"].Total);
        Assert.Equal(8000 + 100, s["cpu"].Idle);        // idle + iowait
    }

    [Fact]
    public void Cpu_source_reports_clocks_from_cpufreq()
    {
        using var _ = Fx.Use("intel-desktop");
        var src = new CpuSource();
        var rows = src.Read().ToList();
        Assert.Contains(rows, r => r.Role == "cpu.clock" && Math.Abs(r.Value!.Value - 3950) < 1);   // média de 3600..4300
        Assert.Contains(rows, r => r.Role == "cpu.clockmax" && r.Value == 4300f);
        Assert.DoesNotContain(rows, r => r.Role == "cpu.load");   // a carga só existe a partir da segunda leitura
    }

    [Fact]
    public void Thermal_zone_duplicate_of_coretemp_is_skipped()
    {
        using var _ = Fx.Use("intel-desktop");
        var rows = new ThermalSource().Read().ToList();
        Assert.DoesNotContain(rows, r => r.Name == "x86_pkg_temp");
        Assert.Contains(rows, r => r.Name == "acpitz" && r.Value == 27.8f);
    }

    [Fact]
    public void Disk_io_ignores_loop_and_partitions()
    {
        using var _ = Fx.Use("intel-desktop");
        var rows = new DiskIoSource().Read().ToList();
        Assert.Contains(rows, r => r.Hardware == "Disco nvme0n1");
        Assert.DoesNotContain(rows, r => r.Hardware.Contains("loop"));
    }

    [Fact]
    public void Memory_source_has_percent_and_used()
    {
        using var _ = Fx.Use("intel-desktop");
        var hub = new SensorHub(new ISensorSource[] { new MemorySource() });
        hub.Refresh();
        Assert.InRange(hub.RamPercent!.Value, 31f, 32f);
        Assert.Equal("31,2 %", hub.Rows.Single(r => r.Name == "Memória").Value);
    }

    [Fact]
    public void Reading_keeps_min_and_max_and_failed_source_does_not_break_others()
    {
        var src = new FakeSource(new[] { 10f, 30f, 20f });
        var hub = new SensorHub(new ISensorSource[] { new ThrowingSource(), src });
        hub.Refresh(); hub.Refresh(); hub.Refresh();
        var r = hub.Rows.Single();
        Assert.Equal(20f, r.RawValue);
        Assert.Equal(10f, r.RawMin);
        Assert.Equal(30f, r.RawMax);
        Assert.Equal("30 °C", r.Max);
    }

    [Fact]
    public void Role_with_highest_priority_wins()
    {
        var hub = new SensorHub(new ISensorSource[] { new RoleSource() });
        hub.Refresh();
        Assert.Equal(70f, hub.CpuTemperature);
    }

    [Fact]
    public void Missing_temperature_explains_why()
    {
        using var _ = Fx.Use("raspberry");
        var hub = new SensorHub(new ISensorSource[] { new HwmonSource() });
        hub.Refresh();
        Assert.Equal(54f, hub.CpuTemperature);   // cpu_thermal (SoC ARM)
        using var __ = Fx.Use("amd-laptop");
        Sys.Root = Path.Combine(AppContext.BaseDirectory, "Fixtures", "nada");
        Directory.CreateDirectory(Sys.Root);
        var empty = new SensorHub(new ISensorSource[] { new HwmonSource() });
        empty.Refresh();
        Assert.Contains("máquina virtual", empty.DescribeCpuTempProblem());
    }

    private sealed class FakeSource(float[] values) : ISensorSource
    {
        private int _i;
        public IEnumerable<SensorSample> Read() { yield return new SensorSample("x", "HW", HwKind.Cpu, SensorKind.Temperature, "T", values[_i++ % values.Length]); }
    }

    private sealed class ThrowingSource : ISensorSource
    {
        public IEnumerable<SensorSample> Read() => throw new IOException("sem permissão");
    }

    private sealed class RoleSource : ISensorSource
    {
        public IEnumerable<SensorSample> Read()
        {
            yield return new SensorSample("a", "HW", HwKind.Cpu, SensorKind.Temperature, "baixa", 99f, "cpu.temp", 1);
            yield return new SensorSample("b", "HW", HwKind.Cpu, SensorKind.Temperature, "alta", 70f, "cpu.temp", 3);
            yield return new SensorSample("c", "HW", HwKind.Cpu, SensorKind.Temperature, "media", 80f, "cpu.temp", 2);
        }
    }
}

public class StorageTests
{
    [Fact]
    public void Disks_are_read_from_sysfs_with_bus_and_type()
    {
        using var _ = Fx.Use("intel-desktop");
        var disks = DiskCollector.Disks();
        Assert.Equal(new[] { "nvme0n1", "sda" }, disks.Select(d => d.Name).OrderByDescending(n => n.StartsWith("nvme")).ToArray());
        var nvme = disks.Single(d => d.Name == "nvme0n1");
        Assert.Equal("NVMe", nvme.Bus);
        Assert.Equal("SSD NVMe", nvme.MediaType);
        Assert.Equal(1953525168L * 512, nvme.SizeBytes);
        Assert.Equal("Samsung SSD 980 PRO 1TB", nvme.Model);
        Assert.Equal(38.85, nvme.TempC is null ? 0 : Math.Round(nvme.TempC.Value, 2));
        var hdd = disks.Single(d => d.Name == "sda");
        Assert.True(hdd.Rotational);
        Assert.Equal("HDD", hdd.MediaType);
        Assert.Equal("WDC WD40EZRZ-00G", hdd.Model);
        Assert.Equal("ATA", hdd.Vendor);
    }

    [Fact]
    public void MountInfo_keeps_real_filesystems_and_decodes_escapes()
    {
        using var _ = Fx.Use("intel-desktop");
        var mounts = DiskCollector.ParseMountInfo(Sys.Lines("/proc/self/mountinfo"), query: false);
        var points = mounts.Select(m => m.MountPoint).ToList();
        Assert.Contains("/", points);
        Assert.Contains("/boot/efi", points);
        Assert.Contains("/mnt/Dados Grandes", points);           // \040 → espaço
        Assert.DoesNotContain("/proc", points);
        Assert.DoesNotContain("/sys", points);
        Assert.DoesNotContain(points, p => p.StartsWith("/snap"));
        Assert.DoesNotContain(points, p => p.StartsWith("/run"));
        Assert.Equal(1, points.Count(p => p == "/" || p == "/home"));   // /home divide o dispositivo com /: fica o mais curto
        Assert.Equal("vfat", mounts.Single(m => m.MountPoint == "/boot/efi").FsType);
    }

    [Fact]
    public void DiskOf_climbs_partition_to_disk_when_links_exist()
    {
        if (!Fx.SymlinksWork() || !OperatingSystem.IsLinux()) return;   // roda no CI Linux
        string root = Path.Combine(Path.GetTempPath(), "infopc-disk-" + Guid.NewGuid().ToString("N"));
        try
        {
            string real = Path.Combine(root, "sys/devices/pci0000:00/nvme/block/nvme0n1/nvme0n1p2");
            Directory.CreateDirectory(real);
            File.WriteAllText(Path.Combine(real, "partition"), "2");
            Directory.CreateDirectory(Path.Combine(root, "sys/dev/block"));
            Directory.CreateSymbolicLink(Path.Combine(root, "sys/dev/block/259:2"), "../../devices/pci0000:00/nvme/block/nvme0n1/nvme0n1p2");
            Directory.CreateDirectory(Path.Combine(root, "sys/class/block"));
            Directory.CreateSymbolicLink(Path.Combine(root, "sys/class/block/nvme0n1p2"), "../../devices/pci0000:00/nvme/block/nvme0n1/nvme0n1p2");
            Sys.Root = root;
            Assert.Equal("nvme0n1", DiskCollector.DiskOf("259:2"));
        }
        finally { Sys.Root = "/"; try { Directory.Delete(root, true); } catch { } }
    }
}

public class SmartTests
{
    private const string Ata = """
    {"smartctl":{"version":[7,3]},"device":{"name":"/dev/sda","protocol":"ATA"},"model_name":"WDC WD40EZRZ","serial_number":"WD-XYZ",
     "firmware_version":"80.00A80","user_capacity":{"bytes":4000787030016},"rotation_rate":5400,"smart_status":{"passed":true},
     "temperature":{"current":36},"power_on_time":{"hours":23456},"power_cycle_count":812,
     "ata_smart_attributes":{"table":[
       {"id":5,"name":"Reallocated_Sector_Ct","value":200,"worst":200,"thresh":140,"raw":{"value":0}},
       {"id":197,"name":"Current_Pending_Sector","value":200,"worst":200,"thresh":0,"raw":{"value":0}},
       {"id":198,"name":"Offline_Uncorrectable","value":200,"worst":200,"thresh":0,"raw":{"value":0}},
       {"id":199,"name":"UDMA_CRC_Error_Count","value":200,"worst":200,"thresh":0,"raw":{"value":0}}]}}
    """;

    private const string Nvme = """
    {"device":{"name":"/dev/nvme0n1","protocol":"NVMe"},"model_name":"Samsung SSD 980 PRO 1TB","serial_number":"S5GXNX0","firmware_version":"5B2QGXA7",
     "smart_status":{"passed":true},"temperature":{"current":38},
     "nvme_smart_health_information_log":{"critical_warning":0,"temperature":38,"available_spare":100,"available_spare_threshold":10,
       "percentage_used":3,"data_units_read":20000000,"data_units_written":15000000,"power_cycles":300,"power_on_hours":4200,
       "unsafe_shutdowns":12,"media_errors":0,"num_err_log_entries":0}}
    """;

    [Fact]
    public void Ata_json_is_parsed_and_judged_healthy()
    {
        Locale.Init();
        var s = SmartReader.Parse(Ata)!;
        Assert.Equal("ATA", s.Protocol);
        Assert.Equal(23456, s.PowerOnHours);
        Assert.Equal(36, s.TempC);
        Assert.Equal(4, s.Attrs.Count);
        var disk = new PhysicalDisk { Name = "sda", Model = "WDC WD40EZRZ", Bus = "SATA", Rotational = true, SizeBytes = 4000787030016 };
        var card = DiskHealthEvaluator.Card(disk, s);
        Assert.Equal(DiskVerdict.Good, card.Verdict);
        Assert.Equal("SAUDÁVEL", card.VerdictText);
        Assert.Contains(card.Metrics, m => m.Key == "Tempo ligado" && m.Value.StartsWith("23.456 h"));
    }

    [Fact]
    public void Ata_with_pending_and_uncorrectable_sectors_is_critical()
    {
        var s = SmartReader.Parse(Ata.Replace("\"id\":198,\"name\":\"Offline_Uncorrectable\",\"value\":200,\"worst\":200,\"thresh\":0,\"raw\":{\"value\":0}",
                                              "\"id\":198,\"name\":\"Offline_Uncorrectable\",\"value\":100,\"worst\":100,\"thresh\":0,\"raw\":{\"value\":8}"))!;
        var card = DiskHealthEvaluator.Card(new PhysicalDisk { Name = "sda", Rotational = true }, s);
        Assert.Equal(DiskVerdict.Critical, card.Verdict);
        Assert.Contains("incorrigível", card.Reasons);
        Assert.Contains("backup", card.Reasons);
    }

    [Fact]
    public void Failed_smart_status_is_critical()
    {
        var s = SmartReader.Parse(Ata.Replace("\"passed\":true", "\"passed\":false"))!;
        Assert.Equal(DiskVerdict.Critical, DiskHealthEvaluator.Card(new PhysicalDisk { Name = "sda" }, s).Verdict);
    }

    [Fact]
    public void Nvme_json_is_parsed_with_written_bytes()
    {
        Locale.Init();
        var s = SmartReader.Parse(Nvme)!;
        Assert.True(s.IsNvme);
        Assert.Equal(3, s.PercentUsed);
        Assert.Equal(15000000L * 512000.0, s.WrittenBytes);
        var card = DiskHealthEvaluator.Card(new PhysicalDisk { Name = "nvme0n1", Bus = "NVMe" }, s);
        Assert.Equal(DiskVerdict.Good, card.Verdict);
        Assert.Contains(card.Metrics, m => m.Key == "Total gravado" && m.Value == "7,68 TB");
    }

    [Fact]
    public void Nvme_worn_out_is_warning_then_critical()
    {
        var warn = SmartReader.Parse(Nvme.Replace("\"percentage_used\":3", "\"percentage_used\":92"))!;
        Assert.Equal(DiskVerdict.Warning, DiskHealthEvaluator.Card(new PhysicalDisk { Name = "nvme0n1" }, warn).Verdict);
        var dead = SmartReader.Parse(Nvme.Replace("\"critical_warning\":0", "\"critical_warning\":4"))!;
        Assert.Equal(DiskVerdict.Critical, DiskHealthEvaluator.Card(new PhysicalDisk { Name = "nvme0n1" }, dead).Verdict);
    }

    [Fact]
    public void Smartctl_error_message_becomes_card_text_not_a_crash()
    {
        var s = SmartReader.Parse("""{"smartctl":{"messages":[{"string":"Permission denied","severity":"error"}],"exit_status":2}}""", "/dev/sda")!;
        Assert.Contains("Permission denied", s.Error);
        var card = DiskHealthEvaluator.Card(new PhysicalDisk { Name = "sda" }, s);
        Assert.Equal(DiskVerdict.Unknown, card.Verdict);
        Assert.Contains("Permission denied", card.Reasons);
    }

    [Fact]
    public void Batch_output_with_markers_is_split_per_disk()
    {
        string batch = "@@INFOPC /dev/sda\n" + Ata + "\n@@INFOPC /dev/nvme0n1\n" + Nvme + "\n";
        var list = SmartReader.ParseBatch(batch);
        Assert.Equal(2, list.Count);
        var cards = DiskHealthEvaluator.Cards(new[] { new PhysicalDisk { Name = "nvme0n1", Bus = "NVMe" }, new PhysicalDisk { Name = "sda" } }, list);
        Assert.All(cards, c => Assert.Equal(DiskVerdict.Good, c.Verdict));
    }

    [Fact]
    public void Without_smart_the_card_explains_how_to_read_it()
    {
        var card = DiskHealthEvaluator.Card(new PhysicalDisk { Name = "sda", Model = "X" }, null);
        Assert.Equal(DiskVerdict.Unknown, card.Verdict);
        Assert.Equal("SEM DADOS", card.VerdictText);
        Assert.Contains("LER S.M.A.R.T.", card.Reasons);
    }

    [Fact]
    public void ReadAll_without_smartctl_says_what_to_install()
    {
        using var _ = Fx.Use("intel-desktop");
        var (infos, res) = SmartReader.ReadAll(new[] { new PhysicalDisk { Name = "sda" } });
        Assert.Empty(infos);
        Assert.False(res.Ok);
        Assert.Contains("smartmontools", res.Message);
    }
}

public class GpuTests
{
    [Fact]
    public void Pci_ids_lookup_and_marketing_names()
    {
        using var _ = Fx.Use("amd-laptop");
        Assert.Equal("NVIDIA GeForce RTX 4070", PciIds.GpuName("10DE", "2786"));
        Assert.Equal("AMD Radeon RX 7600", PciIds.GpuName("1002", "7480"));             // primeira das variantes
        Assert.Equal("AMD Radeon Graphics (Phoenix1)", PciIds.GpuName("1002", "15BF"));  // APU sem colchetes
        Assert.Equal("Intel UHD Graphics 770", PciIds.GpuName("8086", "4690"));
        Assert.Equal("NVIDIA GPU 10DE:FFFF", PciIds.GpuName("10DE", "FFFF"));            // dispositivo desconhecido
    }

    [Fact]
    public void Dpm_clock_parsing()
    {
        using var _ = Fx.Use("amd-laptop");
        Assert.Equal(1800, GpuSource.DpmCurrent("/sys/class/drm/card1/device/pp_dpm_sclk"));
        Assert.Equal(2700, GpuSource.DpmMax("/sys/class/drm/card1/device/pp_dpm_sclk"));
        Assert.Equal(1000, GpuSource.DpmMax("/sys/class/drm/card1/device/pp_dpm_mclk"));
    }

    [Fact]
    public void Pcie_generation_from_link_speed()
    {
        Assert.Equal(1u, GpuInfoBuilder.GenFromSpeed("2.5 GT/s PCIe"));
        Assert.Equal(3u, GpuInfoBuilder.GenFromSpeed("8.0 GT/s PCIe"));
        Assert.Equal(4u, GpuInfoBuilder.GenFromSpeed("16.0 GT/s PCIe"));
        Assert.Equal(5u, GpuInfoBuilder.GenFromSpeed("32.0 GT/s PCIe"));
        Assert.Null(GpuInfoBuilder.GenFromSpeed(null));
        Assert.Equal("PCIe 4.0", GpuInfoBuilder.PcieName(4));
    }

    [Fact]
    public void Nvml_bus_id_matches_sysfs_slot()
    {
        Assert.Equal("0000:01:00.0", NvmlLib.SysfsSlot("00000000:01:00.0"));
        Assert.Equal("0000:c4:00.0", NvmlLib.SysfsSlot("00000000:C4:00.0"));
    }

    [Fact]
    public void Amd_gpu_sections_use_sysfs_and_spec_db()
    {
        using var _ = Fx.Use("amd-laptop");
        var g = new GpuDevice
        {
            Index = 0, Vendor = GpuVendor.Amd, Name = "AMD Radeon RX 7600", Title = "AMD Radeon RX 7600", VendorId = "1002", DeviceId = "7480",
            SubVendorId = "17AA", SubDeviceId = "3C0E", PciSlot = "0000:c4:00.0", Driver = "amdgpu", Card = "card1", SysDir = "/sys/class/drm/card1/device",
        };
        var data = GpuInfoBuilder.Build(g);
        Assert.Equal(new[] { "IDENTIFICAÇÃO", "UNIDADES DE PROCESSAMENTO", "MEMÓRIA", "CLOCKS", "BARRAMENTO", "ENERGIA", "DRIVER E BIOS", "TECNOLOGIAS" },
                     data.Sections.Select(s => s.Header).ToArray());
        var id = data.Sections[0];
        Assert.Equal("Navi 33", id.Children.Single(c => c.Header == "Chip").Value);
        Assert.Equal("Lenovo", id.Children.Single(c => c.Header == "Fabricante da placa").Value);
        Assert.Contains("PCIe 4.0 x16", data.Sections.Single(s => s.Header == "BARRAMENTO").Children.Single(c => c.Header == "Interface").Value);
        Assert.Equal(536870912UL, data.VramBytes);
        Assert.Contains("amdgpu", data.Sections.Single(s => s.Header == "DRIVER E BIOS").Children[0].Value);
        Assert.Contains("RDNA 3", data.Subtitle);
    }

    [Fact]
    public void Amd_source_reports_load_and_vram_with_roles()
    {
        using var _ = Fx.Use("amd-laptop");
        var g = new GpuDevice { Index = 1, Vendor = GpuVendor.Amd, Name = "AMD", Title = "AMD", SysDir = "/sys/class/drm/card1/device", Card = "card1", VendorId = "1002", DeviceId = "15BF" };
        var rows = new GpuSource(g).Read().ToList();
        Assert.Contains(rows, r => r.Role == "gpu1.load" && r.Value == 37f);
        Assert.Contains(rows, r => r.Role == "gpu1.vram.pct" && r.Value == 50f);
        Assert.Contains(rows, r => r.Role == "gpu1.vram.used" && r.Value == 0.25f);
    }

    [Fact]
    public void Gpu_report_data_reaches_the_html()
    {
        using var _ = Fx.Use("amd-laptop");
        var g = new GpuDevice { Index = 0, Vendor = GpuVendor.Amd, Name = "AMD Radeon RX 7600", Title = "AMD Radeon RX 7600", VendorId = "1002", DeviceId = "7480", SysDir = "/sys/class/drm/card1/device", Card = "card1" };
        var data = ReportFactory.Build(new List<InfoNode>(), new[] { g }, new List<DiskHealthCard>(), new List<DiskHealthCard>(), null, null, null);
        string html = ReportBuilder.Html(data);
        Assert.Contains("Placa de vídeo · AMD Radeon RX 7600", html);
        Assert.Contains("Navi 33", html);
        Assert.Contains("sensores do kernel", html);   // rodapé do Linux
        Assert.DoesNotContain("lidos do Windows", html);
    }
}

public class EnergyTests
{
    [Fact]
    public void Battery_health_wear_and_time_remaining()
    {
        using var _ = Fx.Use("amd-laptop");
        var b = BatteryReader.Read().Single();                        // a bateria do mouse (scope=Device) não conta
        Assert.Equal("BAT0", b.Name);
        Assert.Equal(72, b.Percent);
        Assert.Equal(56.0, b.EnergyFullWh);
        Assert.Equal(71.0, b.EnergyDesignWh);
        Assert.Equal(78.9, Math.Round(b.HealthPct!.Value, 1));
        Assert.Equal(312, b.Cycles);
        Assert.True(b.Discharging);
        Assert.Equal(40.32 / 14.0, b.TimeRemaining!.Value.TotalHours, 3);
        Assert.False(BatteryReader.AcOnline());                       // na bateria
    }

    [Fact]
    public void Energy_cards_judge_wear()
    {
        using var _ = Fx.Use("amd-laptop");
        var cards = EnergyCards.Build();
        Assert.Equal(2, cards.Count);                                  // bateria + energia
        var bat = cards[0];
        Assert.Equal(DiskVerdict.Good, bat.Verdict);                   // 21% de desgaste: normal
        Assert.Contains("normal", bat.Reasons);
        Assert.Contains(bat.Metrics, m => m.Key == "Autonomia estimada" && m.Value.StartsWith("2h 52min"));
        Assert.Equal("EM USO", cards[1].VerdictText);
        Assert.Contains(cards[1].Metrics, m => m.Key == "Fonte de energia" && m.Value == "Bateria");
        Assert.Contains(cards[1].Metrics, m => m.Key == "Perfil de energia" && m.Value == "Equilibrado");
    }

    [Fact]
    public void Desktop_without_battery_says_so()
    {
        using var _ = Fx.Use("intel-desktop");
        var cards = EnergyCards.Build();
        Assert.Single(cards);
        Assert.Equal("Nenhuma bateria detectada", cards[0].Meta);
        Assert.Contains(cards[0].Metrics, m => m.Value == "Tomada (PC de mesa)");
        Assert.Null(BatteryReader.AcOnline());
    }
}
