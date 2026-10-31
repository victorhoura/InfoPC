using Xunit;

namespace InfoPC.Tests;

public class BenchToolsTests
{
    [Fact]
    public void Kernel_log_scan_counts_each_family_of_error()
    {
        const string log = """
        Oct 25 10:00:01 pc kernel: mce: [Hardware Error]: Machine check events logged
        Oct 25 10:00:02 pc kernel: [Hardware Error]: CPU 3: Machine Check: 0 Bank 5: bea0000000000108
        Oct 25 10:05:00 pc kernel: NVRM: Xid (PCI:0000:01:00): 79, GPU has fallen off the bus.
        Oct 25 10:06:00 pc kernel: amdgpu 0000:03:00.0: amdgpu: GPU reset(2) succeeded!
        Oct 25 10:07:00 pc kernel: i915 0000:00:02.0: [drm] GPU HANG: ecode 9:1:85fffffc
        Oct 25 11:00:00 pc kernel: blk_update_request: I/O error, dev sda, sector 1234 op 0x0:(READ)
        Oct 25 11:01:00 pc kernel: ata3.00: exception Emask 0x0 SAct 0x0 SErr 0x0 action 0x6 frozen
        Oct 25 12:00:00 pc kernel: Out of memory: Killed process 4242 (chrome) total-vm:9999999kB
        Oct 25 12:00:01 pc kernel: usb 1-1: new high-speed USB device number 5 using xhci_hcd
        Oct 25 12:00:02 pc kernel: EXT4-fs (sda1): mounted filesystem with ordered data mode
        """;
        var e = KernelLog.Scan(log);
        Assert.Equal(2, e.Mce);
        Assert.Equal(3, e.GpuResets);
        Assert.Equal(2, e.DiskErrors);
        Assert.Equal(1, e.Oom);
        Assert.Equal(8, e.Total);
        Assert.Contains("Out of memory", e.LastDetail);
        Assert.Null(e.Error);
        Assert.Equal(0, KernelLog.Scan("usb 1-1: new device\nEXT4-fs mounted").Total);
    }

    [Fact]
    public void Kernel_log_reports_a_readable_reason_when_it_cannot_be_read()
    {
        using var fx = Fx.Use("intel-desktop");   // Shell.Override devolve "comando não encontrado" para tudo
        var e = KernelLog.ErrorsSince(7);
        Assert.NotNull(e.Error);
        Assert.Equal(0, e.Total);
        Shell.Override = (f, _) => f == "journalctl" ? new ProcResult(1, "", "Hint: You are currently not seeing messages from other users. Permission denied") : ProcResult.Missing(f);
        Assert.Contains("permissão", KernelLog.ErrorsSince(7).Error);
    }

    [Fact]
    public void Kernel_log_uses_journalctl_when_available()
    {
        using var fx = Fx.Use("intel-desktop");
        Shell.Override = (f, a) => f == "journalctl" && a.Contains("--since")
            ? new ProcResult(0, "kernel: NVRM: Xid (PCI:0000:01:00): 31, pid=1\n", "") : ProcResult.Missing(f);
        Assert.Equal(1, KernelLog.ErrorsSince(7).GpuResets);
    }

    [Theory]
    [InlineData("=====\n    vkmark Score: 4527\n=====", 4527)]
    [InlineData("                                  glmark2 Score: 12345 \n", 12345)]
    [InlineData("   glmark2-es2 Score: 77", 77)]
    public void Gpu_benchmark_score_is_parsed(string output, double expected) => Assert.Equal(expected, GpuBench.ParseScore(output));

    [Fact]
    public void Gpu_benchmark_without_score_is_null() => Assert.Null(GpuBench.ParseScore("Error: could not create a window"));

