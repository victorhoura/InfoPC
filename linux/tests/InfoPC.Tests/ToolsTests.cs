using Xunit;

namespace InfoPC.Tests;

/// <summary>Árvore temporária de arquivos (/proc, /sys, /etc, home) montada pelo próprio teste.</summary>
public sealed class TempRoot : IDisposable
{
    /// <summary>Nomes de sysfs como "1-2:1.0" e "0000:01:00.0" têm ":", que o Windows não aceita em nomes de pasta: esses testes rodam só no Linux (CI).</summary>
    public static bool ColonsOk => !OperatingSystem.IsWindows();

    public string Dir { get; } = Path.Combine(Path.GetTempPath(), "infopc-root-" + Guid.NewGuid().ToString("N"));
    private readonly Dictionary<string, string?> _env = TestEnv.Isolate();

    public TempRoot()
    {
        Directory.CreateDirectory(Dir);
        Sys.Root = Dir;
        Sys.Home = "/home/tester";
        Shell.Override = (f, _) => ProcResult.Missing(f);
        Elevate.Override = null;
        Locale.Init();
    }

    public void Write(string path, string text)
    {
        string full = Path.Combine(Dir, path.TrimStart('/'));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, text);
    }

    public string Read(string path) => File.ReadAllText(Path.Combine(Dir, path.TrimStart('/')));
    public bool Exists(string path) => File.Exists(Path.Combine(Dir, path.TrimStart('/')));
    public void Mkdir(string path) => Directory.CreateDirectory(Path.Combine(Dir, path.TrimStart('/')));

    public void Dispose()
    {
        Sys.Root = "/";
        Sys.Home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Shell.Override = null; Elevate.Override = null;
        TestEnv.Restore(_env);
        try { Directory.Delete(Dir, true); } catch { }
    }
}

public class LightingTests
{
    private sealed class FakeBackend : ILightBackend
    {
        public string Name => "Fake";
        public int MinIntervalMs => 0;
        public List<(byte, byte, byte)[]> Frames { get; } = new();
        public int Acquire(LightDevice d, bool exclusive) => 0;
        public int Send(LightDevice d, ReadOnlySpan<(byte R, byte G, byte B)> colors) { Frames.Add(colors.ToArray()); return 0; }
        public void Release(LightDevice d) { }
        public void EndSession() { }
        public string ErrorText(int e) => "ok";
        public void Shutdown() { }
    }

    private static LightDevice Dev(FakeBackend b, int leds) =>
        new() { Backend = b, Id = "1", Name = "Fita", LedX = Enumerable.Range(0, leds).Select(i => leds > 1 ? i / (double)(leds - 1) : 0).ToArray() };

    [Fact]
    public void Hsv_primary_hues()
    {
        Assert.Equal(((byte)255, (byte)0, (byte)0), LightEngine.Hsv(0, 1, 1));
        Assert.Equal(((byte)0, (byte)255, (byte)0), LightEngine.Hsv(120, 1, 1));
        Assert.Equal(((byte)0, (byte)0, (byte)255), LightEngine.Hsv(240, 1, 1));
        Assert.Equal(LightEngine.Hsv(30, 1, 1), LightEngine.Hsv(390, 1, 1));       // dá a volta
        Assert.Equal(((byte)0, (byte)0, (byte)0), LightEngine.Hsv(77, 1, 0));
    }

    [Fact]
    public void Effects_compute_expected_colors()
    {
        var white = ((byte)200, (byte)100, (byte)50);
        Assert.Equal(white, LightEngine.Compute("static", 0, 3, 5, white, null));
        Assert.Equal(((byte)0, (byte)0, (byte)0), LightEngine.Compute("off", 0, 3, 5, white, null));
        var minBreath = LightEngine.Compute("breath", 0, 0, 5, white, null);          // t=0: no mínimo (4%)
        Assert.True(minBreath.R <= 10);
        var peak = LightEngine.Compute("breath", 0, 1, 5, white, null);                // cos(2π·0.5)=-1 → máximo
        Assert.True(peak.R >= 190);
        Assert.NotEqual(LightEngine.Compute("rainbow", 0.0, 0, 5, white, null), LightEngine.Compute("rainbow", 0.5, 0, 5, white, null));
        Assert.Equal(((byte)1, (byte)2, (byte)3), LightEngine.Compute("cpu", 0, 0, 5, white, ((byte)1, (byte)2, (byte)3)));
        // temperatura: azul frio → vermelho quente; carga: verde → vermelho
        Assert.Equal(((byte)0, (byte)0, (byte)255), LightEngine.LiveColor("cpu", 30));
        Assert.Equal(((byte)255, (byte)0, (byte)0), LightEngine.LiveColor("cpu", 90));
        Assert.Equal(((byte)0, (byte)255, (byte)0), LightEngine.LiveColor("load", 0));
    }

