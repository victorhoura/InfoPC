using System.Text;

namespace InfoPC;

/// <summary>
/// Modo texto: serve para terminal, SSH sem interface gráfica, scripts e para o CI conferir o programa em Linux real.
/// <see cref="Run"/> devolve null quando os argumentos não pedem modo texto (aí abre a janela).
/// </summary>
public static class Cli
{
    public const string Help = @"InfoPC {0} para Linux

Uso: infopc [comando]

Sem comando, abre a janela.

Comandos (modo texto):
  --info                 Sistema, processador, memória, vídeo e discos
  --sensores [--seguir]  Sensores (temperatura, carga, clock, consumo); --seguir atualiza a cada segundo
  --gpu                  Detalhes das placas de vídeo
  --discos [--smart]     Discos; com --smart lê o S.M.A.R.T. (pede a senha de administrador)
  --energia              Bateria, fonte e perfil de energia
  --relatorio [arquivo]  Relatório HTML (padrão: InfoPC-relatorio-<computador>.html na pasta atual)
  --json [arquivo]       Mesmos dados em JSON (padrão: tela)
  --diagnostico          O que o programa encontrou e o que falta instalar para ler tudo
  --versao               Versão
  --ajuda                Esta ajuda

Dados e configurações: a pasta ""dados"" ao lado do programa (ou ~/.local/share/InfoPC).
Nada é enviado pela internet sem você pedir.";