    [Fact]
    public async Task Gpu_benchmark_run_explains_failures()
    {
        try
        {
            Shell.Override = (f, _) => new ProcResult(0, "no display\n", "Error: cannot open display");
            var r = await GpuBench.RunAsync("vkmark", CancellationToken.None);
            Assert.Null(r.Score);
            Assert.Contains("display", r.Error);

            Shell.Override = (f, _) => new ProcResult(0, "    vkmark Score: 999\n", "");
            Assert.Equal(999, (await GpuBench.RunAsync("vkmark", CancellationToken.None)).Score);

            Shell.Override = (f, _) => ProcResult.Missing(f);
            Assert.Contains("não está instalado", (await GpuBench.RunAsync("vkmark", CancellationToken.None)).Error);
        }
        finally { Shell.Override = null; }
    }

    [Fact]
    public void Bench_target_owner_is_the_most_specific_mount()
    {
        var mounts = new[] { ("/", "ext4"), ("/home", "ext4"), ("/home/vic/jogos", "btrfs"), ("/mnt/dados", "xfs") };
        Assert.Equal("/home", BenchTargets.Owner("/home/vic", mounts));
        Assert.Equal("/home/vic/jogos", BenchTargets.Owner("/home/vic/jogos/x", mounts));
        Assert.Equal("/", BenchTargets.Owner("/opt/app", mounts));
        Assert.Equal("/mnt/dados", BenchTargets.Owner("/mnt/dados", mounts));
        Assert.Equal("/", BenchTargets.Owner("/home2/vic", mounts));      // "/home2" não está dentro de "/home"
    }

    [Fact]
    public void Bios_guide_follows_vendor_and_cpu_family()
    {
        var amd = OcGuide.Build("Gigabyte Technology Co., Ltd.", "B650 AORUS ELITE AX", "AMD Ryzen 7 7700X 8-Core Processor");
        Assert.Contains("GIGABYTE B650 AORUS ELITE AX", amd.Title);
        Assert.Contains("RYZEN 7 7700X", amd.Title);
        Assert.Equal(6, amd.Steps.Count);
        Assert.Contains("Extreme Memory Profile", amd.Steps[1].Where);
        Assert.Contains("Curve Optimizer", amd.Steps[3].Title);
        Assert.Contains("Tweaker", amd.Steps[3].Where);

        var intel = OcGuide.Build("ASUSTeK COMPUTER INC.", "PRIME Z790-P", "13th Gen Intel(R) Core(TM) i7-13700K");
        Assert.Equal(3, intel.Steps.Count);
        Assert.Contains("AI Tweaker", intel.Steps[1].Where);
        Assert.Contains("PL1/PL2", intel.Steps[2].Text);

        var msi = OcGuide.Build("Micro-Star International Co., Ltd.", "MAG B550", "AMD Ryzen 5 5600X");
        Assert.Contains("OC → A-XMP/EXPO", msi.Steps[1].Where);
    }

    [Fact]
    public void Stability_check_stops_at_safety_limits_and_new_kernel_errors()
    {
        var none = new KernelErrors(0, 0, 0, 0, null, null);
        var c = new StabilityCheck(cpu: true, gpu: true, none);
        Assert.Null(c.Sample(70, 60, 0));
        Assert.Contains("CPU", c.Sample(95, 60, 0));
        Assert.Contains("GPU", c.Sample(70, 88, 0));
        Assert.Contains("memória", c.Sample(70, 60, 3));
        Assert.Equal(95f, c.MaxCpuTemp);

        var ok = new StabilityCheck(true, false, none);
        ok.Sample(80, 99, 0);                                             // GPU não é alvo: 99 °C não conta
        var stable = ok.Finish(none, null, TimeSpan.FromMinutes(15));
        Assert.True(stable.Stable);
        Assert.Contains("15 min", stable.Reason);

        var broken = new StabilityCheck(true, false, new KernelErrors(1, 0, 0, 0, null, null));
        var unstable = broken.Finish(new KernelErrors(3, 0, 0, 0, null, null), null, TimeSpan.FromMinutes(5));
        Assert.False(unstable.Stable);
        Assert.Contains("2 novo(s) erro(s)", unstable.Reason);

        // erros que já existiam antes do teste não reprovam
        Assert.True(new StabilityCheck(true, false, new KernelErrors(5, 0, 0, 0, null, null))
                    .Finish(new KernelErrors(5, 0, 0, 0, null, null), null, TimeSpan.FromMinutes(5)).Stable);
    }