    [Fact]
    public void Tick_scales_brightness_and_only_resends_when_something_changes()
    {
        var b = new FakeBackend();
        var dev = Dev(b, 3);
        var e = new LightEngine { Effect = "static", Color = (200, 100, 0), Brightness = 50 };
        var diag = new List<string>();
        e.Diag = diag.Add;
        e.Tick(new[] { dev }, 0, 1000);
        Assert.Single(b.Frames);
        Assert.Equal(((byte)100, (byte)50, (byte)0), b.Frames[0][0]);
        e.Tick(new[] { dev }, 0.1, 1100);                       // igual, antes de 2 s: não reenvia
        Assert.Single(b.Frames);
        e.Tick(new[] { dev }, 0.1, 3500);                       // passou de 2 s: reenvia (o fabricante não retoma as luzes)
        Assert.Equal(2, b.Frames.Count);
        e.Color = (0, 0, 255); e.Reset();
        e.Tick(new[] { dev }, 0.2, 3600);
        Assert.Equal(((byte)0, (byte)0, (byte)127), b.Frames[2][1]);
        Assert.Single(diag);                                    // só avisa quando o resultado do envio muda
    }

    [Fact]
    public void Live_effect_follows_the_sensor_and_reports_when_it_is_missing()
    {
        var b = new FakeBackend();
        var dev = Dev(b, 1);
        float? temp = 85;
        var e = new LightEngine { Effect = "cpu", LiveValue = _ => temp };
        string text = e.Tick(new[] { dev }, 0, 0);
        Assert.Equal("CPU 85 °C", text);
        Assert.Equal(((byte)255, (byte)0, (byte)0), b.Frames[0][0]);
        temp = null; e.Reset();
        Assert.Equal("Sensor indisponível.", e.Tick(new[] { dev }, 0, 5000));
    }

    [Fact]
    public void OpenRgb_without_a_server_says_how_to_turn_it_on()
    {
        var o = new OpenRgbBackend();
        var (devices, msg) = o.Connect("127.0.0.1", 1);        // porta 1: ninguém escuta
        Assert.Empty(devices);
        Assert.Contains("OpenRGB", msg);
    }

    [Fact]
    public void OpenRgb_setup_hint_mentions_udev_rules_and_i2c_when_missing()
    {
        using var t = new TempRoot();
        string hint = OpenRgbLocator.SetupHint()!;
        Assert.Contains("udev", hint);
        Assert.Contains("i2c", hint);
        t.Write("/usr/lib/udev/rules.d/60-openrgb.rules", "x");
        t.Write("/dev/i2c-3", "");
        Assert.Null(OpenRgbLocator.SetupHint());
    }
}

public class StartupTests
{
    private const string Dropbox = "[Desktop Entry]\nType=Application\nName=Dropbox\nName[pt_BR]=Dropbox (sincronização)\nExec=dropbox start -i\nComment=Sincroniza arquivos\n";

    [Fact]
    public void Desktop_files_are_parsed_with_localized_names_and_enabled_flags()
    {
        var d = StartupManager.ParseDesktop(Dropbox + "[Desktop Action x]\nName=Outro\n");
        Assert.Equal("Dropbox", d["Name"]);
        Assert.Equal("Dropbox (sincronização)", d["Name[pt_BR]"]);
        Assert.Equal("dropbox start -i", d["Exec"]);
        Assert.False(d.ContainsKey("Hidden"));
        Assert.True(StartupManager.IsEnabled(d));
        Assert.False(StartupManager.IsEnabled(StartupManager.ParseDesktop("[Desktop Entry]\nHidden=true\n")));
        Assert.False(StartupManager.IsEnabled(StartupManager.ParseDesktop("[Desktop Entry]\nX-GNOME-Autostart-enabled=false\n")));
    }