    public static int? Run(string[] args, TextWriter? output = null)
    {
        if (args.Length == 0) return null;
        var o = output ?? Console.Out;
        string cmd = args[0].ToLowerInvariant();
        string? arg1 = args.Skip(1).FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal));
        bool Flag(string f) => args.Skip(1).Any(a => a.Equals(f, StringComparison.OrdinalIgnoreCase));
        Locale.Init();

        try
        {
            switch (cmd)
            {
                case "--versao": case "--version": case "-v": o.WriteLine(AppInfo.Version); return 0;
                case "--ajuda": case "--help": case "-h": case "-?": o.WriteLine(Help, AppInfo.Version); return 0;
                case "--info": PrintTree(o, SystemInfoCollector.Collect(null)); return 0;
                case "--gpu": return Gpu(o);
                case "--discos": return Disks(o, Flag("--smart"));
                case "--energia": foreach (var c in EnergyCards.Build()) PrintCard(o, c); return 0;
                case "--sensores": return Sensors(o, Flag("--seguir"));
                case "--relatorio": case "--report": return Report(o, arg1, json: false);
                case "--json": return Report(o, arg1, json: true);
                case "--diagnostico": return Diagnose(o);
                default:
                    if (cmd.StartsWith('-')) { o.WriteLine($"Comando desconhecido: {args[0]}. Use --ajuda."); return 2; }
                    return null;
            }
        }
        catch (Exception ex)
        {
            o.WriteLine("Erro: " + ex.Message);
            return 1;
        }
    }

    // ---------- impressão ----------

    public static void PrintTree(TextWriter o, IEnumerable<InfoNode> nodes, int depth = 0)
    {
        foreach (var n in nodes)
        {
            o.WriteLine(new string(' ', depth * 2) + (depth == 0 ? n.Header.ToUpperInvariant() : n.Display));
            if (n.Children.Count > 0) PrintTree(o, n.Children, depth + 1);
            if (depth == 0) o.WriteLine();
        }
    }

    public static void PrintCard(TextWriter o, DiskHealthCard c)
    {
        o.WriteLine($"{c.Title}  [{c.VerdictText}]");
        if (c.Meta.Length > 0) o.WriteLine("  " + c.Meta);
        if (c.Reasons.Length > 0) o.WriteLine("  " + c.Reasons);
        foreach (var m in c.Metrics) o.WriteLine($"    {m.Key}: {m.Value}");
        o.WriteLine();
    }

    private static int Gpu(TextWriter o)
    {
        var gpus = GpuCollector.Detect();
        if (gpus.Count == 0) { o.WriteLine("Nenhuma GPU PCI encontrada em /sys/class/drm."); return 0; }
        foreach (var g in gpus)
        {
            var d = GpuInfoBuilder.Build(g);
            o.WriteLine($"{g.Name.ToUpperInvariant()}  {(d.Subtitle.Length > 0 ? "· " + d.Subtitle : "")}");
            PrintTree(o, d.Sections, 1);
            foreach (var kv in GpuInfoBuilder.LiveExtras(g)) o.WriteLine($"  {kv.Key}: {kv.Value}");
            o.WriteLine();
        }
        return 0;
    }

    private static int Disks(TextWriter o, bool smart)
    {
        var disks = DiskCollector.Disks();
        List<SmartInfo>? infos = null;
        if (smart)
        {
            var (list, res) = SmartReader.ReadAll(disks);
            if (!res.Ok) o.WriteLine("S.M.A.R.T.: " + res.Message + "\n");
            infos = list;
        }
        foreach (var c in DiskHealthEvaluator.Cards(disks, infos)) PrintCard(o, c);
        return 0;
    }

    private static int Sensors(TextWriter o, bool follow)
    {
        var hub = SensorHub.CreateDefault(GpuCollector.Detect());
        hub.Refresh();
        do
        {
            Thread.Sleep(1000); // carga e consumo precisam de duas leituras
            hub.Refresh();
            if (follow) o.WriteLine($"----- {DateTime.Now:HH:mm:ss} -----");
            foreach (var grp in hub.Rows.Where(r => r.RawValue != null).OrderBy(r => r.HwKind).ThenBy(r => r.Hardware, StringComparer.Ordinal).GroupBy(r => r.Hardware))
            {
                o.WriteLine(grp.Key.ToUpperInvariant());
                foreach (var r in grp.OrderBy(r => r.Order).ThenBy(r => r.Name, StringComparer.Ordinal))
                    o.WriteLine($"  {r.Category,-12} {r.Name,-34} {r.Value,12}   mín {r.Min,10}   máx {r.Max,10}");
                o.WriteLine();
            }
            if (hub.Rows.Count == 0) o.WriteLine("Nenhum sensor encontrado. " + hub.DescribeCpuTempProblem());
            else if (hub.CpuTemperature == null) o.WriteLine("Temperatura da CPU: " + hub.DescribeCpuTempProblem());
            if (hub.Rapl?.Blocked == true) o.WriteLine("Consumo da CPU (RAPL): o kernel só deixa o administrador ler. Rode com sudo para ver.");
        } while (follow && !KeyPressed());
        return 0;
    }

    private static bool KeyPressed()
    {
        try { return Console.KeyAvailable; }
        catch (InvalidOperationException) { return false; } // entrada redirecionada: segue até Ctrl+C
    }

    private static int Report(TextWriter o, string? file, bool json)
    {
        var gpus = GpuCollector.Detect();
        var disks = DiskCollector.Disks();
        var hub = SensorHub.CreateDefault(gpus);
        hub.Refresh(); Thread.Sleep(1000); hub.Refresh();
        var data = ReportFactory.Build(SystemInfoCollector.Collect(null, gpus, disks), gpus, DiskHealthEvaluator.Cards(disks, null),
                                       EnergyCards.Build(hub), null, hub, AppSettings.Load());
        string text = json ? ReportBuilder.Json(data) : ReportBuilder.Html(data);
        if (file == null && json) { o.WriteLine(text); return 0; }
        file ??= $"InfoPC-relatorio-{data.Machine}.html";
        File.WriteAllText(file, text, new UTF8Encoding(false));
        o.WriteLine("Relatório gravado em " + Path.GetFullPath(file));
        return 0;
    }

    // ---------- diagnóstico ----------

    public static int Diagnose(TextWriter o)
    {
        var os = OsInfo.Read();
        o.WriteLine($"InfoPC {AppInfo.Version} · {os.Name} · kernel {os.Kernel} · {os.Arch} · {os.Libc}");
        o.WriteLine($"Sessão: {os.Session ?? "sem ambiente gráfico"} · usuário {(Elevate.IsRoot ? "root" : "comum")} · pasta de dados: {AppSettings.Folder}");
        if (AppSettings.FolderNotice is { } notice) o.WriteLine(notice);
        o.WriteLine();
        string Tool(string cmd, string why) => Shell.Which(cmd) is { } p ? $"  [ok]  {cmd,-14} {p}" : $"  [--]  {cmd,-14} falta: {why}";
        o.WriteLine("Programas de apoio:");
        o.WriteLine(Tool("pkexec", "pedir a senha de administrador na interface (pacote polkit)"));
        o.WriteLine(Tool("smartctl", "S.M.A.R.T. dos discos (pacote smartmontools)"));
        o.WriteLine(Tool("dmidecode", "módulos de RAM (pacote dmidecode)"));
        o.WriteLine(Tool("lspci", "nomes de dispositivos PCI (pacote pciutils)"));
        o.WriteLine(Tool("lsusb", "dispositivos USB (pacote usbutils)"));
        o.WriteLine(Tool("sensors", "diagnóstico extra de sensores (pacote lm-sensors)"));
        o.WriteLine(Tool("nvidia-smi", "ajustes da GPU NVIDIA (driver proprietário)"));
        o.WriteLine(Tool("gamemoderun", "perfis de jogo com GameMode (pacote gamemode)"));
        o.WriteLine(Tool("mangohud", "overlay de FPS nos jogos (pacote mangohud)"));
        o.WriteLine(Tool("xdg-open", "abrir links e pastas"));
        o.WriteLine();
        o.WriteLine("Hardware:");
        var gpus = GpuCollector.Detect();
        o.WriteLine($"  GPUs: {gpus.Count}" + string.Concat(gpus.Select(g => $"\n    - {g.Name} · driver {g.Driver ?? "nenhum"}{(g.HasNvml ? " · NVML ok" : "")}{(g.Suspended ? " · em repouso" : "")}")));
        o.WriteLine($"  NVML (NVIDIA): {(NvmlLib.Available ? "disponível" : "ausente")}");
        o.WriteLine($"  hwmon: {Sys.List("/sys/class/hwmon").Count} chip(s) · zonas térmicas: {Sys.List("/sys/class/thermal").Count(n => n.StartsWith("thermal_zone"))}");
        var hub = SensorHub.CreateDefault(gpus);
        hub.Refresh(); Thread.Sleep(600); hub.Refresh();
        o.WriteLine($"  sensores lidos: {hub.Rows.Count(r => r.RawValue != null)}");
        o.WriteLine($"  temperatura da CPU: {(hub.CpuTemperature is { } t ? $"{t:0.#} °C" : "indisponível — " + hub.DescribeCpuTempProblem())}");
        o.WriteLine($"  consumo da CPU (RAPL): {(hub.CpuPower is { } w ? $"{w:0.#} W" : hub.Rapl?.Blocked == true ? "bloqueado para usuário comum (precisa de root)" : "indisponível")}");
        o.WriteLine($"  bateria: {BatteryReader.Read().Count} · discos: {DiskCollector.Disks().Count}");
        return 0;
    }
}
