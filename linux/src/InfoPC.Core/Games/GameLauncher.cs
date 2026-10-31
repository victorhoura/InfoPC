using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace InfoPC;

/// <summary>Divide uma linha de argumentos respeitando aspas simples e duplas (sem shell: nada é interpretado, só separado).</summary>
public static class ArgSplitter
{
    public static List<string> Split(string line)
    {
        var args = new List<string>();
        var cur = new StringBuilder();
        char quote = '\0';
        bool has = false;
        foreach (char c in line)
        {
            if (quote != '\0') { if (c == quote) quote = '\0'; else cur.Append(c); }
            else if (c is '"' or '\'') { quote = c; has = true; }
            else if (char.IsWhiteSpace(c)) { if (has || cur.Length > 0) { args.Add(cur.ToString()); cur.Clear(); has = false; } }
            else cur.Append(c);
        }
        if (has || cur.Length > 0) args.Add(cur.ToString());
        return args;
    }
}

/// <summary>
/// Aplica o perfil de um jogo e o inicia. No Linux o perfil vira ferramentas padrão do ecossistema: GameMode (prioridade e governador),
/// MangoHud (overlay de FPS), GPU dedicada (PRIME) e os argumentos de janela do jogo. Nada fica gravado fora do que o usuário mandou.
/// </summary>
public static class GameLauncher
{
    // ---------------- Argumentos de gráficos (os mesmos da edição Windows) ----------------

    /// <summary>Monta a linha de argumentos do perfil (modo de janela, resolução, taxa e extras) no estilo da engine escolhida.</summary>
    public static string BuildArgs(GameProfile p)
    {
        var a = new List<string>();
        int w = 0, h = 0;
        var m = Regex.Match(p.Res ?? "", @"^(\d+)x(\d+)$");
        if (m.Success) { w = int.Parse(m.Groups[1].Value); h = int.Parse(m.Groups[2].Value); }

        switch (p.Style)
        {
            case "unity":
                if (p.WinMode == "full") a.Add("-screen-fullscreen 1");
                else if (p.WinMode == "window") a.Add("-screen-fullscreen 0");
                else if (p.WinMode == "borderless") a.Add("-popupwindow");
                if (w > 0) a.Add($"-screen-width {w} -screen-height {h}");
                break;
            case "unreal":
                if (p.WinMode == "full") a.Add("-fullscreen");
                else if (p.WinMode is "window" or "borderless") a.Add("-windowed");
                if (w > 0) a.Add($"-ResX={w} -ResY={h}");
                break;
            default: // source / CS2
                if (p.WinMode == "full") a.Add("-fullscreen");
                else if (p.WinMode == "window") a.Add("-windowed");
                else if (p.WinMode == "borderless") a.Add("-windowed -noborder");
                if (w > 0) a.Add($"-w {w} -h {h}");
                if (p.Hz > 0) a.Add($"-freq {p.Hz}");
                break;
        }
        if (!string.IsNullOrWhiteSpace(p.Extra)) a.Add(p.Extra.Trim());
        return string.Join(" ", a);
    }

    // ---------------- Variáveis e ferramentas do perfil ----------------

    /// <summary>Variáveis de ambiente que mandam o jogo para a GPU dedicada em máquinas com duas placas (PRIME).</summary>
    public static Dictionary<string, string> GpuEnv(IReadOnlyList<GpuDevice> gpus)
    {
        var real = gpus.Where(g => !g.IsVirtual).ToList();
        if (real.Count < 2) return new Dictionary<string, string>();
        if (real.Any(g => g.Vendor == GpuVendor.Nvidia && !g.IsIntegrated))
            return new Dictionary<string, string> { ["__NV_PRIME_RENDER_OFFLOAD"] = "1", ["__GLX_VENDOR_LIBRARY_NAME"] = "nvidia", ["__VK_LAYER_NV_optimus"] = "NVIDIA_only" };
        return new Dictionary<string, string> { ["DRI_PRIME"] = "1" };
    }

    public static Dictionary<string, string> ProfileEnv(GameProfile p, IReadOnlyList<GpuDevice> gpus)
    {
        var env = new Dictionary<string, string>();
        if (p.DedicatedGpu) foreach (var kv in GpuEnv(gpus)) env[kv.Key] = kv.Value;
        if (p.ProtonWayland) env["PROTON_ENABLE_WAYLAND"] = "1";
        return env;
    }

    /// <summary>Programas que "embrulham" o jogo, na ordem: gamemoderun mangohud.</summary>
    public static List<string> Wrappers(GameProfile p)
    {
        var w = new List<string>();
        if (p.GameMode) w.Add("gamemoderun");
        if (p.MangoHud) w.Add("mangohud");
        return w;
    }