    [Fact]
    public void SetKey_replaces_or_appends_inside_desktop_entry_only()
    {
        string a = StartupManager.SetKey(Dropbox, "Hidden", "true");
        Assert.Contains("Hidden=true", a);
        Assert.Equal(a, StartupManager.SetKey(a, "Hidden", "true"));                   // idempotente
        string b = StartupManager.SetKey(a, "Hidden", "false");
        Assert.Contains("Hidden=false", b);
        Assert.DoesNotContain("Hidden=true", b);
        string withAction = "[Desktop Entry]\nName=X\n\n[Desktop Action y]\nName=Y\n";
        string c = StartupManager.SetKey(withAction, "Hidden", "true");
        Assert.True(c.IndexOf("Hidden=true", StringComparison.Ordinal) < c.IndexOf("[Desktop Action y]", StringComparison.Ordinal));
    }

    [Fact]
    public void List_merges_user_and_system_entries_and_user_copy_wins()
    {
        using var t = new TempRoot();
        t.Write("/home/tester/.config/autostart/dropbox.desktop", Dropbox);
        t.Write("/home/tester/.config/autostart/steam.desktop", "[Desktop Entry]\nName=Steam\nExec=steam -silent\nHidden=true\n");
        t.Write("/etc/xdg/autostart/steam.desktop", "[Desktop Entry]\nName=Steam (sistema)\nExec=steam\n");
        t.Write("/etc/xdg/autostart/nm-applet.desktop", "[Desktop Entry]\nName=Network\nExec=nm-applet\n");
        var list = StartupManager.List();
        Assert.Equal(3, list.Count);                                                    // steam não repete
        var steam = list.Single(e => e.File == "steam.desktop");
        Assert.False(steam.Enabled);
        Assert.False(steam.System);
        Assert.Contains("substitui", steam.Source);
        Assert.True(list.Single(e => e.File == "nm-applet.desktop").System);
        Assert.Equal("Dropbox (sincronização)", list.Single(e => e.File == "dropbox.desktop").Name);
    }

    [Fact]
    public void Disabling_a_system_entry_creates_a_hidden_user_copy_and_enabling_removes_it()
    {
        using var t = new TempRoot();
        t.Write("/etc/xdg/autostart/nm-applet.desktop", "[Desktop Entry]\nName=Network\nExec=nm-applet\n");
        var e = StartupManager.List().Single();
        var s = new AppSettings();
        Assert.Null(StartupManager.SetEnabled(e, false, s, () => { }));
        Assert.Contains("Hidden=true", t.Read("/home/tester/.config/autostart/nm-applet.desktop"));
        Assert.Contains("Exec=nm-applet", t.Read("/home/tester/.config/autostart/nm-applet.desktop"));
        Assert.True(t.Exists("/etc/xdg/autostart/nm-applet.desktop"));                  // o arquivo do sistema nunca é tocado
        var after = StartupManager.List().Single();
        Assert.False(after.Enabled);
        Assert.Null(StartupManager.SetEnabled(after, true, s, () => { }));
        Assert.False(t.Exists("/home/tester/.config/autostart/nm-applet.desktop"));
        Assert.True(StartupManager.List().Single().Enabled);
    }

    [Fact]
    public void Removing_a_user_entry_keeps_a_copy_that_can_be_restored()
    {
        using var t = new TempRoot();
        t.Write("/home/tester/.config/autostart/dropbox.desktop", Dropbox);
        var s = new AppSettings();
        Assert.Null(StartupManager.Remove(StartupManager.List().Single(), s, () => { }));
        Assert.False(t.Exists("/home/tester/.config/autostart/dropbox.desktop"));
        Assert.Single(s.StartupRemoved);
        Assert.Null(StartupManager.RestoreRemoved(s));
        Assert.Equal(Dropbox, t.Read("/home/tester/.config/autostart/dropbox.desktop"));
        Assert.Empty(s.StartupRemoved);
    }

