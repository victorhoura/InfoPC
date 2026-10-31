using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace InfoPC;

/// <summary>Programa do usuário (agrupado pelo nome do processo) que pode ser fechado para liberar recursos.</summary>
public sealed class BackgroundApp : INotifyPropertyChanged
{
    public string ProcessName { get; init; } = "";
    public string Title { get; init; } = "";
    public string Detail { get; init; } = "";
    public long MemoryBytes { get; init; }
    public int Count { get; init; }
    public bool Suggested { get; init; }
    public int[] Pids { get; init; } = Array.Empty<int>();
    public string MemoryText => $"{MemoryBytes / 1048576.0:N0} MB";

    private bool _selected;
    public bool Selected
    {
        get => _selected;
        set { _selected = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Selected))); }
    }
    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed record CloseResult(List<string> Closed, List<string> NotClosed);

/// <summary>
/// Fecha programas em segundo plano do PRÓPRIO usuário (SIGTERM, o pedido educado de "feche": o programa salva e sai) e libera o cache de arquivos.
/// Nunca toca em processos de outros usuários, do sistema, do desktop nem em programas iniciados num terminal.
/// </summary>
public static class Optimizer
{
    [DllImport("libc", EntryPoint = "getuid")] private static extern uint GetUid();
    [DllImport("libc", EntryPoint = "kill", SetLastError = true)] private static extern int Kill(int pid, int sig);

    /// <summary>Nomes (como o kernel mostra, até 15 letras) que nunca entram na lista: sessão gráfica, áudio, rede, login, lojas de jogos e o próprio programa.</summary>
    private static readonly HashSet<string> Protected = new(StringComparer.OrdinalIgnoreCase)
    {
        "systemd", "(sd-pam)", "dbus-daemon", "dbus-broker", "dbus-broker-lau", "pipewire", "pipewire-pulse", "wireplumber", "pulseaudio",
        "gnome-shell", "gnome-session-b", "gnome-session-c", "gnome-keyring-d", "gnome-settings-", "gsd-media-keys", "plasmashell", "kwin_x11", "kwin_wayland",
        "kded5", "kded6", "ksmserver", "kglobalaccel5", "kglobalaccel6", "Xorg", "Xwayland", "mutter", "sway", "Hyprland", "i3", "openbox", "xfwm4", "xfce4-session",
        "xdg-desktop-por", "xdg-document-po", "xdg-permission-", "at-spi-bus-laun", "at-spi2-registr", "gvfsd", "gvfs-udisks2-vo", "gvfsd-fuse", "ibus-daemon", "ibus-x11",
        "fcitx", "fcitx5", "polkitd", "polkit-gnome-au", "lxpolkit", "nm-applet", "NetworkManager", "bash", "zsh", "fish", "sh", "ssh", "ssh-agent", "sshd", "tmux", "screen",
        "login", "gdm", "gdm-session-wor", "gdm-wayland-ses", "gdm-x-session", "sddm", "lightdm", "ps", "top", "htop",
        "gnome-terminal-", "konsole", "xterm", "alacritty", "kitty", "wezterm-gui", "tilix", "foot", "terminator",
        "steam", "steamwebhelper", "reaper", "pressure-vessel", "wineserver", "wine", "wine64", "winedevice.exe", "gamemoded", "gamemode-simple", "lutris", "heroic",
        "code", "codium", "dotnet", "InfoPC", "infopc",
    };

    /// <summary>Programas de segundo plano que costumam ser seguros de fechar durante o jogo (vêm marcados).</summary>
    private static readonly Dictionary<string, string> SuggestedApps = new(StringComparer.OrdinalIgnoreCase)
    {
        ["dropbox"] = "Sincronização do Dropbox (usa disco e rede)", ["onedrive"] = "Sincronização do OneDrive", ["megasync"] = "Sincronização do MEGA",
        ["nextcloud"] = "Sincronização do Nextcloud", ["syncthing"] = "Sincronização do Syncthing",
        ["teams-for-linux"] = "Microsoft Teams", ["teams"] = "Microsoft Teams", ["slack"] = "Slack", ["zoom"] = "Zoom", ["skypeforlinux"] = "Skype",
        ["tracker-miner-fs"] = "Indexador de arquivos (Tracker)", ["tracker-miner-fs-3"] = "Indexador de arquivos (Tracker)", ["tracker-extract-3"] = "Extração de metadados (Tracker)",
        ["baloo_file"] = "Indexador de arquivos do KDE (Baloo)", ["baloo_file_extra"] = "Indexador de arquivos do KDE (Baloo)",
        ["gnome-software"] = "Loja de aplicativos do GNOME (atualizações em segundo plano)", ["update-notifier"] = "Aviso de atualizações", ["tumblerd"] = "Gerador de miniaturas",
        ["jetbrains-toolb"] = "JetBrains Toolbox", ["evolution-alarm-"] = "Alarmes do Evolution", ["geary"] = "Geary (e-mail)",
    };

    public static uint CurrentUid => OperatingSystem.IsLinux() ? GetUid() : 0;

