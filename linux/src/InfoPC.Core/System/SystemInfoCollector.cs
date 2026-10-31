namespace InfoPC;

/// <summary>Monta a árvore "Sistema" (as mesmas seis categorias da edição Windows) a partir de /proc, /sys e dos outros coletores.</summary>
public static class SystemInfoCollector
{
    public static List<InfoNode> Collect(RamLayout? ram = null, IReadOnlyList<GpuDevice>? gpus = null, IReadOnlyList<PhysicalDisk>? disks = null)
    {
        var roots = new List<InfoNode>();
        Safe(roots, "Sistema operacional", CollectOs);
        Safe(roots, "Placa-mãe e BIOS", CollectBoard);
        Safe(roots, "Processador", CollectCpu);
        Safe(roots, "Memória RAM", n => CollectRam(n, ram));
        Safe(roots, "Placa de vídeo", n => CollectGpu(n, gpus ?? GpuCollector.Detect()));
        Safe(roots, "Armazenamento", n => CollectDisks(n, disks ?? DiskCollector.Disks()));
        return roots;
    }

    private static void Safe(List<InfoNode> roots, string title, Action<InfoNode> fill)
    {
        var node = new InfoNode { Header = title };
        try { fill(node); }
        catch (Exception ex) { node.Add("Erro ao ler", ex.Message); }
        roots.Add(node);
    }

    private static void CollectOs(InfoNode n)
    {
        var o = OsInfo.Read();
        n.Add("Nome", o.Name);
        if (o.VersionId.Length > 0) n.Add("Versão", o.VersionId);
        n.Add("Kernel", o.Kernel);
        n.Add("Arquitetura", o.Arch);
        n.Add("Nome do computador", o.Hostname);
        n.Add("Tempo ligado", Fmt.Uptime(o.Uptime));
        if (o.Desktop != null || o.Session != null) n.Add("Ambiente gráfico", string.Join(" · ", new[] { o.Desktop, o.Session }.Where(s => s != null)!));
        n.Add("Sistema de inicialização", Fmt.Dash(o.Init));
        n.Add("Firmware", o.BootMode + (o.SecureBoot is bool sb ? (sb ? " · Secure Boot ligado" : " · Secure Boot desligado") : ""));
        n.Add("Gerenciador de pacotes", Fmt.Dash(o.PackageManager));
        n.Add("Biblioteca C", o.Libc);
        if (o.Virtualization != null) n.Add("Virtualização", o.Virtualization);
    }

    private static void CollectBoard(InfoNode n)
    {
        var d = Dmi.Read();
        if (!d.HasData)
        {
            n.Add("Sem dados", "O kernel não expõe o DMI (comum em ARM, contêiner e WSL).");
            return;
        }
        var sys = n.Add("Sistema");
        sys.Add("Fabricante", Fmt.Dash(d.SysVendor));
        sys.Add("Modelo", Fmt.Dash(d.ProductName));
        sys.Add("Versão", Fmt.Dash(d.ProductVersion));
        if (d.ChassisType.Length > 0) sys.Add("Tipo de gabinete", d.ChassisType);
        var board = n.Add("Placa-mãe");
        board.Add("Fabricante", Fmt.Dash(d.BoardVendor));
        board.Add("Modelo", Fmt.Dash(d.BoardName));
        board.Add("Versão", Fmt.Dash(d.BoardVersion));
        var bios = n.Add("BIOS");
        bios.Add("Fabricante", Fmt.Dash(d.BiosVendor));
        bios.Add("Versão", Fmt.Dash(d.BiosVersion));
        bios.Add("Data", Fmt.Dash(d.BiosDate));
    }