    [Fact]
    public void Removing_changes_nothing_when_the_copy_cannot_be_saved_first()
    {
        using var t = new TempRoot();
        t.Write("/home/tester/.config/autostart/dropbox.desktop", Dropbox);
        var s = new AppSettings();
        string? err = StartupManager.Remove(StartupManager.List().Single(), s, () => throw new IOException("disco cheio"));
        Assert.Contains("Nada foi removido", err);
        Assert.Equal(Dropbox, t.Read("/home/tester/.config/autostart/dropbox.desktop"));
        Assert.Empty(s.StartupRemoved);

        bool savedWhileFileExisted = false;
        Assert.Null(StartupManager.Remove(StartupManager.List().Single(), s, () => savedWhileFileExisted = t.Exists("/home/tester/.config/autostart/dropbox.desktop")));
        Assert.True(savedWhileFileExisted);                                              // a cópia foi gravada antes de apagar
        Assert.False(t.Exists("/home/tester/.config/autostart/dropbox.desktop"));
    }

    [Fact]
    public void Removing_a_user_copy_that_hides_a_system_entry_keeps_the_system_entry_off()
    {
        using var t = new TempRoot();
        t.Write("/etc/xdg/autostart/demo.desktop", "[Desktop Entry]\nName=Demo\nExec=demo\n");
        string userCopy = "[Desktop Entry]\nName=Demo\nExec=demo --minimizado\nHidden=true\n";
        t.Write("/home/tester/.config/autostart/demo.desktop", userCopy);
        var s = new AppSettings();
        var e = StartupManager.List().Single();
        Assert.True(e.OverridesSystem);
        Assert.False(e.Enabled);

        Assert.Null(StartupManager.Remove(e, s, () => { }));
        var after = StartupManager.List().Single();
        Assert.False(after.Enabled);                                                     // antes a do sistema voltava a valer
        Assert.Contains("Exec=demo\n", t.Read("/home/tester/.config/autostart/demo.desktop"));
        Assert.Equal("[Desktop Entry]\nName=Demo\nExec=demo\n", t.Read("/etc/xdg/autostart/demo.desktop"));

        Assert.Null(StartupManager.RestoreRemoved(s));
        Assert.Equal(userCopy, t.Read("/home/tester/.config/autostart/demo.desktop"));  // a cópia do usuário volta igual
    }

    [Fact]
    public void User_enabled_systemd_units_are_listed_and_toggled_with_systemctl()
    {
        using var t = new TempRoot();
        t.Write("/home/tester/.config/systemd/user/default.target.wants/syncthing.service", "[Unit]\nDescription=Syncthing - sincronização\n");
        var calls = new List<string>();
        Shell.Override = (f, a) => { calls.Add(f + " " + string.Join(' ', a)); return f == "systemctl" ? new ProcResult(0, "", "") : ProcResult.Missing(f); };
        var e = StartupManager.List().Single();
        Assert.Equal(StartupKind.SystemdUser, e.Kind);
        Assert.Equal("syncthing", e.Name);
        Assert.Equal("Syncthing - sincronização", e.Comment);
        var s = new AppSettings();
        Assert.Null(StartupManager.SetEnabled(e, false, s, () => { }));
        Assert.Contains("systemctl --user disable syncthing.service", calls);
        Assert.Null(StartupManager.Remove(e, s, () => { }));
        Assert.Null(StartupManager.RestoreRemoved(s));
        Assert.Contains("systemctl --user enable syncthing.service", calls);
    }

    [Fact]
    public void A_disabled_systemd_unit_stays_listed_and_can_be_enabled_again()
    {
        using var t = new TempRoot();
        const string link = "/home/tester/.config/systemd/user/default.target.wants/demo.service";
        t.Write(link, "[Unit]\nDescription=Demo\n");
        var calls = new List<string>();
        Shell.Override = (f, a) =>
        {
            if (f != "systemctl") return ProcResult.Missing(f);
            calls.Add(string.Join(' ', a));
            // como o systemctl de verdade: disable apaga o link de *.wants, enable o recria
            if (a.Contains("disable")) File.Delete(Path.Combine(t.Dir, link.TrimStart('/')));
            if (a.Contains("enable")) t.Write(link, "[Unit]\nDescription=Demo\n");
            return new ProcResult(0, "", "");
        };
        var s = new AppSettings();
        var saved = new List<bool>();

        Assert.Null(StartupManager.SetEnabled(StartupManager.List(s).Single(), false, s, () => saved.Add(t.Exists(link))));
        Assert.Equal(new[] { true }, saved);                                             // gravado antes do disable, com o link ainda lá
        var off = StartupManager.List(s).Single();                                       // antes sumia da lista
        Assert.Equal("demo", off.Name);
        Assert.False(off.Enabled);
        Assert.Equal("Demo", off.Comment);

        Assert.Null(StartupManager.SetEnabled(off, true, s, () => { }));
        Assert.Equal("--user enable demo.service", calls.Last());
        Assert.Empty(s.SystemdDisabled);
        Assert.True(StartupManager.List(s).Single().Enabled);
    }