    /// <summary>Texto das "Opções de inicialização" da Steam: VARIAVEL=valor gamemoderun mangohud %command% argumentos.</summary>
    public static string BuildSteamOptions(GameProfile p, IReadOnlyList<GpuDevice> gpus)
    {
        var parts = new List<string>();
        foreach (var kv in ProfileEnv(p, gpus)) parts.Add($"{kv.Key}={kv.Value}");
        parts.AddRange(Wrappers(p));
        parts.Add("%command%");
        string args = BuildArgs(p);
        if (args.Length > 0) parts.Add(args);
        return string.Join(" ", parts);
    }

    // ---------------- Executável do jogo ----------------

    private static readonly string[] IgnoredExe =
        { "unins", "crash", "redist", "vc_", "vcredist", "dxsetup", "dotnet", "easyanticheat", "beservice", "cef", "setup", "uninstall", "launcher_helper", "notification" };

    /// <summary>Procura o executável principal na pasta do jogo (nome parecido com o do jogo; senão, o maior). Pode errar: o usuário escolhe outro.</summary>
    public static string? FindMainExe(string dir, string gameName)
    {
        try
        {
            if (!Directory.Exists(Sys.P(dir))) return null;
            var files = new DirectoryInfo(Sys.P(dir)).EnumerateFiles("*", new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 3, IgnoreInaccessible = true })
                .Where(f => !IgnoredExe.Any(i => f.Name.Contains(i, StringComparison.OrdinalIgnoreCase)))
                .Where(f => f.Extension.Equals(".exe", StringComparison.OrdinalIgnoreCase)
                            || (f.Extension is ".sh" or ".x86_64" or ".AppImage" or "" && IsExecutable(f))).ToList();
            if (files.Count == 0) return null;
            string norm = Regex.Replace(gameName.ToLowerInvariant(), "[^a-z0-9]", "");
            var match = files.Where(f => { string n = Regex.Replace(Path.GetFileNameWithoutExtension(f.Name).ToLowerInvariant(), "[^a-z0-9]", ""); return n.Length >= 3 && (norm.Contains(n) || n.Contains(norm)); })
                             .OrderByDescending(f => f.Length).FirstOrDefault();
            return (match ?? files.OrderByDescending(f => f.Length).First()).FullName;
        }
        catch { return null; }
    }

    private static bool IsExecutable(FileInfo f)
    {
        try { return !OperatingSystem.IsWindows() && (f.UnixFileMode & (UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute)) != 0; }
        catch { return false; }
    }

    // ---------------- Iniciar ----------------

    /// <summary>
    /// Aplica o perfil, inicia o jogo e acompanha o processo até ele fechar. Ao terminar (ou cancelar) fecha os programas auxiliares marcados.
    /// Para jogos da Steam, GameMode e MangoHud entram pelas opções de inicialização que o usuário aplicou na Steam.
    /// </summary>
    public static async Task RunAsync(LibGame g, GameProfile p, IReadOnlyList<GpuDevice> gpus, Action<string> status, Action<string> learnedExe, CancellationToken ct)
    {
        var helpers = new List<(Process Proc, bool Close)>();
        try
        {
            string? exe = p.ExePath ?? g.ExePath ?? (g.Store == "Manual" ? null : FindMainExe(g.InstallDir, g.Name));
            foreach (var h in p.Helpers.Where(h => File.Exists(h.Path)))
            {
                status($"Abrindo {Path.GetFileName(h.Path)}…");
                var proc = ProcessRunner.Start(h.Path, ArgSplitter.Split(h.Args));
                if (proc != null) helpers.Add((proc, h.CloseWithGame));
            }
            if (helpers.Count > 0) await Task.Delay(1500, ct);

            status($"Iniciando {g.Name}…");
            if (g.IsSteam)
            {
                if (!Shell.Open($"steam://rungameid/{g.Id}")) throw new InvalidOperationException("Não consegui abrir a Steam (xdg-open ausente ou sem o protocolo steam://).");
            }
            else if (g.LaunchUri != null)
            {
                if (g.Store == "Lutris" && Shell.Has("lutris")) Shell.Run("lutris", new[] { g.LaunchUri }, 5000);
                else if (!Shell.Open(g.LaunchUri)) throw new InvalidOperationException($"Não consegui abrir {g.Store}. Abra o jogo pelo próprio programa.");
            }
            else if (exe != null)
            {
                var cmd = new List<string>(Wrappers(p).Where(Shell.Has));
                if (exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    if (!Shell.Has("wine")) throw new InvalidOperationException("Este jogo é do Windows e o Wine não está instalado (página Instalar apps).");
                    cmd.Add("wine");
                }
                cmd.Add(exe);
                cmd.AddRange(ArgSplitter.Split((g.Args + " " + BuildArgs(p)).Trim()));
                string first = cmd[0];
                var env = ProfileEnv(p, gpus);
                var psi = new ProcessStartInfo(Shell.Which(first) ?? first) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(exe) ?? "" };
                foreach (var a in cmd.Skip(1)) psi.ArgumentList.Add(a);
                foreach (var kv in env) psi.Environment[kv.Key] = kv.Value;
                Process.Start(psi);
            }
            else throw new InvalidOperationException("Não achei o executável deste jogo. Escolha o arquivo em \"Executável\".");

            // espera o processo do jogo aparecer (até 3 min) e acompanha até fechar
            GameDetector.Running? game = null;
            for (int i = 0; i < 120 && game == null; i++)
            {
                await Task.Delay(1500, ct);
                game = GameDetector.Find(g, exe);
            }
            if (game == null) { status("Não detectei o processo do jogo (ele pode ter demorado a abrir)."); return; }

            while (game != null)
            {
                status($"{g.Name} em execução" + (p.GameMode ? " · GameMode" : "") + (p.MangoHud ? " · MangoHud" : "") + ".");
                while (GameDetector.Alive(game.Pid) && !ct.IsCancellationRequested) await Task.Delay(1500, ct);
                game = null;
                // alguns jogos fecham um launcher e abrem o jogo de verdade: espera mais um pouco
                for (int i = 0; i < 6 && game == null; i++)
                {
                    await Task.Delay(1000, ct);
                    game = GameDetector.Find(g, exe);
                }
            }
            status($"{g.Name} encerrado.");
        }
        catch (OperationCanceledException) { status("Acompanhamento cancelado."); }
        catch (Exception ex) { status("Falhou: " + ex.Message); }
        finally
        {
            foreach (var (proc, close) in helpers)
            {
                if (!close) continue;
                try { if (!proc.HasExited) { proc.CloseMainWindow(); if (!proc.WaitForExit(1500)) proc.Kill(); } } catch { }
            }
        }
    }
}