    private static void CollectCpu(InfoNode n)
    {
        var c = CpuInfo.Read();
        n.Add("Nome", c.Model);
        n.Add("Fabricante", Fmt.Dash(c.Vendor));
        n.Add("Arquitetura", c.Architecture);
        n.Add("Soquetes", c.Sockets.ToString());
        n.Add("Núcleos físicos", c.Cores.ToString());
        n.Add("Threads (lógicos)", c.Threads.ToString());
        if (c.MinMhz != null) n.Add("Clock mínimo", Fmt.Mhz(c.MinMhz.Value));
        if (c.BaseMhz != null) n.Add("Clock base", Fmt.Mhz(c.BaseMhz.Value));
        if (c.MaxMhz != null) n.Add("Clock máximo (boost)", Fmt.Mhz(c.MaxMhz.Value));
        if (c.CurMhz != null) n.Add("Clock médio agora", Fmt.Mhz(c.CurMhz.Value));
        if (c.Governor != null) n.Add("Governador de frequência", c.Governor + (c.ScalingDriver != null ? $" ({c.ScalingDriver})" : ""));
        if (c.Epp != null) n.Add("Preferência de energia (EPP)", c.Epp);
        foreach (var cache in c.Caches) n.Add("Cache " + cache.Name, cache.Text);
        if (c.Family.Length > 0) n.Add("Família / modelo / stepping", $"{c.Family} / {c.ModelId} / {c.Stepping}");
        if (c.Microcode != null) n.Add("Microcódigo", c.Microcode);
        n.Add("Virtualização por hardware", c.Virtualization ? $"{c.VirtualizationKind}" + (c.KvmAvailable ? " · KVM disponível" : " · /dev/kvm ausente") : "Não detectada");
        if (c.Vulnerabilities.Count > 0)
        {
            var v = n.Add("Mitigações de segurança da CPU", c.Vulnerabilities.Values.Count(x => x.StartsWith("Vulnerable")) is var bad and > 0 ? $"{bad} sem mitigação" : "");
            foreach (var (k, val) in c.Vulnerabilities.OrderBy(k => k.Key, StringComparer.Ordinal)) v.Add(k, val);
        }
    }

    private static void CollectRam(InfoNode n, RamLayout? ram)
    {
        var mem = MemInfo.Read();
        var mods = ram?.Modules.Where(m => m.Populated).ToList();
        if (mods is { Count: > 0 })
            n.Add("Total instalado", Fmt.Bytes(mods.Sum(m => m.SizeMb) * 1048576.0));
        else if (mem != null)
            n.Add("Total visível ao sistema", Fmt.Bytes(mem.TotalKb * 1024.0));

        var sum = n.Add("Resumo");
        if (mods is { Count: > 0 })
        {
            int slots = ram!.Array?.Slots > 0 ? ram.Array!.Slots : ram.Modules.Count;
            sum.Add("Módulos instalados", slots > 0 ? $"{mods.Count} de {slots} slots" : mods.Count.ToString());
            if (ram.Array is { MaxCapacityMb: > 0 } arr) sum.Add("Capacidade máxima suportada pela placa", Fmt.Bytes(arr.MaxCapacityMb * 1048576.0));
            var types = mods.Select(m => m.Type).Where(t => t.Length > 0).Distinct().ToList();
            if (types.Count > 0) sum.Add("Tipo", string.Join(" / ", types));
            int conf = mods.Max(m => m.ConfiguredMts), rated = mods.Max(m => m.SpeedMts);
            sum.Add("Velocidade em uso", conf > 0 ? $"{conf} MT/s" : "—");
            sum.Add("Velocidade nominal do módulo", rated > 0 ? $"{rated} MT/s" : "—");
            if (conf > 0 && rated > 0 && conf < rated) sum.Add("Aviso", "A memória roda abaixo da velocidade nominal: ative o perfil XMP/EXPO na BIOS.");
            var chans = mods.Select(m => m.BankLocator).Where(b => b.Contains("CHANNEL", StringComparison.OrdinalIgnoreCase)).Distinct().Count();
            if (chans > 0) sum.Add("Canais ativos (estimado)", chans.ToString());
            bool ecc = ram.Array != null && !ram.Array.Ecc.Equals("None", StringComparison.OrdinalIgnoreCase) && ram.Array.Ecc.Length > 0 && ram.Array.Ecc != "Unknown";
            sum.Add("ECC", ecc ? "Sim (correção de erros)" : "Não");
            var volts = mods.Where(m => m.CfgV != null).Select(m => $"{m.CfgV:0.##} V").Distinct().ToList();
            if (volts.Count > 0) sum.Add("Tensão em uso", string.Join(" / ", volts));
        }
        else
        {
            sum.Add("Módulos", "Os detalhes (tipo, velocidade, fabricante) só o administrador lê. Clique em LER MÓDULOS na página Sistema.");
        }
        if (mem != null)
        {
            sum.Add("Em uso (no momento da leitura)", $"{Fmt.Bytes(mem.UsedKb * 1024.0)} de {Fmt.Bytes(mem.TotalKb * 1024.0)} ({mem.UsedPct / 100:P0})");
            sum.Add("Disponível", Fmt.Bytes(mem.AvailableKb * 1024.0));
            sum.Add("Cache de arquivos", Fmt.Bytes((mem.CachedKb + mem.BuffersKb) * 1024.0));
        }
        var swaps = MemInfo.Swaps();
        if (swaps.Count > 0)
        {
            var sw = n.Add("Swap");
            foreach (var s in swaps) sw.Add(s.IsZram ? s.Name + " (zram, comprimida na RAM)" : s.Name, $"{Fmt.Bytes(s.UsedKb * 1024.0)} de {Fmt.Bytes(s.SizeKb * 1024.0)}");
        }
        else n.Add("Swap", "Não configurada");

        if (mods != null)
            foreach (var m in mods)
            {
                var slot = n.Add(string.IsNullOrEmpty(m.Locator) ? "Módulo" : m.Locator, m.PartNumber);
                slot.Add("Capacidade", Fmt.Bytes(m.SizeMb * 1048576.0));
                slot.Add("Tipo", Fmt.Dash(m.Type));
                slot.Add("Formato", Fmt.Dash(m.FormFactor));
                if (m.Rank > 0) slot.Add("Ranks", m.Rank.ToString());
                slot.Add("Velocidade em uso", m.ConfiguredMts > 0 ? $"{m.ConfiguredMts} MT/s" : "—");
                slot.Add("Velocidade nominal", m.SpeedMts > 0 ? $"{m.SpeedMts} MT/s" : "—");
                if (m.CfgV != null) slot.Add("Tensão em uso", $"{m.CfgV:0.##} V");
                if (m.DataWidth > 0) slot.Add("Largura de dados / total", $"{m.DataWidth} / {m.TotalWidth} bits");
                slot.Add("Fabricante", Fmt.Dash(m.Manufacturer));
                slot.Add("Part number", Fmt.Dash(m.PartNumber));
                if (m.Serial.Length > 0) slot.Add("Nº de série", m.Serial);
            }
    }