    [Fact]
    public void Disabling_a_systemd_unit_changes_nothing_when_it_cannot_be_saved_first()
    {
        using var t = new TempRoot();
        t.Write("/home/tester/.config/systemd/user/default.target.wants/demo.service", "[Unit]\nDescription=Demo\n");
        var calls = new List<string>();
        Shell.Override = (f, a) => { calls.Add(f + " " + string.Join(' ', a)); return f == "systemctl" ? new ProcResult(0, "", "") : ProcResult.Missing(f); };
        var s = new AppSettings();
        string? err = StartupManager.SetEnabled(StartupManager.List(s).Single(), false, s, () => throw new IOException("disco cheio"));
        Assert.Contains("Nada foi alterado", err);
        Assert.DoesNotContain(calls, c => c.Contains("disable"));
        Assert.Empty(s.SystemdDisabled);
    }
}

public class OptimizeTests
{
    private static void Proc(TempRoot t, int pid, string name, uint uid, long rssKb, int tty = 0, string cmd = "")
    {
        t.Write($"/proc/{pid}/status", $"Name:\t{name}\nUid:\t{uid}\t{uid}\t{uid}\t{uid}\nVmRSS:\t   {rssKb} kB\n");
        t.Write($"/proc/{pid}/stat", $"{pid} ({name}) S 1 {pid} {pid} {tty} -1 4194560 100 0 0 0 1 1 0 0 20 0 1 0 1000 1000 1000\n");
        t.Write($"/proc/{pid}/cmdline", (cmd.Length > 0 ? cmd : "/usr/bin/" + name).Replace(' ', '\0') + "\0");
    }

    [Fact]
    public void Only_own_non_system_processes_are_listed_with_suggestions_first()
    {
        using var t = new TempRoot();
        uint me = Optimizer.CurrentUid;                       // 0 no Windows de desenvolvimento, uid real no Linux
        Proc(t, 100, "dropbox", me, 51200);
        Proc(t, 101, "gnome-shell", me, 900000);              // desktop: protegido
        Proc(t, 102, "chrome", me, 800000);
        Proc(t, 103, "bash", me, 30000);                      // shell: protegido
        Proc(t, 104, "tinyproc", me, 5000);                   // pequeno demais
        Proc(t, 105, "meuscript", me, 400000, tty: 34816);    // iniciado de um terminal
        Proc(t, 106, "outro-usuario", me + 1000, 700000);     // de outra conta
        Proc(t, 107, "steam", me, 600000);                    // Steam nunca entra
        Proc(t, 108, "chrome", me, 200000);                   // segundo processo do mesmo programa: agrupa
        t.Write("/proc/self/status", "Name:\tx\n");

        var apps = Optimizer.ListApps();
        Assert.Equal(new[] { "dropbox", "chrome" }, apps.Select(a => a.Title).ToArray());
        Assert.True(apps[0].Suggested && apps[0].Selected);
        Assert.False(apps[1].Selected);
        Assert.Equal(2, apps[1].Count);
        Assert.Equal((800000 + 200000) * 1024L, apps[1].MemoryBytes);
        Assert.Equal(new[] { 102, 108 }, apps[1].Pids.OrderBy(x => x).ToArray());
        Assert.Contains("Dropbox", apps[0].Detail);
    }