    [Fact]
    public void Stability_is_inconclusive_when_the_kernel_log_could_not_be_read()
    {
        var unread = new KernelErrors(0, 0, 0, 0, null, "permission denied");          // o caso do relatório do R13
        var r = new StabilityCheck(true, false, unread).Finish(unread, null, TimeSpan.FromMinutes(15));
        Assert.False(r.Stable);
        Assert.True(r.Inconclusive);
        Assert.Equal("INCONCLUSIVO", r.Verdict);
        Assert.Contains("permission denied", r.Reason);

        // só o log de depois legível: os erros antigos não podem virar "novos"
        var r2 = new StabilityCheck(true, false, unread).Finish(new KernelErrors(4, 0, 0, 0, null, null), null, TimeSpan.FromMinutes(5));
        Assert.True(r2.Inconclusive);

        // uma falha de verdade continua INSTÁVEL, mesmo sem o log
        var r3 = new StabilityCheck(true, false, unread).Finish(unread, "a CPU chegou a 95 °C", TimeSpan.FromMinutes(5));
        Assert.Equal("INSTÁVEL", r3.Verdict);
    }

    [Fact]
    public void Stability_is_inconclusive_when_the_gpu_load_stopped()
    {
        var none = new KernelErrors(0, 0, 0, 0, null, null);
        var r = new StabilityCheck(false, true, none).Finish(none, null, TimeSpan.FromMinutes(1), "a carga de GPU parou (vkmark fechou sozinho)");
        Assert.Equal("INCONCLUSIVO", r.Verdict);
        Assert.Contains("vkmark fechou sozinho", r.Reason);
        // se o kernel viu a GPU resetar no mesmo teste, é INSTÁVEL
        var reset = new StabilityCheck(false, true, none).Finish(new KernelErrors(0, 1, 0, 0, null, null), null, TimeSpan.FromMinutes(1), "a carga de GPU parou");
        Assert.Equal("INSTÁVEL", reset.Verdict);
    }

    [Fact]
    public async Task Gpu_stress_reports_when_its_program_keeps_closing()
    {
        // programa que só termina na hora, como o vkmark sem display: o caso do relatório do R12
        string quick = OperatingSystem.IsWindows() ? Path.Combine(Environment.SystemDirectory, "whoami.exe") : "/bin/true";
        var g = new GpuStress();
        Assert.True(g.Start(quick));
        for (int i = 0; i < 100 && !g.Failed; i++) await Task.Delay(100);
        Assert.True(g.Failed);
        Assert.False(g.Running);                                          // antes continuava "rodando" sem carga
        Assert.Contains("fechou sozinho", g.Error);
        g.Stop();
        Assert.False(g.Failed);
    }