    private static void CollectGpu(InfoNode n, IReadOnlyList<GpuDevice> gpus)
    {
        if (gpus.Count == 0)
        {
            n.Add("Sem dados", "Nenhuma GPU PCI encontrada em /sys/class/drm.");
            return;
        }
        foreach (var g in gpus)
        {
            var gn = n.Add(g.Name);
            gn.Add("Tipo", g.IsVirtual ? "Virtual" : g.IsIntegrated ? "Integrada" : "Dedicada");
            gn.Add("Driver do kernel", g.Driver ?? "nenhum");
            long vram = Sys.Long(g.SysDir + "/mem_info_vram_total") ?? 0;
            if (g.HasNvml && NvmlLib.MemoryInfo(g.Nvml) is { } m && !g.Suspended) vram = (long)m.Total;
            if (vram > 0) gn.Add("Memória de vídeo", Fmt.Bytes(vram));
            gn.Add("Código PCI", g.PciId + (g.PciSlot.Length > 0 ? " · " + g.PciSlot : ""));
        }
    }

    public static void CollectDisks(InfoNode n, IReadOnlyList<PhysicalDisk> disks)
    {
        n.Children.Clear();
        if (disks.Count == 0) { n.Add("Sem dados", "Nenhum disco listado em /sys/block."); return; }
        foreach (var d in disks)
        {
            var dn = n.Add(d.Title, Fmt.Capacity(d.SizeBytes));
            dn.Add("Dispositivo", d.DevPath);
            dn.Add("Interface", d.Bus);
            dn.Add("Tipo", d.MediaType);
            if (d.Firmware.Length > 0) dn.Add("Firmware", d.Firmware);
            if (d.Serial.Length > 0) dn.Add("Nº de série", d.Serial);
            foreach (var m in d.Mounts)
                dn.Add($"Montado em {m.MountPoint}", $"{m.FsType} · {Fmt.Bytes(m.UsedBytes)} usados de {Fmt.Bytes(m.TotalBytes)} ({m.UsedPct / 100:P0})");
        }
        var orphan = DiskCollector.Mounts().Where(m => m.Disk == null).ToList();
        foreach (var m in orphan)
            n.Add($"Montado em {m.MountPoint}", $"{m.FsType} · {m.Device} · {Fmt.Bytes(m.UsedBytes)} usados de {Fmt.Bytes(m.TotalBytes)}");
    }
}