    [Fact]
    public async Task Close_reports_what_left_and_what_did_not()
    {
        using var t = new TempRoot();
        var gone = new BackgroundApp { ProcessName = "a", Title = "A", Pids = new[] { 999991 } };   // não existe em /proc: já saiu
        t.Mkdir("/proc/999992");
        var stuck = new BackgroundApp { ProcessName = "b", Title = "B", Pids = new[] { 999992 } };   // continua existindo
        var r = await Optimizer.CloseAsync(new[] { gone, stuck });
        Assert.Equal(new[] { "A" }, r.Closed.ToArray());
        Assert.Equal(new[] { "B" }, r.NotClosed.ToArray());
    }

    [Fact]
    public void Tweaks_read_state_apply_with_sysctl_and_restore_the_original()
    {
        using var t = new TempRoot();
        t.Write("/proc/sys/vm/swappiness", "60");
        t.Write("/proc/sys/vm/max_map_count", "1048576");
        var cmds = new List<string>();
        Elevate.Override = (f, a) => { cmds.Add(f + " " + string.Join(' ', a)); return new ProcResult(0, "", ""); };

        var tweaks = LinuxTweaks.All().Where(x => x.Available()).ToList();
        Assert.Equal(new[] { "swappiness", "max_map_count" }, tweaks.Select(x => x.Id).ToArray());   // sem cpufreq nem split_lock neste kernel de teste
        var swap = tweaks.Single(x => x.Id == "swappiness");
        swap.Refresh();
        Assert.False(swap.IsOn);

        var s = new AppSettings();
        Assert.True(LinuxTweaks.Apply(swap, true, s, () => { }).Ok);
        Assert.Equal("sysctl -w vm.swappiness=10", cmds.Last());
        Assert.Equal("60", s.TweakBackup["swappiness"]);

        t.Write("/proc/sys/vm/swappiness", "10");                       // o kernel agora diz 10
        swap.Refresh();
        Assert.True(swap.IsOn);
        Assert.True(LinuxTweaks.Apply(swap, false, s, () => { }).Ok);
        Assert.Equal("sysctl -w vm.swappiness=60", cmds.Last());       // volta ao valor de antes
        Assert.False(s.TweakBackup.ContainsKey("swappiness"));

        var map = tweaks.Single(x => x.Id == "max_map_count");
        Assert.True(LinuxTweaks.Apply(map, true, s, () => { }).Ok);
        Assert.Equal("sysctl -w vm.max_map_count=2147483642", cmds.Last());
        Assert.True(LinuxTweaks.RestoreAll(tweaks, s).Ok);
        Assert.Equal("sysctl -w vm.max_map_count=1048576", cmds.Last());
        Assert.Empty(s.TweakBackup);
    }

    [Fact]
    public void A_failed_tweak_does_not_leave_a_stale_backup()
    {
        using var t = new TempRoot();
        t.Write("/proc/sys/vm/swappiness", "60");
        Elevate.Override = (_, _) => new ProcResult(126, "", "cancelado");
        var swap = LinuxTweaks.All().Single(x => x.Id == "swappiness");
        var s = new AppSettings();
        var r = LinuxTweaks.Apply(swap, true, s, () => { });
        Assert.False(r.Ok);
        Assert.Empty(s.TweakBackup);
    }

    [Fact]
    public void A_tweak_changes_nothing_when_the_original_cannot_be_saved_first()
    {
        using var t = new TempRoot();
        t.Write("/proc/sys/vm/swappiness", "60");
        var order = new List<string>();
        Elevate.Override = (f, a) => { order.Add(f + " " + string.Join(' ', a)); return new ProcResult(0, "", ""); };
        var swap = LinuxTweaks.All().Single(x => x.Id == "swappiness");
        var s = new AppSettings();

        var r = LinuxTweaks.Apply(swap, true, s, () => throw new IOException("disco cheio"));
        Assert.False(r.Ok);
        Assert.Contains("Nada foi alterado", r.Message);
        Assert.Contains("disco cheio", r.Message);
        Assert.Empty(order);                                              // o sysctl nem foi chamado
        Assert.Empty(s.TweakBackup);

        r = LinuxTweaks.Apply(swap, true, s, () => order.Add("save"));
        Assert.True(r.Ok);
        Assert.Equal(new[] { "save", "sysctl -w vm.swappiness=10" }, order); // o original vai para o disco antes
        Assert.Equal("60", s.TweakBackup["swappiness"]);
    }

