using Xunit;

namespace InfoPC.Tests;

public class GamesTests
{
    /// <summary>Copia a fixture para uma pasta temporária (testes que gravam arquivos não podem sujar as fixtures).</summary>
    private static string CopyFixture(string name)
    {
        string src = Path.Combine(AppContext.BaseDirectory, "Fixtures", name);
        string dst = Path.Combine(Path.GetTempPath(), "infopc-fx-" + Guid.NewGuid().ToString("N"));
        foreach (var d in Directory.GetDirectories(src, "*", SearchOption.AllDirectories)) Directory.CreateDirectory(d.Replace(src, dst));
        Directory.CreateDirectory(dst);
        foreach (var f in Directory.GetFiles(src, "*", SearchOption.AllDirectories)) File.Copy(f, f.Replace(src, dst), true);
        return dst;
    }

    [Fact]
    public void Steam_library_finds_installed_games_in_every_library_and_skips_tools()
    {
        using var fx = Fx.Use("steam-home");
        Assert.Equal("/home/tester/.local/share/Steam", SteamInfo.SteamPath());
        var libs = SteamInfo.LibraryFolders(SteamInfo.SteamPath()!);
        Assert.Equal(new[] { "/home/tester/.local/share/Steam", "/mnt/jogos/SteamLibrary" }, libs.ToArray());

        var games = GameLibrary.Scan(Array.Empty<CustomGame>());
        Assert.Equal(new[] { "Counter-Strike 2", "ELDEN RING", "Team Fortress 2" }, games.Select(g => g.Name).ToArray());
        Assert.DoesNotContain(games, g => g.Name.StartsWith("Proton") || g.Name.StartsWith("Steamworks") || g.Name == "Portal 2");   // ferramenta / instalação incompleta
        var cs2 = games.Single(g => g.Id == "730");
        Assert.True(cs2.IsSteam);
        Assert.False(cs2.Windows);                       // sem compatdata: roda nativo
        Assert.True(games.Single(g => g.Id == "1245620").Windows);   // compatdata existe: Proton
        Assert.Equal("Steam:730", cs2.Key);
        Assert.EndsWith("/steamapps/common/Counter-Strike Global Offensive", cs2.InstallDir);
        Assert.Contains("Proton", games.Single(g => g.Id == "1245620").Subtitle);
    }