    /// <summary>Programa de carga falso: fica <paramref name="seconds"/> s no ar, marca a rodada em "rodadas.txt" e sai com <paramref name="code"/>.</summary>
    private static string FakeLoad(string dir, double seconds, int code)
    {
        string marks = Path.Combine(dir, "rodadas.txt");
        if (OperatingSystem.IsWindows())
        {
            string cmd = Path.Combine(dir, "carga.cmd");
            // o ping espera 1 s entre as respostas: n+1 pings dão n segundos
            File.WriteAllText(cmd, $"@ping -n {(int)Math.Ceiling(seconds) + 1} 127.0.0.1 >nul\r\n@echo x>>\"{marks}\"\r\n@exit /b {code}\r\n");
            return cmd;
        }
        string sh = Path.Combine(dir, "carga.sh");
        File.WriteAllText(sh, $"#!/bin/sh\nsleep {seconds.ToString(System.Globalization.CultureInfo.InvariantCulture)}\necho x >> '{marks}'\nexit {code}\n");
        File.SetUnixFileMode(sh, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return sh;
    }

    private static int Rounds(string dir)
    {
        try { return File.ReadAllLines(Path.Combine(dir, "rodadas.txt")).Length; }
        catch (IOException) { return 0; }
    }

    [Fact]
    public async Task Gpu_stress_fails_on_a_nonzero_exit_even_after_three_seconds()
    {
        // R12-C: antes só a duração contava, então esta saída reiniciava para sempre e o teste podia dar ESTÁVEL
        string dir = Directory.CreateTempSubdirectory("infopc-gpu").FullName;
        var g = new GpuStress();
        try
        {
            Assert.True(g.Start(FakeLoad(dir, 3.4, 42)));
            for (int i = 0; i < 150 && !g.Failed; i++) await Task.Delay(100);
            Assert.True(g.Failed);
            Assert.False(g.Running);
            Assert.Contains("código 42", g.Error);
            Assert.Equal(1, Rounds(dir));                                 // não tentou de novo
        }
        finally { g.Stop(); Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task Gpu_stress_starts_a_new_round_when_the_program_ends_normally()
    {
        string dir = Directory.CreateTempSubdirectory("infopc-gpu").FullName;
        var g = new GpuStress();
        try
        {
            Assert.True(g.Start(FakeLoad(dir, 3.2, 0)));
            for (int i = 0; i < 200 && Rounds(dir) < 2; i++) await Task.Delay(100);
            Assert.True(Rounds(dir) >= 2);
            Assert.True(g.Running);
            Assert.False(g.Failed);
            Assert.Null(g.Error);
        }
        finally
        {
            g.Stop();
            await Task.Delay(300);                                        // o Stop encerra a árvore; dá tempo de soltar a pasta
            try { Directory.Delete(dir, true); } catch (IOException) { }
        }
    }

    [Fact]
    public void Stress_tester_starts_and_stops_cleanly()
    {
        var s = new StressTester();
        s.Start(cpu: true, ram: false);
        Assert.True(s.Running);
        Thread.Sleep(300);
        s.Stop();
        Assert.False(s.Running);
        Assert.Equal(0, s.MemoryErrors);
        s.Stop(); // chamar duas vezes não quebra
    }

    [Fact]
    public void Ram_bench_shared_with_windows_runs()
    {
        var lat = MemoryBench.MeasureLatencyNs(new CancellationTokenSource(1500).Token);
        Assert.InRange(lat, 0.1, 5000);
    }

    [Fact]
    public async Task Nvidia_tuning_validates_before_asking_for_a_password()
    {
        var g = new GpuDevice { NvmlBusId = "00000000:01:00.0" };
        Assert.False(GpuTuning.SetPowerLimit(g, 5).Ok);           // fora da faixa
        Assert.False(GpuTuning.LockClocks(g, 100000).Ok);
        Assert.False(GpuTuning.SetPowerLimit(new GpuDevice { NvmlBusId = "1;rm -rf /" }, 200).Ok);   // endereço malformado nunca vai ao comando
        string? seen = null;
        try
        {
            Elevate.Override = (f, a) => { seen = f + " " + string.Join(' ', a); return new ProcResult(0, "ok", ""); };
            Assert.True(GpuTuning.SetPowerLimit(g, 200).Ok);
            Assert.Equal("nvidia-smi -i 00000000:01:00.0 -pl 200", seen);
            Assert.True(GpuTuning.LockClocks(g, 1800).Ok);
            Assert.Equal("nvidia-smi -i 00000000:01:00.0 -lgc 210,1800", seen);
        }
        finally { Elevate.Override = null; }
        await Task.CompletedTask;
    }
}

/// <summary>Estes testes usam o próprio Linux (CI do GitHub); no Windows de desenvolvimento passam sem fazer nada.</summary>
public class RealLinuxTests
{
    private static bool Linux => OperatingSystem.IsLinux();

    [Fact]
    public void Real_system_is_readable()
    {
        if (!Linux) return;
        Locale.Init();
        var cpu = CpuInfo.Read();
        Assert.True(cpu.Threads >= 1);
        Assert.False(string.IsNullOrWhiteSpace(cpu.Model));
        var mem = MemInfo.Read();
        Assert.NotNull(mem);
        Assert.True(mem!.TotalKb > 100_000);
        var os = OsInfo.Read();
        Assert.False(string.IsNullOrWhiteSpace(os.Name));
        Assert.StartsWith("glibc", os.Libc);
        Assert.True(os.Uptime.TotalSeconds > 0);
        Assert.NotEmpty(SystemInfoCollector.Collect());
    }

    [Fact]
    public void Real_sensors_have_cpu_load_after_two_reads()
    {
        if (!Linux) return;
        var hub = SensorHub.CreateDefault(GpuCollector.Detect(true));
        hub.Refresh();
        double sink = 0; for (int i = 0; i < 3_000_000; i++) sink += Math.Sqrt(i);   // dá o que medir
        Thread.Sleep(400);
        hub.Refresh();
        Assert.NotNull(hub.CpuLoad);
        Assert.InRange(hub.CpuLoad!.Value, 0, 100);
        Assert.NotNull(hub.RamPercent);
        GC.KeepAlive(sink);
    }

    [Fact]
    public void Real_disks_and_mounts_are_linked()
    {
        if (!Linux) return;
        var mounts = DiskCollector.Mounts();
        Assert.Contains(mounts, m => m.MountPoint == "/");
        Assert.True(mounts.First(m => m.MountPoint == "/").TotalBytes > 0);
        _ = DiskCollector.Disks();   // não pode lançar nem em VM sem disco "padrão"
    }

    [Fact]
    public void Real_disk_benchmark_runs_with_a_small_file_and_cleans_up()
    {
        if (!Linux) return;
        string dir = Path.Combine(Path.GetTempPath(), "infopc-diskbench-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var target = new BenchTarget("teste", dir, "/", "?", 50L << 30);
            var r = DiskBench.Run(target, 64, (_, _) => { }, CancellationToken.None);
            Assert.True(r.SeqReadMBs > 0 && r.SeqWriteMBs > 0 && r.ReadIopsQ1 > 0 && r.WriteIopsQ1 > 0 && r.ReadIopsQ8 > 0);
            Assert.True(r.Score > 0);
            Assert.Empty(Directory.GetFiles(dir));
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public void Real_loading_benchmark_runs_and_cleans_up()
    {
        if (!Linux) return;
        string dir = Path.Combine(Path.GetTempPath(), "infopc-load-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var r = MemoryBench.MeasureLoading(new BenchTarget("teste", dir, "/", "?", 50L << 30), (_, _) => { }, CancellationToken.None);
            Assert.True(r.SeqReadMBs > 0 && r.SmallFilesMBs > 0 && r.MapLoadSeconds > 0);
            Assert.False(Directory.Exists(Path.Combine(dir, "mapa")));
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public void Real_text_mode_commands_succeed()
    {
        if (!Linux) return;
        foreach (var cmd in new[] { "--info", "--sensores", "--gpu", "--discos", "--energia", "--diagnostico" })
        {
            var sw = new StringWriter();
            Assert.Equal(0, Cli.Run(new[] { cmd }, sw));
            Assert.True(sw.ToString().Length > 20, cmd);
        }
    }

    [Fact]
    public void Real_gpu_detection_and_info_never_throw()
    {
        if (!Linux) return;
        foreach (var g in GpuCollector.Detect(true))
        {
            var d = GpuInfoBuilder.Build(g);
            Assert.NotEmpty(d.Sections);
            _ = GpuInfoBuilder.LiveExtras(g);
            _ = new GpuSource(g).Read().ToList();
        }
    }
}