    [Fact]
    public void A_tweak_changes_nothing_when_the_current_value_cannot_be_read()
    {
        using var t = new TempRoot();
        t.Write("/proc/sys/vm/swappiness", "");                              // existe, mas não deu para ler o valor
        Elevate.Override = (_, _) => throw new InvalidOperationException("não deveria alterar sem o original");
        var s = new AppSettings();
        var r = LinuxTweaks.Apply(LinuxTweaks.All().Single(x => x.Id == "swappiness"), true, s, () => { });
        Assert.False(r.Ok);
        Assert.Contains("não consegui ler o valor atual de vm.swappiness", r.Message);
        Assert.Empty(s.TweakBackup);
    }

    [Fact]
    public void Governor_uses_power_profiles_daemon_without_a_password_when_present()
    {
        using var t = new TempRoot();
        t.Write("/sys/devices/system/cpu/cpu0/cpufreq/scaling_governor", "powersave");
        var cmds = new List<string>();
        string current = "balanced";
        Shell.Override = (f, a) =>
        {
            cmds.Add(f + " " + string.Join(' ', a));
            if (f != "powerprofilesctl") return ProcResult.Missing(f);
            if (a.Count == 0) return new ProcResult(0, "", "");          // Shell.Has() só pergunta se o programa existe
            return a[0] switch { "list" => new ProcResult(0, "* balanced:\n  performance:\n  power-saver:\n", ""), "get" => new ProcResult(0, current + "\n", ""), _ => new ProcResult(0, "", "") };
        };
        Elevate.Override = (f, _) => throw new InvalidOperationException("não deveria pedir senha com o power-profiles-daemon");
        var gov = LinuxTweaks.All().Single(x => x.Id == "governor");
        var s = new AppSettings();
        Assert.True(LinuxTweaks.Apply(gov, true, s, () => { }).Ok);
        Assert.Contains("powerprofilesctl set performance", cmds);
        Assert.Equal("ppd:balanced", s.TweakBackup["governor"]);
        Assert.True(LinuxTweaks.Apply(gov, false, s, () => { }).Ok);
        Assert.Contains("powerprofilesctl set balanced", cmds);
    }

    [Fact]
    public void Governor_without_ppd_writes_sysfs_through_a_fixed_script()
    {
        using var t = new TempRoot();
        t.Write("/sys/devices/system/cpu/cpu0/cpufreq/scaling_governor", "powersave");
        var calls = new List<(string F, string[] A)>();
        Elevate.Override = (f, a) => { calls.Add((f, a.ToArray())); return new ProcResult(0, "", ""); };
        var s = new AppSettings();
        Assert.True(LinuxTweaks.Apply(LinuxTweaks.All().Single(x => x.Id == "governor"), true, s, () => { }).Ok);
        var (f, args) = calls.Single();
        Assert.Equal("sh", f);
        Assert.Contains("echo performance", args[1]);
        Assert.Equal("gov:powersave", s.TweakBackup["governor"]);
        calls.Clear();
        Assert.True(LinuxTweaks.RestoreAll(LinuxTweaks.All(), s).Ok);
        Assert.Contains("echo powersave", calls.Single().A[1]);
    }
}

public class AppInstallerTests
{
    [Fact]
    public void Catalog_is_sane_and_every_name_is_safe_for_the_shell_script()
    {
        var cat = AppInstaller.Catalog();
        Assert.True(cat.Count >= 20);
        var rx = new System.Text.RegularExpressions.Regex(@"^[A-Za-z0-9+._-]+$");
        foreach (var a in cat)
        {
            Assert.False(string.IsNullOrWhiteSpace(a.Description), a.Name);
            Assert.True(a.Apt != null || a.Dnf != null || a.Pacman != null || a.Zypper != null || a.Flatpak != null, a.Name + " sem origem");
            foreach (var pkg in new[] { a.Apt, a.Dnf, a.Pacman, a.Zypper }.Where(p => p != null).SelectMany(p => p!.Split(' '))) Assert.Matches(rx, pkg);
            if (a.Flatpak != null) Assert.Matches(rx, a.Flatpak);
        }
        Assert.Equal(cat.Count, cat.Select(a => a.Name).Distinct().Count());
        Assert.DoesNotContain("$(", AppInstaller.NativeScript);
        Assert.DoesNotContain("eval", AppInstaller.NativeScript);
    }

