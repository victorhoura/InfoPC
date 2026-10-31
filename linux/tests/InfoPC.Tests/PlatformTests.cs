using Xunit;

namespace InfoPC.Tests;

public class ShellElevateTests
{
    [Fact]
    public void Quote_protects_spaces_and_quotes_for_copy_paste()
    {
        Assert.Equal("simple-arg_1.2", Shell.Quote("simple-arg_1.2"));
        Assert.Equal("'a b'", Shell.Quote("a b"));
        Assert.Equal("'it'\\''s'", Shell.Quote("it's"));
        Assert.Equal("''", Shell.Quote(""));
        Assert.Equal("sudo systemctl 'disable now'", "sudo " + Shell.CommandLine("systemctl", new[] { "disable now" }));
    }

    [Fact]
    public void Root_actions_never_run_a_program_found_only_in_the_users_PATH()
    {
        string dir = Path.Combine(Path.GetTempPath(), "infopc-path-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string name = "infopc-falso-" + Guid.NewGuid().ToString("N")[..8];
        string full = Path.Combine(dir, name);
        File.WriteAllText(full, "#!/bin/sh\n");
        string? old = Environment.GetEnvironmentVariable("PATH");
        try
        {
            Environment.SetEnvironmentVariable("PATH", dir + ":" + old);
            if (!OperatingSystem.IsWindows()) Assert.Equal(full, Shell.Which(name));   // o PATH do usuário acha...
            Assert.Null(Shell.WhichTrusted(name));                                        // ...mas só os diretórios do sistema valem para root
            Assert.Null(Shell.WhichTrusted(full));
            var r = Elevate.Run(name, new[] { "x" });
            Assert.True(r.Unavailable);
            Assert.StartsWith("sudo ", r.ManualCommand);
        }
        finally { Environment.SetEnvironmentVariable("PATH", old); try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public void Shell_override_replaces_execution_and_missing_commands_are_flagged()
    {
        try
        {
            Shell.Override = (f, a) => f == "echo" ? new ProcResult(0, "oi\n", "") : null;
            Assert.Equal("oi\n", Shell.Run("echo", new[] { "x" }).Out);
            Assert.True(Shell.Has("echo"));
        }
        finally { Shell.Override = null; }
        Assert.True(Shell.Run("infopc-comando-que-nao-existe-xyz").NotFound);
    }

    [Fact]
    public void Elevate_maps_pkexec_exit_codes_to_friendly_results()
    {
        try
        {
            Elevate.Override = (_, _) => new ProcResult(126, "", "Error executing command as another user: Not authorized");
            var r = Elevate.Run("dmidecode", new[] { "-t", "memory" });
            if (!Elevate.IsRoot) { Assert.True(r.Cancelled); Assert.Contains("senha", r.Message); }

            Elevate.Override = (_, _) => new ProcResult(0, "tudo certo", "");
            var ok = Elevate.Run("dmidecode", new[] { "-t", "memory" });
            Assert.True(ok.Ok);
            Assert.Equal("tudo certo", ok.Message);
        }
        finally { Elevate.Override = null; }
    }

    [Fact]
    public void Elevate_without_pkexec_returns_the_manual_command()
    {
        if (Elevate.IsRoot) return;
        try
        {
            Shell.Override = (_, _) => ProcResult.Missing("pkexec");
            var r = Elevate.Run("smartctl", new[] { "-a", "/dev/sda" });
            Assert.True(r.Unavailable);
            Assert.Contains("sudo", r.ManualCommand);
            Assert.Contains("/dev/sda", r.ManualCommand);
            Assert.Contains("terminal", r.Message);
        }
        finally { Shell.Override = null; }
    }
}

public class SettingsTests
{
    [Fact]
    public void Settings_roundtrip_in_the_chosen_folder()
    {
        string dir = Path.Combine(Path.GetTempPath(), "infopc-cfg-" + Guid.NewGuid().ToString("N"));
        string old = AppSettings.Folder;
        try
        {
            AppSettings.Folder = dir;
            var s = new AppSettings { HowManyFpsKey = "segredo-123", LightTheme = true };
            s.BenchRecords.Add(new BenchRecord(new DateTime(2026, 10, 25), "cpu", 123.4, "ok"));
            s.GameProfiles["steam:730"] = new GameProfile { GameMode = true, MangoHud = true, Res = "1920x1080" };
            s.Save();

            var back = AppSettings.Load();
            Assert.Equal("segredo-123", back.HowManyFpsKey);
            Assert.True(back.LightTheme);
            Assert.Single(back.BenchRecords);
            Assert.True(back.GameProfiles["steam:730"].GameMode);
            Assert.False(File.Exists(Path.Combine(dir, "settings.json.tmp")));

            if (OperatingSystem.IsLinux())
                Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(Path.Combine(dir, "settings.json")));
        }
        finally { AppSettings.Folder = old; try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public async Task Fps_api_cache_answers_without_internet_and_ignores_stale_entries()
    {
        string dir = Path.Combine(Path.GetTempPath(), "infopc-cfg-" + Guid.NewGuid().ToString("N"));
        string old = AppSettings.Folder;
        try
        {
            Directory.CreateDirectory(dir);
            AppSettings.Folder = dir;
            var now = DateTime.UtcNow;
            File.WriteAllText(Path.Combine(dir, "fps-cache.json"), System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, object>
            {
                ["R5 7600|RTX 4070|Fortnite|1920x1080|Native"] = new { AvgFps = 180.0, Min1Fps = 120.0, Fetched = now.AddDays(-1) },
                ["R5 7600|RTX 4070|Fortnite|2560x1440|Native"] = new { AvgFps = 140.0, Min1Fps = 90.0, Fetched = now.AddDays(-8) },
            }));

            var api = new HowManyFpsClient();   // sem chave: qualquer ida à internet lançaria exceção
            Assert.False(api.Configured);
            Assert.Equal(180.0, api.Cached("R5 7600", "RTX 4070", "Fortnite", "1920x1080", "Native")?.AvgFps);
            Assert.Null(api.Cached("R5 7600", "RTX 4070", "Fortnite", "2560x1440", "Native"));   // mais de 7 dias: vencido
            Assert.Null(api.Cached("R5 7600", "RTX 4070", "Counter-Strike 2", "1920x1080", "Native"));

            Assert.Equal(120.0, (await api.GetAsync("R5 7600", "RTX 4070", "Fortnite", "1920x1080", "Native")).Min1Fps);   // do cache
            await Assert.ThrowsAsync<InvalidOperationException>(() => api.GetAsync("R5 7600", "RTX 4070", "Fortnite", "2560x1440", "Native"));
        }
        finally { AppSettings.Folder = old; try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public void Corrupt_settings_do_not_crash_and_report_the_problem()
    {
        string dir = Path.Combine(Path.GetTempPath(), "infopc-cfg-" + Guid.NewGuid().ToString("N"));
        string old = AppSettings.Folder;
        try
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "settings.json"), "{ isto não é json");
            AppSettings.Folder = dir;
            var s = AppSettings.Load();
            Assert.NotNull(s.LoadError);
            Assert.Empty(s.BenchRecords);
            // o próximo Save sobrescreve o arquivo: o original fica guardado, com os registros de desfazer
            Assert.Equal("{ isto não é json", File.ReadAllText(Path.Combine(dir, "settings.json.corrompido")));
        }
        finally { AppSettings.Folder = old; try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public void Data_folder_honours_the_environment_override()
    {
        string dir = Path.Combine(Path.GetTempPath(), "infopc-env-" + Guid.NewGuid().ToString("N"));
        string? old = Environment.GetEnvironmentVariable("INFOPC_DADOS");
        try
        {
            Environment.SetEnvironmentVariable("INFOPC_DADOS", dir);
            Assert.Equal(dir, AppSettings.ResolveFolder());
            Assert.True(Directory.Exists(dir));
        }
        finally { Environment.SetEnvironmentVariable("INFOPC_DADOS", old); try { Directory.Delete(dir, true); } catch { } }
    }
}

public sealed class DataFolderTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("infopc-pasta").FullName;
    private const string Me = "0123456789abcdef";
    private static readonly DateTime Old = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), Newer = Old.AddDays(1), Newest = Old.AddDays(2);
    private string Exe => Path.Combine(_root, "exe");
    private string Fallback => Path.Combine(_root, "fallback");
    private string Target => Path.Combine(Exe, "dados", Me);

    public DataFolderTests() => Directory.CreateDirectory(Exe);

    public void Dispose() => Directory.Delete(_root, true);

    private string Resolve(out string? notice, string? legacy = null, bool move = false) => DataFolder.Resolve(Exe, Me, Fallback, out notice, legacy, move);

    /// <summary>Um arquivo com a data escolhida: é ela que decide quais dados são os mais novos.</summary>
    private void Put(string path, string text = "x", DateTime? when = null)
    {
        string full = Path.Combine(_root, path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, text);
        File.SetLastWriteTimeUtc(full, when ?? Old);
    }

    private static string Read(string dir, string file = "settings.json") => File.ReadAllText(Path.Combine(dir, file));

    /// <summary>Faz o teste de gravação falhar na pasta, como numa pasta que virou somente leitura, sem mudar permissão nenhuma.</summary>
    private FileStream BlockWrites()
    {
        Directory.CreateDirectory(Target);
        return new FileStream(Path.Combine(Target, ".gravavel-" + Environment.ProcessId), FileMode.Create, FileAccess.Write, FileShare.None);
    }

    [Fact]
    public void Data_goes_next_to_the_executable_and_not_to_the_fallback()
    {
        Assert.Equal(Target, Resolve(out var notice));
        Assert.Null(notice);
        Assert.True(Directory.Exists(Target));
        Assert.Empty(Directory.GetFileSystemEntries(Target));                     // o teste de gravação não deixa nada
    }

    [Fact]
    public void A_folder_that_cannot_be_written_uses_the_fallback()
    {
        Put("exe/dados", "é um arquivo, então não dá para criar a pasta");
        Assert.Equal(Fallback, Resolve(out var notice));
        Assert.Null(notice);                                                      // não havia dados para levar
        Assert.Equal(Fallback, DataFolder.Resolve("", Me, Fallback, out _));
    }

    [Fact]
    public void Identity_separates_machines_and_users_and_is_a_valid_stable_folder_name()
    {
        string id = DataFolder.Identity("maquina-a", "ana");
        Assert.Equal(id, DataFolder.Identity("maquina-a", "ana"));
        Assert.NotEqual(id, DataFolder.Identity("maquina-b", "ana"));
        Assert.NotEqual(id, DataFolder.Identity("maquina-a", "bia"));
        Assert.Matches("^[0-9a-f]{16}$", id);
        Assert.DoesNotContain("ana", id);
        Assert.Equal("7fe7aa63a33f0104", DataFolder.Identity("máquina", "ana"));   // calculado fora do C#: o nome não pode mudar entre versões, ou os dados somem
    }

    [Fact]
    public void Another_machine_does_not_see_the_undo_records_of_this_one()
    {
        // o programa foi levado num pen drive
        string here = DataFolder.Resolve(Exe, DataFolder.Identity("maquina-a", "ana"), Fallback, out _);
        File.WriteAllText(Path.Combine(here, "settings.json"), "desfazer da máquina A");
        string there = DataFolder.Resolve(Exe, DataFolder.Identity("maquina-b", "ana"), Fallback, out _);
        Assert.NotEqual(here, there);
        Assert.False(File.Exists(Path.Combine(there, "settings.json")));
        Assert.Equal(here, DataFolder.Resolve(Exe, DataFolder.Identity("maquina-a", "ana"), Fallback, out _));   // de volta, está tudo lá
    }

    [Fact]
    public void Previous_data_including_undo_backups_is_copied_once_and_the_origin_stays()
    {
        // Windows: %AppData%\InfoPC é a pasta alternativa e também de onde vêm os dados antigos
        Put("fallback/settings.json", "antigo");
        Put("fallback/Startup/20260101-atalho.lnk", "atalho");
        Put("fallback/DriversSeriais/ch340/ch340.inf", "driver");

        Assert.Equal(Target, Resolve(out var notice));
        Assert.Null(notice);                                                      // a primeira cópia não pede atenção
        Assert.Equal("antigo", Read(Target));
        Assert.Equal("atalho", Read(Target, "Startup/20260101-atalho.lnk"));
        Assert.Equal("driver", Read(Target, "DriversSeriais/ch340/ch340.inf"));
        Assert.Equal("antigo", Read(Fallback));                                   // a versão anterior do programa continua funcionando

        // abrir de novo não copia outra vez, nem com a origem igual; e o que mudou depois vale
        Assert.Equal(Target, Resolve(out notice));
        Assert.Null(notice);
        Put($"exe/dados/{Me}/settings.json", "novo", Newer);
        Assert.Equal(Target, Resolve(out notice));
        Assert.Equal("novo", Read(Target));
        Assert.Null(notice);
    }

    [Fact]
    public void Data_in_the_fallback_of_a_previous_version_is_not_left_behind()
    {
        // R14-A: a versão anterior não conseguia gravar ao lado do programa, e os registros estavam em ~/.local/share/InfoPC
        Put("fallback/settings.json", "desfazer no fallback");
        string flat = Path.Combine(Exe, "dados");
        Assert.Equal(Target, Resolve(out _, flat, move: true));
        Assert.Equal("desfazer no fallback", Read(Target));
        Assert.Equal("desfazer no fallback", Read(Fallback));                     // moveLegacy vale só para a antiga pasta comum
    }

    [Fact]
    public void When_the_portable_folder_stops_accepting_writes_the_latest_data_goes_to_the_fallback()
    {
        // R14-B: a cópia antiga em %AppData% escondia o que foi feito depois da primeira cópia
        Put("fallback/settings.json", "antigo");
        Resolve(out _);
        Put($"exe/dados/{Me}/settings.json", "antigo e novo", Newer);
        Put($"exe/dados/{Me}/Startup/novo.lnk", "atalho novo", Newer);

        string selected;
        string? notice;
        using (BlockWrites()) selected = Resolve(out notice);

        Assert.Equal(Fallback, selected);
        Assert.Equal("antigo e novo", Read(Fallback));
        Assert.Equal("atalho novo", Read(Fallback, "Startup/novo.lnk"));
        Assert.Contains(".anterior", notice);
        var kept = Assert.Single(Directory.GetDirectories(_root, "fallback.anterior-*"));
        Assert.Equal("antigo", Read(kept));                                       // nada se perde na troca
    }

    [Fact]
    public void Without_an_old_copy_the_fallback_receives_the_data_and_the_user_is_told()
    {
        Put($"exe/dados/{Me}/settings.json", "desfazer");
        string selected;
        string? notice;
        using (BlockWrites()) selected = Resolve(out notice);
        Assert.Equal(Fallback, selected);
        Assert.Equal("desfazer", Read(Fallback));
        Assert.Contains("Não consigo mais gravar na pasta do programa", notice);
        Assert.Empty(Directory.GetDirectories(_root, "fallback.anterior-*"));
    }

    [Fact]
    public void When_the_portable_folder_is_writable_again_the_data_comes_back_and_nothing_repeats()
    {
        Put($"exe/dados/{Me}/settings.json", "portátil");
        using (BlockWrites()) Resolve(out _);
        Put("fallback/settings.json", "feito enquanto não dava para gravar", Newer);
        File.Delete(Path.Combine(Target, ".gravavel-" + Environment.ProcessId));

        Assert.Equal(Target, Resolve(out var notice));
        Assert.Equal("feito enquanto não dava para gravar", Read(Target));
        Assert.NotNull(notice);
        var kept = Assert.Single(Directory.GetDirectories(Path.Combine(Exe, "dados"), "*.anterior-*"));
        Assert.Equal("portátil", Read(kept));

        Assert.Equal(Target, Resolve(out notice));                                // abrir de novo não troca nada
        Assert.Null(notice);
        Assert.Single(Directory.GetDirectories(Path.Combine(Exe, "dados"), "*.anterior-*"));
    }

    [Fact]
    public void Nowhere_to_write_keeps_reading_the_latest_data_and_warns()
    {
        Put($"exe/dados/{Me}/settings.json", "desfazer");
        Put("fallback", "é um arquivo, então não dá para criar a pasta");
        string selected;
        string? notice;
        using (BlockWrites()) selected = Resolve(out notice);
        Assert.Equal(Target, selected);
        Assert.Equal("desfazer", Read(Target));
        Assert.Contains("Não consigo gravar", notice);
    }

    [Fact]
    public void A_copy_that_fails_keeps_using_the_old_folder_and_leaves_nothing_half_done()
    {
        Put("fallback/settings.json", "antigo");
        Put("fallback/Startup/a.lnk", "a");
        // um arquivo com o nome da pasta temporária faz a cópia falhar
        Directory.CreateDirectory(Path.Combine(Exe, "dados"));
        File.WriteAllText(Target + ".migrando", "impede");

        Assert.Equal(Fallback, Resolve(out var notice));
        Assert.Contains("Não consegui copiar", notice);
        Assert.False(File.Exists(Path.Combine(Target, "settings.json")));
        Assert.Equal("antigo", Read(Fallback));
    }

    [Fact]
    public void An_interrupted_copy_from_a_previous_run_is_redone()
    {
        Put("fallback/settings.json", "completo");
        Put($"exe/dados/{Me}.migrando/settings.json", "pela metade", Newest);     // a pasta temporária nunca conta, mesmo mais nova
        Assert.Equal(Target, Resolve(out _));
        Assert.Equal("completo", Read(Target));
        Assert.False(Directory.Exists(Target + ".migrando"));
    }

    [Fact]
    public void The_old_shared_data_folder_moves_to_this_machine_so_the_next_machine_does_not_adopt_it()
    {
        // Linux antes do D-028: dados/settings.json direto, ao lado de pastas de outras máquinas
        string flat = Path.Combine(Exe, "dados");
        Put("exe/dados/settings.json", "deste PC");
        Put("exe/dados/fps-cache.json", "cache");
        string other = "fedcba9876543210";
        Put($"exe/dados/{other}/settings.json", "de outro PC", Newest);

        Assert.Equal(Target, Resolve(out _, flat, move: true));
        Assert.Equal("deste PC", Read(Target));
        Assert.Equal("cache", Read(Target, "fps-cache.json"));
        Assert.False(File.Exists(Path.Combine(flat, "settings.json")));
        Assert.Equal("de outro PC", Read(Path.Combine(flat, other)));             // o de outro PC fica como estava
        Assert.False(File.Exists(Path.Combine(Target, other, "settings.json")));

        string next = DataFolder.Resolve(Exe, DataFolder.Identity("outra", "ana"), Fallback, out _, flat, moveLegacy: true);
        Assert.False(File.Exists(Path.Combine(next, "settings.json")));
    }

    [Fact]
    public void A_backup_without_settings_next_to_it_counts_by_its_own_date()
    {
        Put("fallback/settings.json", "antigo");
        Put($"exe/dados/{Me}/Startup/atalho.lnk", "backup mais novo", Newer);
        Assert.Equal(Target, Resolve(out _));
        Assert.True(File.Exists(Path.Combine(Target, "Startup", "atalho.lnk")));
        Assert.False(File.Exists(Path.Combine(Target, "settings.json")));
    }

    [Fact]
    public void Nothing_to_bring_over_does_not_touch_anything()
    {
        string flat = Path.Combine(Exe, "dados");
        Assert.Equal(Target, Resolve(out _, flat, move: true));
        Assert.Equal(Target, Resolve(out _, Path.Combine(_root, "nao-existe")));
        Assert.Empty(Directory.GetFileSystemEntries(Target));
    }
}

public class CliTests
{
    private static (int? Code, string Out) Run(params string[] args)
    {
        var sw = new StringWriter();
        int? code = Cli.Run(args, sw);
        return (code, sw.ToString());
    }

    [Fact]
    public void No_arguments_means_open_the_window() => Assert.Null(Cli.Run(Array.Empty<string>()));

    [Fact]
    public void Version_and_help()
    {
        var (code, outp) = Run("--versao");
        Assert.Equal(0, code);
        Assert.Equal(AppInfo.Version, outp.Trim());
        Assert.Matches(@"^\d+\.\d+\.\d+$", AppInfo.Version);

        var (hc, help) = Run("--ajuda");
        Assert.Equal(0, hc);
        Assert.Contains("--relatorio", help);
        Assert.Contains("--diagnostico", help);
        Assert.Contains(AppInfo.Version, help);
    }

    [Fact]
    public void Unknown_flag_is_an_error_but_plain_argument_opens_the_window()
    {
        var (code, outp) = Run("--nada");
        Assert.Equal(2, code);
        Assert.Contains("--ajuda", outp);
        Assert.Null(Cli.Run(new[] { "arquivo.txt" }, new StringWriter()));
    }

    [Fact]
    public void Info_prints_the_system_tree()
    {
        using var _ = Fx.Use("intel-desktop");
        var (code, outp) = Run("--info");
        Assert.Equal(0, code);
        Assert.Contains("SISTEMA OPERACIONAL", outp);
        Assert.Contains("Ubuntu 22.04.4 LTS", outp);
        Assert.Contains("PROCESSADOR", outp);
        Assert.Contains("Núcleos físicos: 4", outp);
    }

    [Fact]
    public void Energy_command_on_a_desktop()
    {
        using var _ = Fx.Use("intel-desktop");
        var (code, outp) = Run("--energia");
        Assert.Equal(0, code);
        Assert.Contains("Tomada (PC de mesa)", outp);
    }

    [Fact]
    public void Report_writes_html_without_serial_numbers()
    {
        using var _ = Fx.Use("intel-desktop");
        string file = Path.Combine(Path.GetTempPath(), "infopc-rel-" + Guid.NewGuid().ToString("N") + ".html");
        try
        {
            var (code, outp) = Run("--relatorio", file);
            Assert.Equal(0, code);
            Assert.Contains("Relatório gravado", outp);
            string html = File.ReadAllText(file);
            Assert.StartsWith("<!doctype html>", html);
            Assert.Contains("Ubuntu 22.04.4 LTS", html);
            Assert.Contains("Sensores", html);
            Assert.Contains("Package id 0", html);
            Assert.DoesNotContain("série", html.Replace("Números de série foram omitidos", ""));
        }
        finally { try { File.Delete(file); } catch { } }
    }

    [Fact]
    public void Json_report_is_valid_json()
    {
        using var fx = Fx.Use("intel-desktop");
        var (code, outp) = Run("--json");
        Assert.Equal(0, code);
        using var doc = System.Text.Json.JsonDocument.Parse(outp);
        Assert.True(doc.RootElement.TryGetProperty("sistema", out _));
        Assert.True(doc.RootElement.TryGetProperty("sensores", out _));
    }

    [Fact]
    public void Diagnose_lists_tools_and_never_throws_on_an_empty_machine()
    {
        using var _ = Fx.Use("raspberry");
        var (code, outp) = Run("--diagnostico");
        Assert.Equal(0, code);
        Assert.Contains("Programas de apoio", outp);
        Assert.Contains("pkexec", outp);
        Assert.Contains("temperatura da CPU: 54", outp);
    }
}

public class SharedCodeTests
{
    [Fact]
    public void Game_estimate_and_cpu_gpu_names_come_from_the_shared_catalog()
    {
        Assert.Equal("RTX 4070", GameCatalog.ShortGpuName("NVIDIA GeForce RTX 4070"));
        Assert.Equal("Ryzen 7 7700X", GameCatalog.ShortCpuName("AMD Ryzen 7 7700X 8-Core Processor"));
        Assert.Equal(100, GameCatalog.GpuPerf("NVIDIA GeForce RTX 4070"));
        var cs2 = GameCatalog.Games.First(g => g.Name == "Counter-Strike 2");
        var e = GameCatalog.Estimate(cs2, 100, 100, "1920x1080", "Alto", "Native");
        Assert.True(e.Avg > 0);
    }

    [Fact]
    public void Vdf_parses_steam_library_files()
    {
        const string vdf = "\"libraryfolders\"\n{\n\t\"0\"\n\t{\n\t\t\"path\"\t\t\"/home/u/.local/share/Steam\"\n\t\t\"apps\"\n\t\t{\n\t\t\t\"730\"\t\t\"28054700000\"\n\t\t}\n\t}\n}\n";
        var root = Vdf.Parse(vdf).Child("libraryfolders")!;
        var lib = root.Children.Single().Value;
        Assert.Equal("/home/u/.local/share/Steam", lib.Get("path"));
        Assert.Equal("28054700000", lib.Child("apps")!.Get("730"));
        Assert.Contains("libraryfolders", Vdf.Serialize(Vdf.Parse(vdf)));
    }

    [Fact]
    public void Vdf_keeps_other_steam_options_byte_for_byte()
    {
        // visto no localconfig.vdf real: o atalho do overlay tem uma tabulação dentro do valor
        const string vdf = "\"UserLocalConfigStore\"\n{\n\t\"friends\"\n\t{\n\t\t\"InGameOverlayShortcutKey\"\t\t\"Shift\tKEY_TAB\"\n\t\t\"path\"\t\t\"C:\\\\Steam\"\n\t\t\"nome\"\t\t\"a \\\"b\\\"\"\n\t}\n}\n";
        Assert.Equal(vdf, Vdf.Serialize(Vdf.Parse(vdf)));
    }

    [Fact]
    public void Bottleneck_classification_rules()
    {
        Assert.Equal(Limiter.Gpu, new LoadSample(95, 40, 60, null, 50, 50, 70, 60, false, null).Classify());
        Assert.Equal(Limiter.Cpu, new LoadSample(60, 50, 92, "núcleo 3", 50, 50, 70, 60, false, null).Classify());
        Assert.Equal(Limiter.Vram, new LoadSample(60, 50, 40, null, 98, 50, 70, 60, false, null).Classify());
        Assert.Equal(Limiter.Thermal, new LoadSample(99, 50, 40, null, 50, 50, 83, 60, true, null).Classify());
        Assert.Equal(Limiter.Capped, new LoadSample(40, 30, 40, null, 50, 50, 60, 55, false, null).Classify());
        Assert.Contains("equilibrado", BottleneckBalance.Compute(100, 100).Text);
    }

    [Fact]
    public void Disk_health_card_defaults()
    {
        var c = new DiskHealthCard { Title = "x" };
        Assert.Equal("SEM DADOS", c.VerdictText);
        Assert.True(c.IsAlert);
        Assert.Equal("SAUDÁVEL", new DiskHealthCard { Verdict = DiskVerdict.Good }.VerdictText);
    }
}