    /// <summary>Lista os programas do usuário que podem ser fechados, maiores primeiro. Os "sugeridos" vêm marcados.</summary>
    public static List<BackgroundApp> ListApps(int top = 40)
    {
        uint me = CurrentUid;
        var groups = new Dictionary<string, (long Rss, List<int> Pids, string Cmd)>(StringComparer.OrdinalIgnoreCase);
        foreach (var n in Sys.List("/proc"))
        {
            if (!int.TryParse(n, out int pid) || pid == Environment.ProcessId) continue;
            var st = Sys.KeyValues($"/proc/{n}/status");
            if (!st.TryGetValue("Uid", out var uidLine) || Num.L(uidLine.Split('\t', ' ')[0]) != me) continue;
            string name = st.GetValueOrDefault("Name", "");
            if (name.Length == 0 || Protected.Contains(name) || name.StartsWith("kworker", StringComparison.Ordinal) || name.StartsWith("gvfs", StringComparison.Ordinal)
                || name.StartsWith("at-spi", StringComparison.Ordinal) || name.StartsWith("xdg-", StringComparison.Ordinal)) continue;
            if (TtyOf(n) != 0) continue;                                  // iniciado de um terminal: é do usuário, não do desktop
            if (Sys.Read($"/proc/{n}/stat") is { } stat && ParentOf(stat) == 2) continue;   // thread do kernel
            long rss = (Num.L(st.GetValueOrDefault("VmRSS", "").Replace("kB", "").Trim()) ?? 0) * 1024;
            string cmd = (Sys.Read($"/proc/{n}/cmdline") ?? "").Replace('\0', ' ').Trim();
            if (!groups.TryGetValue(name, out var g)) g = (0, new List<int>(), cmd);
            g.Rss += rss; g.Pids.Add(pid);
            groups[name] = g;
        }

        var apps = new List<BackgroundApp>();
        foreach (var (name, g) in groups)
        {
            bool suggested = SuggestedApps.ContainsKey(name);
            if (g.Rss < 20L << 20 && !suggested) continue;                // processos pequenos não valem a pena
            apps.Add(new BackgroundApp
            {
                ProcessName = name, Title = name, Detail = suggested ? SuggestedApps[name] : Shorten(g.Cmd),
                MemoryBytes = g.Rss, Count = g.Pids.Count, Suggested = suggested, Pids = g.Pids.ToArray(), Selected = suggested,
            });
        }
        return apps.OrderByDescending(a => a.Suggested).ThenByDescending(a => a.MemoryBytes).Take(top).ToList();
    }

    private static int TtyOf(string pid)
    {
        string? stat = Sys.Read($"/proc/{pid}/stat");
        if (stat == null) return 0;
        int close = stat.LastIndexOf(')');
        var f = close >= 0 ? stat[(close + 2)..].Split(' ') : Array.Empty<string>();
        return f.Length > 4 ? (int)(Num.L(f[4]) ?? 0) : 0;          // campos após ")": estado, ppid, pgrp, sessão, tty_nr
    }

    private static int ParentOf(string stat)
    {
        int close = stat.LastIndexOf(')');
        var f = close >= 0 ? stat[(close + 2)..].Split(' ') : Array.Empty<string>();
        return f.Length > 1 ? (int)(Num.L(f[1]) ?? 0) : 0;
    }

    private static string Shorten(string cmd) => cmd.Length <= 90 ? cmd : cmd[..90] + "…";

    /// <summary>Pede para fechar (SIGTERM) e espera até 3 s; o que não saiu fica na lista de "não fechou". Nunca usa SIGKILL.</summary>
    public static async Task<CloseResult> CloseAsync(IEnumerable<BackgroundApp> apps)
    {
        var closed = new List<string>(); var stuck = new List<string>();
        var list = apps.ToList();
        foreach (var a in list)
            foreach (var pid in a.Pids)
                try { Kill(pid, 15); } catch { }
        for (int i = 0; i < 6; i++)
        {
            await Task.Delay(500);
            if (list.All(a => a.Pids.All(p => !Sys.DirExists($"/proc/{p}")))) break;
        }
        foreach (var a in list) (a.Pids.All(p => !Sys.DirExists($"/proc/{p}")) ? closed : stuck).Add(a.Title);
        return new CloseResult(closed, stuck);
    }

    /// <summary>Descarrega o cache de arquivos (sync + drop_caches=1, só páginas de arquivo). Pede a senha. O Linux já devolve esse cache sozinho quando falta RAM.</summary>
    public static (RootResult Result, long FreedBytes) DropCaches()
    {
        long before = (MemInfo.Read()?.AvailableKb ?? 0) * 1024;
        var r = Elevate.RunScript("sync; echo 1 > /proc/sys/vm/drop_caches");
        long after = (MemInfo.Read()?.AvailableKb ?? 0) * 1024;
        return (r, Math.Max(0, after - before));
    }

    /// <summary>TRIM dos SSDs montados (fstrim -av): devolve ao SSD os blocos livres. Pede a senha.</summary>
    public static RootResult Trim() => Elevate.Run("fstrim", new[] { "-av" }, 300000);
}