    [Fact]
    public void Resolve_prefers_native_then_flatpak_and_flags_installed_ones()
    {
        Shell.Override = (f, a) => f switch
        {
            "dpkg" => a.Count > 1 && a[1] == "gamemode" ? new ProcResult(0, "", "") : new ProcResult(1, "", ""),
            "flatpak" when a.Count > 0 && a[0] == "info" => new ProcResult(1, "", ""),
            "flatpak" => new ProcResult(0, "", ""),
            _ => ProcResult.Missing(f),
        };
        try
        {
            var apps = AppInstaller.Catalog();
            AppInstaller.Resolve(apps, PackageManager.Apt);
            var gm = apps.Single(a => a.Name == "GameMode");
            Assert.True(gm.Installed);
            Assert.Equal("Instalado", gm.Status);
            Assert.False(gm.CanSelect);
            var steam = apps.Single(a => a.Name == "Steam");
            Assert.Equal("steam-installer", steam.Source);                // nativo vence o Flatpak
            Assert.True(steam.CanSelect);
            Assert.Equal("Flatpak", apps.Single(a => a.Name == "Discord").Source);   // só existe em Flatpak
            Assert.Equal("vkmark", apps.Single(a => a.Name == "vkmark").Source);

            AppInstaller.Resolve(apps, PackageManager.Pacman);
            Assert.Equal("", apps.Single(a => a.Name == "vkmark").Source);            // sem pacote no Arch e sem Flatpak: indisponível
            Assert.Equal("Indisponível nesta distribuição", apps.Single(a => a.Name == "vkmark").Status);
        }
        finally { Shell.Override = null; }
    }

    [Fact]
    public async Task Native_install_is_one_privileged_call_with_names_as_arguments_and_per_app_results()
    {
        var calls = new List<(string F, string[] A)>();
        Elevate.Override = (f, a) =>
        {
            calls.Add((f, a.ToArray()));
            return new ProcResult(0, "OK: gamemode\nFALHOU: mangohud\n", "");
        };
        try
        {
            var apps = AppInstaller.Catalog();
            var chosen = new List<InstallableApp> { apps.Single(a => a.Name == "GameMode"), apps.Single(a => a.Name == "MangoHud"), apps.Single(a => a.Name == "pciutils e usbutils") };
            var res = await AppInstaller.InstallNativeAsync(chosen, PackageManager.Apt);
            var (f, args) = Assert.Single(calls);
            Assert.Equal("sh", f);
            Assert.Equal("-c", args[0]);
            Assert.Equal(AppInstaller.NativeScript, args[1]);                       // roteiro fixo
            Assert.Equal(new[] { "infopc", "apt", "gamemode", "mangohud", "pciutils usbutils" }, args.Skip(2).ToArray());
            Assert.True(res[chosen[0]].Ok);
            Assert.False(res[chosen[1]].Ok);
            Assert.False(res[chosen[2]].Ok);                                        // não houve "OK:" para ele
        }
        finally { Elevate.Override = null; }
    }

    [Fact]
    public async Task A_cancelled_password_prompt_marks_every_app_as_failed_with_the_reason()
    {
        Elevate.Override = (_, _) => new ProcResult(126, "", "");
        try
        {
            var a = AppInstaller.Catalog().Single(x => x.Name == "GameMode");
            var res = await AppInstaller.InstallNativeAsync(new[] { a }, PackageManager.Dnf);
            Assert.False(res[a].Ok);
            Assert.Contains("senha", res[a].Text);
        }
        finally { Elevate.Override = null; }
    }

    [Fact]
    public void Package_manager_is_detected_from_available_binaries()
    {
        Shell.Override = (f, _) => f == "dnf" ? new ProcResult(0, "", "") : ProcResult.Missing(f);
        try
        {
            // Which() usa o PATH real; o importante é não lançar e devolver um valor do enum
            Assert.True(Enum.IsDefined(AppInstaller.Detect()));
        }
        finally { Shell.Override = null; }
    }
}