    [Fact]
    public void Manual_programs_are_listed_only_when_the_file_exists()
    {
        string dir = Path.Combine(Path.GetTempPath(), "infopc-manual-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string exe = Path.Combine(dir, "MeuJogo.exe");
        File.WriteAllText(exe, "MZ");
        try
        {
            var list = GameLibrary.Scan(new[] { new CustomGame { Name = "Meu Jogo", Exe = exe }, new CustomGame { Name = "Sumiu", Exe = Path.Combine(dir, "nao-existe") } });
            var g = Assert.Single(list.Where(x => x.Store == "Manual"));
            Assert.Equal("Meu Jogo", g.Name);
            Assert.True(g.Windows);                      // .exe → Wine
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public void Steam_launch_options_roundtrip_with_backup_and_refuse_while_steam_runs()
    {
        string root = CopyFixture("steam-home");
        try
        {
            Sys.Root = root; Sys.Home = "/home/tester";
            Assert.Equal("-novid", SteamInfo.GetLaunchOptions("730"));
            Assert.Null(SteamInfo.GetLaunchOptions("440"));

            Assert.Contains("aberta", SteamInfo.SetLaunchOptions("730", "x", steamRunning: true));     // nunca grava com a Steam aberta
            Assert.Equal("-novid", SteamInfo.GetLaunchOptions("730"));
            string cfg = Directory.GetFiles(root, "localconfig.vdf", SearchOption.AllDirectories).Single();
            string original = File.ReadAllText(cfg);

            Assert.Null(SteamInfo.SetLaunchOptions("730", "gamemoderun mangohud %command% -novid -high", steamRunning: false));
            Assert.Equal("gamemoderun mangohud %command% -novid -high", SteamInfo.GetLaunchOptions("730"));
            Assert.Equal(original, File.ReadAllText(cfg + ".infopc.bak"));
            Assert.Contains("\"playtime\"", File.ReadAllText(cfg));        // o resto do arquivo é preservado

            Assert.Null(SteamInfo.SetLaunchOptions("440", "mangohud %command%", false));                // jogo sem entrada ainda: cria
            Assert.Equal("mangohud %command%", SteamInfo.GetLaunchOptions("440"));
            Assert.Equal(original, File.ReadAllText(cfg + ".infopc.bak"));                                // a cópia continua a de antes do InfoPC
            Assert.Null(SteamInfo.SetLaunchOptions("730", "", false));                                  // vazio remove a chave
            Assert.Null(SteamInfo.GetLaunchOptions("730"));
        }
        finally { Sys.Root = "/"; Sys.Home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile); try { Directory.Delete(root, true); } catch { } }
    }

    [Fact]
    public void Steam_restore_gives_back_the_options_the_game_had_before_infopc()
    {
        string root = CopyFixture("steam-home");
        try
        {
            Sys.Root = root; Sys.Home = "/home/tester";
            int saves = 0;
            void Persist() => saves++;
            var p = new GameProfile();
            Assert.Null(SteamInfo.ApplyLaunchOptions("730", "gamemoderun %command%", p, steamRunning: false, Persist));
            Assert.Equal("-novid", Assert.Single(p.SteamOriginals).Value);
            Assert.Equal(1, saves);                                                           // o original foi gravado antes da Steam
            Assert.Null(SteamInfo.ApplyLaunchOptions("730", "mangohud %command%", p, false, Persist));
            Assert.Equal("-novid", Assert.Single(p.SteamOriginals).Value);                    // a 2ª gravação não troca o original

            Assert.Contains("aberta", SteamInfo.RestoreLaunchOptions("730", p, steamRunning: true, Persist));
            Assert.Equal("-novid", Assert.Single(p.SteamOriginals).Value);                    // recusou: o original continua guardado
            Assert.Null(SteamInfo.RestoreLaunchOptions("730", p, false, Persist));
            Assert.Equal("-novid", SteamInfo.GetLaunchOptions("730"));
            Assert.Empty(p.SteamOriginals);
            Assert.NotNull(SteamInfo.RestoreLaunchOptions("730", p, false, Persist));         // nada mais a restaurar

            var q = new GameProfile();                                                        // jogo sem opções antes
            Assert.Null(SteamInfo.ApplyLaunchOptions("440", "mangohud %command%", q, false, Persist));
            Assert.Equal("", Assert.Single(q.SteamOriginals).Value);
            Assert.Null(SteamInfo.RestoreLaunchOptions("440", q, false, Persist));
            Assert.Null(SteamInfo.GetLaunchOptions("440"));                                   // restaurar remove a chave
        }
        finally { Sys.Root = "/"; Sys.Home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile); try { Directory.Delete(root, true); } catch { } }
    }

    [Fact]
    public void Steam_restore_goes_back_to_the_account_it_changed()
    {
        string root = CopyFixture("steam-home");
        try
        {
            Sys.Root = root; Sys.Home = "/home/tester";
            string a = Directory.GetFiles(root, "localconfig.vdf", SearchOption.AllDirectories).Single();
            string b = a.Replace(Path.Combine("userdata", "12345"), Path.Combine("userdata", "67890"));
            Directory.CreateDirectory(Path.GetDirectoryName(b)!);
            File.WriteAllText(a, File.ReadAllText(a).Replace("-novid", "-conta-A"));
            File.WriteAllText(b, File.ReadAllText(a).Replace("-conta-A", "-conta-B"));
            File.SetLastWriteTimeUtc(a, DateTime.UtcNow);
            File.SetLastWriteTimeUtc(b, DateTime.UtcNow.AddHours(-1));                       // A usou a Steam por último

            var p = new GameProfile();
            Assert.Null(SteamInfo.ApplyLaunchOptions("730", "-infopc", p, false, () => { }));
            File.SetLastWriteTimeUtc(b, DateTime.UtcNow.AddHours(1));                        // agora é B quem entrou na Steam
            Assert.Equal("-conta-B", SteamInfo.GetLaunchOptions("730"));

            Assert.Null(SteamInfo.RestoreLaunchOptions("730", p, false, () => { }));
            Assert.Contains("\"-conta-A\"", File.ReadAllText(a));                             // A recebeu o próprio original
            Assert.Contains("\"-conta-B\"", File.ReadAllText(b));                             // B não foi tocada
            Assert.Empty(p.SteamOriginals);

            Assert.Null(SteamInfo.ApplyLaunchOptions("730", "-infopc", p, false, () => { })); // B (a mais recente)
            File.SetLastWriteTimeUtc(a, DateTime.UtcNow.AddHours(2));
            Assert.Null(SteamInfo.ApplyLaunchOptions("730", "-infopc", p, false, () => { })); // e A: cada conta guarda o seu original
            Assert.Equal(2, p.SteamOriginals.Count);
            Assert.Null(SteamInfo.RestoreLaunchOptions("730", p, false, () => { }));
            Assert.Contains("\"-conta-A\"", File.ReadAllText(a));
            Assert.Contains("\"-conta-B\"", File.ReadAllText(b));
        }
        finally { Sys.Root = "/"; Sys.Home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile); try { Directory.Delete(root, true); } catch { } }
    }