/// <summary>Acha o jogo em execução olhando a linha de comando dos processos (/proc/*/cmdline), sem injetar nada nele.</summary>
public static class GameDetector
{
    public sealed record Running(int Pid, string Name, string Store);

    private static readonly Regex SteamRx = new(@"SteamLaunch\s+AppId=(\d+)", RegexOptions.Compiled);

    /// <summary>"reaper SteamLaunch AppId=730 -- ..." → "730".</summary>
    public static string? SteamAppId(string cmdline)
    {
        var m = SteamRx.Match(cmdline);
        return m.Success ? m.Groups[1].Value : null;
    }

    public static List<(int Pid, string Cmd)> Processes()
    {
        var list = new List<(int, string)>();
        foreach (var n in Sys.List("/proc"))
        {
            if (!int.TryParse(n, out int pid)) continue;
            string? raw = Sys.Read($"/proc/{n}/cmdline");
            if (string.IsNullOrEmpty(raw)) continue;
            list.Add((pid, raw.Replace('\0', ' ').Trim()));
        }
        return list;
    }

    /// <summary>O processo desse jogo, se estiver rodando (Steam pelo AppId; os demais pela pasta de instalação ou pelo executável).</summary>
    public static Running? Find(LibGame g, string? exe = null) => Find(g, exe, Processes());

    public static Running? Find(LibGame g, string? exe, List<(int Pid, string Cmd)> procs)
    {
        string dir = g.InstallDir.Replace('\\', '/').TrimEnd('/');
        foreach (var (pid, cmd) in procs)
        {
            if (pid == Environment.ProcessId) continue;
            string norm = cmd.Replace('\\', '/');
            if (g.IsSteam)
            {
                if (SteamAppId(cmd) == g.Id && !norm.Contains("steamwebhelper")) return new Running(pid, g.Name, g.Store);
                continue;
            }
            if ((exe != null && norm.Contains(exe.Replace('\\', '/'))) || (dir.Length > 3 && norm.Contains(dir + "/")))
                return new Running(pid, g.Name, g.Store);
        }
        return null;
    }

    /// <summary>Qualquer jogo da biblioteca que esteja rodando agora (para o Gargalo e o Overlay).</summary>
    public static Running? Any(IReadOnlyList<LibGame> library)
    {
        var procs = Processes();
        foreach (var g in library)
            if (Find(g, g.ExePath, procs) is { } r) return r;
        return null;
    }

    public static bool Alive(int pid) => Sys.DirExists($"/proc/{pid}");
}