    [Fact]
    public void Steam_is_left_untouched_when_the_original_cannot_be_saved()
    {
        string root = CopyFixture("steam-home");
        try
        {
            Sys.Root = root; Sys.Home = "/home/tester";
            string cfg = Directory.GetFiles(root, "localconfig.vdf", SearchOption.AllDirectories).Single();
            string before = File.ReadAllText(cfg);
            var p = new GameProfile();
            string? err = SteamInfo.ApplyLaunchOptions("730", "-infopc", p, false, () => throw new IOException("disco cheio"));
            Assert.Contains("disco cheio", err);
            Assert.Equal(before, File.ReadAllText(cfg));                                     // a Steam não foi alterada
            Assert.False(File.Exists(cfg + ".infopc.bak"));
            Assert.Empty(p.SteamOriginals);                                                   // nem ficou um original que não está no disco

            Assert.Null(SteamInfo.ApplyLaunchOptions("730", "-infopc", p, false, () => { }));
            File.Delete(cfg);                                                                 // a conta sumiu: o original não é descartado
            Assert.Contains("Não achei", SteamInfo.RestoreLaunchOptions("730", p, false, () => { }));
            Assert.Single(p.SteamOriginals);
        }
        finally { Sys.Root = "/"; Sys.Home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile); try { Directory.Delete(root, true); } catch { } }
    }

    [Fact]
    public void Lutris_json_is_parsed_even_with_warnings_before_it()
    {
        const string json = """
        Warning: something about GTK
        [{"id": 12, "slug": "hades", "name": "Hades", "runner": "wine", "platform": "Windows", "directory": "/home/u/Games/hades", "installed": 1},
         {"id": 13, "slug": "x", "name": "Nao instalado", "runner": "linux", "installed": 0},
         {"id": 14, "slug": "y", "name": "Dosbox Game", "runner": "dosbox", "directory": null, "installed": 1}]
        """;
        var l = GameLibrary.ParseLutris(json);
        Assert.Equal(2, l.Count);
        Assert.Equal("lutris:rungameid/12", l[0].LaunchUri);
        Assert.True(l[0].Windows);
        Assert.False(l[1].Windows);
        Assert.Equal("", l[1].InstallDir);
    }

    [Fact]
    public void Heroic_json_epic_and_gog_formats()
    {
        const string epic = """{"Sugar": {"app_name":"Sugar","title":"Rocket League","install_path":"/home/u/Games/Heroic/RL","is_dlc":false}, "DlcX": {"app_name":"DlcX","is_dlc":true}}""";
        var e = GameLibrary.ParseHeroic(epic, "legendary", "Epic (Heroic)");
        var g = Assert.Single(e);
        Assert.Equal("Rocket League", g.Name);
        Assert.Equal("heroic://launch/legendary/Sugar", g.LaunchUri);

        const string gog = """{"installed":[{"appName":"1207658930","install_path":"/home/u/Games/Heroic/Witcher","platform":"linux"}]}""";
        var o = GameLibrary.ParseHeroic(gog, "gog", "GOG (Heroic)");
        Assert.Equal("Witcher", Assert.Single(o).Name);     // sem título: usa o nome da pasta
        Assert.False(o[0].Windows);
        Assert.Empty(GameLibrary.ParseHeroic("não é json", "gog", "GOG"));
    }

    [Fact]
    public void Graphics_arguments_follow_the_engine_style()
    {
        var src = new GameProfile { Style = "source", WinMode = "borderless", Res = "1920x1080", Hz = 144, Extra = "-novid" };
        Assert.Equal("-windowed -noborder -w 1920 -h 1080 -freq 144 -novid", GameLauncher.BuildArgs(src));
        var unity = new GameProfile { Style = "unity", WinMode = "full", Res = "2560x1440", Hz = 165 };
        Assert.Equal("-screen-fullscreen 1 -screen-width 2560 -screen-height 1440", GameLauncher.BuildArgs(unity));   // Unity não usa -freq
        var ue = new GameProfile { Style = "unreal", WinMode = "window", Res = "1280x720" };
        Assert.Equal("-windowed -ResX=1280 -ResY=720", GameLauncher.BuildArgs(ue));
        Assert.Equal("", GameLauncher.BuildArgs(new GameProfile()));
    }

    [Fact]
    public void Steam_options_combine_env_wrappers_command_and_args()
    {
        var one = new[] { new GpuDevice { Vendor = GpuVendor.Amd, VendorId = "1002", DeviceId = "7480" } };
        var two = new[]
        {
            new GpuDevice { Vendor = GpuVendor.Intel, VendorId = "8086", DeviceId = "4690", IsIntegrated = true },
            new GpuDevice { Vendor = GpuVendor.Nvidia, VendorId = "10DE", DeviceId = "2786" },
        };
        var p = new GameProfile { GameMode = true, MangoHud = true, DedicatedGpu = true, WinMode = "full", Extra = "-novid" };

        Assert.Equal("gamemoderun mangohud %command% -fullscreen -novid", GameLauncher.BuildSteamOptions(p, one));      // uma GPU só: nada de PRIME
        string nv = GameLauncher.BuildSteamOptions(p, two);
        Assert.StartsWith("__NV_PRIME_RENDER_OFFLOAD=1 __GLX_VENDOR_LIBRARY_NAME=nvidia __VK_LAYER_NV_optimus=NVIDIA_only gamemoderun mangohud %command%", nv);
        var amdTwo = new[] { two[0], new GpuDevice { Vendor = GpuVendor.Amd, VendorId = "1002", DeviceId = "7480" } };
        Assert.Contains("DRI_PRIME=1", GameLauncher.BuildSteamOptions(p, amdTwo));

        Assert.Equal("%command%", GameLauncher.BuildSteamOptions(new GameProfile(), two));
        Assert.Contains("PROTON_ENABLE_WAYLAND=1", GameLauncher.BuildSteamOptions(new GameProfile { ProtonWayland = true }, one));
        Assert.Equal(new[] { "gamemoderun" }, GameLauncher.Wrappers(new GameProfile { GameMode = true }));
    }

    [Fact]
    public void Virtual_gpus_do_not_count_as_a_second_gpu()
    {
        var gpus = new[]
        {
            new GpuDevice { Vendor = GpuVendor.Nvidia, VendorId = "10DE", DeviceId = "2786" },
            new GpuDevice { Vendor = GpuVendor.Other, VendorId = "1414", DeviceId = "0006" },    // Hyper-V
        };
        Assert.Empty(GameLauncher.GpuEnv(gpus));
    }

    [Fact]
    public void Argument_splitter_respects_quotes_without_a_shell()
    {
        Assert.Equal(new[] { "-novid", "+map", "de dust" }, ArgSplitter.Split("-novid +map \"de dust\"").ToArray());
        Assert.Equal(new[] { "a", "b c", "" }, ArgSplitter.Split("a 'b c' \"\"").ToArray());
        Assert.Empty(ArgSplitter.Split("   "));
        // nada é interpretado: substituição de comando e ponto e vírgula viram texto comum
        Assert.Equal(new[] { "$(rm", "-rf", "/)", ";", "ls" }, ArgSplitter.Split("$(rm -rf /) ; ls").ToArray());
    }

    [Fact]
    public void Running_game_is_detected_by_steam_app_id_or_install_dir()
    {
        Assert.Equal("730", GameDetector.SteamAppId("/home/u/.steam/ubuntu12_32/reaper SteamLaunch AppId=730 -- /home/u/.steam/ubuntu12_32/steam-launch-wrapper"));
        Assert.Null(GameDetector.SteamAppId("firefox https://example.com"));

        var cs2 = new LibGame { Store = "Steam", Id = "730", Name = "Counter-Strike 2", InstallDir = "/lib/steamapps/common/Counter-Strike Global Offensive" };
        var procs = new List<(int, string)>
        {
            (10, "firefox"), (11, "steam"), (12, "reaper SteamLaunch AppId=440 -- x"), (13, "reaper SteamLaunch AppId=730 -- /x/cs2"),
            (14, "steamwebhelper SteamLaunch AppId=730 steamwebhelper"),
        };
        Assert.Equal(13, GameDetector.Find(cs2, null, procs)!.Pid);

        var manual = new LibGame { Store = "Manual", Id = "/opt/g/game", Name = "G", InstallDir = "/opt/g", ExePath = "/opt/g/game" };
        Assert.Equal(21, GameDetector.Find(manual, "/opt/g/game", new List<(int, string)> { (20, "bash"), (21, "/opt/g/game --windowed") })!.Pid);
        Assert.Null(GameDetector.Find(manual, "/opt/g/game", new List<(int, string)> { (20, "bash") }));

        var win = new LibGame { Store = "Steam", Id = "1", Name = "W", InstallDir = "/lib/steamapps/common/ELDEN RING" };
        var wine = new LibGame { Store = "Lutris", Id = "9", Name = "Hades", InstallDir = "/home/u/Games/hades" };
        Assert.NotNull(GameDetector.Find(wine, null, new List<(int, string)> { (30, @"Z:\home\u\Games\hades\Hades.exe") }));   // Wine mostra caminho do Windows
    }

    [Fact]
    public void MangoHud_config_maps_overlay_items_and_corner()
    {
        string cfg = MangoHud.Build(new[] { "fps", "frametime", "low1", "gpu_load", "gpu_temp", "gpu_perfcap", "cpu_load", "ram_used", "vram_used", "clock" }, "BR", 1.25);
        var lines = cfg.Split('\n').Select(l => l.Trim()).ToList();
        Assert.Contains("position=bottom-right", lines);
        Assert.Contains("font_size=30", lines);
        foreach (var k in new[] { "fps", "frametime", "fps_metrics=avg,0.01", "gpu_stats", "gpu_temp", "cpu_stats", "ram", "vram", "time" }) Assert.Contains(k, lines);
        Assert.DoesNotContain(lines, l => l.Contains("perfcap"));      // sem equivalente no MangoHud: some
        Assert.StartsWith("# Gerado pelo InfoPC", cfg);
        Assert.True(MangoHud.Supports("fps"));
        Assert.False(MangoHud.Supports("gpu_perfcap"));
        Assert.Equal("top-left", MangoHud.Position("TL"));
        Assert.Equal(lines.Distinct().Count(), lines.Count);            // sem linhas repetidas
    }

    [Fact]
    public void MangoHud_write_keeps_a_backup_of_a_foreign_config()
    {
        string home = Path.Combine(Path.GetTempPath(), "infopc-mh-" + Guid.NewGuid().ToString("N"));
        string? oldXdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        try
        {
            Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", Path.Combine(home, "cfg"));
            Sys.Root = "/";
            Directory.CreateDirectory(Path.Combine(home, "cfg", "MangoHud"));
            File.WriteAllText(Path.Combine(home, "cfg", "MangoHud", "MangoHud.conf"), "fps_only\n# meu arquivo\n");
            string path = MangoHud.Write(MangoHud.Build(new[] { "fps" }, "TL", 1));
            Assert.Equal("fps_only\n# meu arquivo\n", File.ReadAllText(path + ".infopc.bak"));
            Assert.StartsWith("# Gerado pelo InfoPC", File.ReadAllText(path));
            MangoHud.Write(MangoHud.Build(new[] { "fps", "gpu_load" }, "TL", 1));    // 2ª vez: arquivo já é nosso, o backup original fica intacto
            Assert.Equal("fps_only\n# meu arquivo\n", File.ReadAllText(path + ".infopc.bak"));
        }
        finally { Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", oldXdg); try { Directory.Delete(home, true); } catch { } }
    }

    [Fact]
    public void Bottleneck_sample_reads_roles_and_names_the_busiest_thread()
    {
        var hub = new SensorHub(new ISensorSource[] { new FakeLoad() });
        hub.Refresh();
        var gpu = new GpuDevice { Index = 0 };
        var s = LinuxBottleneck.Sample(hub, gpu);
        Assert.Equal(96f, s.GpuLoad);
        Assert.Equal(40f, s.CpuTotal);
        Assert.Equal("thread 3", s.BusiestCore);
        Assert.Equal(Limiter.Gpu, s.Classify());
        Assert.Contains("GPU", LinuxBottleneck.Title(Limiter.Gpu));
        Assert.Contains("swap", LinuxBottleneck.Advice(Limiter.Ram));
        Assert.DoesNotContain("Windows", LinuxBottleneck.Advice(Limiter.Ram));
    }

    private sealed class FakeLoad : ISensorSource
    {
        public IEnumerable<SensorSample> Read()
        {
            yield return new SensorSample("g", "GPU", HwKind.Gpu, SensorKind.Load, "GPU Core", 96f, "gpu0.load", 3);
            yield return new SensorSample("c", "CPU", HwKind.Cpu, SensorKind.Load, "CPU Total", 40f, "cpu.load", 3);
            yield return new SensorSample("t2", "CPU", HwKind.Cpu, SensorKind.Load, "CPU Thread #2", 30f);
            yield return new SensorSample("t3", "CPU", HwKind.Cpu, SensorKind.Load, "CPU Thread #3", 88f);
            yield return new SensorSample("m", "CPU", HwKind.Cpu, SensorKind.Load, "CPU Thread mais ocupada", 88f, "cpu.maxcore", 3);
        }
    }
}
