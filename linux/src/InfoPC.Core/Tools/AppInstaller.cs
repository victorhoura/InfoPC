using System.ComponentModel;
using System.Text.RegularExpressions;

namespace InfoPC;

public enum PackageManager { None, Apt, Dnf, Pacman, Zypper }

/// <summary>App do catálogo "Instalar apps": um pacote por gerenciador da distribuição e, quando existe, o Flatpak.</summary>
public sealed class InstallableApp : INotifyPropertyChanged
{
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public string? Apt { get; init; }
    public string? Dnf { get; init; }
    public string? Pacman { get; init; }
    public string? Zypper { get; init; }
    public string? Flatpak { get; init; }
    /// <summary>Comando que prova a instalação (qualquer origem), ex.: "steam".</summary>
    public string? Binary { get; init; }
    public bool PreSelect { get; init; }

    private bool _selected, _installed;
    private string _status = "", _source = "";
    public bool Selected { get => _selected; set { _selected = value; Raise(nameof(Selected)); } }
    public bool Installed { get => _installed; set { _installed = value; Raise(nameof(Installed)); Raise(nameof(CanSelect)); } }
    public bool CanSelect => !Installed && Source.Length > 0;
    public string Status { get => _status; set { _status = value; Raise(nameof(Status)); } }
    /// <summary>De onde vai instalar: o nome do pacote nativo, "Flatpak" ou vazio (indisponível nesta distribuição).</summary>
    public string Source { get => _source; set { _source = value; Raise(nameof(Source)); Raise(nameof(CanSelect)); } }

    public string? NativePackage(PackageManager m) => m switch { PackageManager.Apt => Apt, PackageManager.Dnf => Dnf, PackageManager.Pacman => Pacman, PackageManager.Zypper => Zypper, _ => null };

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Raise(string p) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));
}

/// <summary>
/// Catálogo de apps e instalação pelo gerenciador da distribuição (apt, dnf, pacman, zypper) ou pelo Flatpak. Os pacotes nativos entram numa única
/// chamada com senha (pkexec); o Flatpak instala na conta do usuário, sem senha. Nomes de pacote vêm só deste catálogo.
/// </summary>
public static class AppInstaller
{
    private static readonly Regex SafeName = new(@"^[A-Za-z0-9][A-Za-z0-9+._-]*$", RegexOptions.Compiled);

    public static List<InstallableApp> Catalog() => new()
    {
        new() { Name = "Steam", Description = "Loja e biblioteca de jogos da Valve (com Proton para jogos do Windows).", Apt = "steam-installer", Dnf = "steam", Pacman = "steam", Zypper = "steam", Flatpak = "com.valvesoftware.Steam", Binary = "steam", PreSelect = true },
        new() { Name = "GameMode", Description = "Pede prioridade e desempenho ao sistema enquanto o jogo roda (usado nos perfis da Biblioteca).", Apt = "gamemode", Dnf = "gamemode", Pacman = "gamemode", Zypper = "gamemode", Binary = "gamemoderun", PreSelect = true },
        new() { Name = "MangoHud", Description = "Overlay de FPS, frametime e temperaturas dentro do jogo.", Apt = "mangohud", Dnf = "mangohud", Pacman = "mangohud", Zypper = "mangohud", Binary = "mangohud", PreSelect = true },
        new() { Name = "Discord", Description = "Chat de voz e texto para jogar com os amigos.", Flatpak = "com.discordapp.Discord", Binary = "discord", PreSelect = true },
        new() { Name = "Lutris", Description = "Biblioteca para jogos de várias lojas, emuladores e Wine.", Apt = "lutris", Dnf = "lutris", Pacman = "lutris", Zypper = "lutris", Flatpak = "net.lutris.Lutris", Binary = "lutris" },
        new() { Name = "Heroic Games Launcher", Description = "Cliente das lojas Epic, GOG e Amazon.", Flatpak = "com.heroicgameslauncher.hgl", Binary = "heroic" },
        new() { Name = "ProtonUp-Qt", Description = "Instala versões do Proton-GE e do Wine-GE para a Steam e o Lutris.", Flatpak = "net.davidotek.pupgui2", Binary = "protonup-qt" },
        new() { Name = "Wine", Description = "Executa programas e jogos do Windows.", Apt = "wine", Dnf = "wine", Pacman = "wine", Zypper = "wine", Binary = "wine" },
        new() { Name = "OBS Studio", Description = "Gravação e transmissão de tela.", Apt = "obs-studio", Dnf = "obs-studio", Pacman = "obs-studio", Zypper = "obs-studio", Flatpak = "com.obsproject.Studio", Binary = "obs" },
        new() { Name = "VLC", Description = "Player de vídeo e música que abre qualquer formato.", Apt = "vlc", Dnf = "vlc", Pacman = "vlc", Zypper = "vlc", Flatpak = "org.videolan.VLC", Binary = "vlc", PreSelect = true },
        new() { Name = "Google Chrome", Description = "Navegador do Google.", Flatpak = "com.google.Chrome", Binary = "google-chrome-stable" },
        new() { Name = "7-Zip (p7zip)", Description = "Compactador (zip, rar, 7z).", Apt = "p7zip-full", Dnf = "p7zip", Pacman = "p7zip", Zypper = "p7zip", Binary = "7z" },

        new() { Name = "vkmark", Description = "Benchmark de GPU em Vulkan (usado na página Benchmark).", Apt = "vkmark", Dnf = "vkmark", Binary = "vkmark" },
        new() { Name = "glmark2", Description = "Benchmark de GPU em OpenGL (usado nas páginas Benchmark e Stress).", Apt = "glmark2", Dnf = "glmark2", Pacman = "glmark2", Zypper = "glmark2", Binary = "glmark2" },
        new() { Name = "smartmontools", Description = "Lê o S.M.A.R.T. dos discos (página Discos).", Apt = "smartmontools", Dnf = "smartmontools", Pacman = "smartmontools", Zypper = "smartmontools", Binary = "smartctl", PreSelect = true },
        new() { Name = "lm-sensors", Description = "Detecta sensores de temperatura e ventoinhas da placa-mãe.", Apt = "lm-sensors", Dnf = "lm_sensors", Pacman = "lm_sensors", Zypper = "sensors", Binary = "sensors", PreSelect = true },
        new() { Name = "pciutils e usbutils", Description = "Nomes dos dispositivos PCI e USB (lspci, lsusb).", Apt = "pciutils usbutils", Dnf = "pciutils usbutils", Pacman = "pciutils usbutils", Zypper = "pciutils usbutils", Binary = "lspci" },
        new() { Name = "dmidecode", Description = "Módulos de RAM e dados da placa-mãe (botão LER MÓDULOS DE RAM).", Apt = "dmidecode", Dnf = "dmidecode", Pacman = "dmidecode", Zypper = "dmidecode", Binary = "dmidecode" },
        new() { Name = "nvtop", Description = "Monitor de GPU no terminal (NVIDIA, AMD, Intel).", Apt = "nvtop", Dnf = "nvtop", Pacman = "nvtop", Zypper = "nvtop", Binary = "nvtop" },
        new() { Name = "btop", Description = "Monitor de CPU, memória, disco e rede no terminal.", Apt = "btop", Dnf = "btop", Pacman = "btop", Zypper = "btop", Binary = "btop" },
        new() { Name = "fwupd", Description = "Atualizações de firmware (BIOS, SSD, periféricos) pelo LVFS (página Drivers).", Apt = "fwupd", Dnf = "fwupd", Pacman = "fwupd", Zypper = "fwupd", Binary = "fwupdmgr" },
        new() { Name = "OpenRGB", Description = "Controle de iluminação RGB de placas-mãe, RAM e periféricos (página Integração).", Apt = "openrgb", Dnf = "openrgb", Pacman = "openrgb", Zypper = "openrgb", Flatpak = "org.openrgb.OpenRGB", Binary = "openrgb" },
        new() { Name = "LACT", Description = "Ajuste de clock, tensão e ventoinha de placas AMD.", Flatpak = "io.github.ilya_zlobintsev.LACT", Binary = "lact" },
        new() { Name = "Arduino IDE 2", Description = "Editor e carregador para Arduino, ESP32 e placas compatíveis (Maker).", Flatpak = "cc.arduino.IDE2", Binary = "arduino-ide" },
    };

    public static PackageManager Detect() => OsInfo.PackageManager() switch
    {
        "apt" => PackageManager.Apt, "dnf" => PackageManager.Dnf, "pacman" => PackageManager.Pacman, "zypper" => PackageManager.Zypper, _ => PackageManager.None
    };

    public static bool FlatpakAvailable => Shell.Has("flatpak");

    /// <summary>Define de onde cada app instalaria (nativo, depois Flatpak) e marca os que já estão instalados.</summary>
    public static void Resolve(IEnumerable<InstallableApp> apps, PackageManager pm)
    {
        foreach (var a in apps)
        {
            string? native = a.NativePackage(pm);
            a.Source = native != null ? native : (a.Flatpak != null && FlatpakAvailable ? "Flatpak" : "");
            a.Installed = IsInstalled(a, pm);
            if (a.Installed) { a.Selected = false; a.Status = "Instalado"; }
            else if (a.Source.Length == 0) a.Status = a.Flatpak != null ? "Precisa do Flatpak" : "Indisponível nesta distribuição";
            else if (a.Status is "Instalado" or "Precisa do Flatpak" or "Indisponível nesta distribuição") a.Status = "";
        }
    }

    public static bool IsInstalled(InstallableApp a, PackageManager pm)
    {
        if (a.Binary != null && Shell.Has(a.Binary)) return true;
        string? pkg = a.NativePackage(pm)?.Split(' ')[0];
        if (pkg != null && SafeName.IsMatch(pkg))
        {
            var r = pm switch
            {
                PackageManager.Apt => Shell.Run("dpkg", new[] { "-s", pkg }, 4000),
                PackageManager.Dnf or PackageManager.Zypper => Shell.Run("rpm", new[] { "-q", pkg }, 4000),
                PackageManager.Pacman => Shell.Run("pacman", new[] { "-Qi", pkg }, 4000),
                _ => ProcResult.Missing("pm"),
            };
            if (r.Ok) return true;
        }
        return a.Flatpak != null && FlatpakAvailable && Shell.Run("flatpak", new[] { "info", a.Flatpak }, 4000).Ok;
    }

    /// <summary>Roteiro fixo que instala cada pacote separadamente (um que falha não derruba os outros); os nomes entram como argumentos.</summary>
    public const string NativeScript =
        "mgr=\"$1\"; shift\n" +
        "[ \"$mgr\" = apt ] && apt-get update -qq\n" +
        "for p in \"$@\"; do\n" +
        "  case \"$mgr\" in\n" +
        "    apt) DEBIAN_FRONTEND=noninteractive apt-get install -y $p ;;\n" +
        "    dnf) dnf install -y $p ;;\n" +
        "    pacman) pacman -S --noconfirm --needed $p ;;\n" +
        "    zypper) zypper --non-interactive install $p ;;\n" +
        "  esac && echo \"OK: $p\" || echo \"FALHOU: $p\"\n" +
        "done\nexit 0\n";

    /// <summary>Instala os pacotes nativos de uma vez (uma senha). Devolve o resultado por app.</summary>
    public static async Task<Dictionary<InstallableApp, (bool Ok, string Text)>> InstallNativeAsync(IReadOnlyList<InstallableApp> apps, PackageManager pm, Action<string>? onLine = null)
    {
        var result = new Dictionary<InstallableApp, (bool, string)>();
        var args = new List<string> { "-c", NativeScript, "infopc", pm.ToString().ToLowerInvariant() };
        foreach (var a in apps)
        {
            string pkgs = a.NativePackage(pm) ?? "";
            if (!pkgs.Split(' ').All(p => SafeName.IsMatch(p))) { result[a] = (false, "nome de pacote inválido"); continue; }
            args.Add(pkgs);
        }
        if (args.Count == 4) return result;
        var res = await Task.Run(() => Elevate.Run("sh", args, 1800000));
        onLine?.Invoke(res.Output);
        if (!res.Ok && res.Output.Length == 0)
        {
            foreach (var a in apps) result.TryAdd(a, (false, res.Message));
            return result;
        }
        foreach (var a in apps.Where(a => !result.ContainsKey(a)))
        {
            string pkgs = a.NativePackage(pm)!;
            bool ok = res.Output.Split('\n').Any(l => l.Trim() == "OK: " + pkgs);
            result[a] = (ok, ok ? "Instalado" : "Falhou (veja o registro)");
        }
        return result;
    }

    /// <summary>Instala um Flatpak na conta do usuário (sem senha), criando o repositório Flathub se faltar.</summary>
    public static async Task<(bool Ok, string Text)> InstallFlatpakAsync(InstallableApp a, CancellationToken ct, Action<string>? onLine = null)
    {
        if (a.Flatpak == null || !SafeName.IsMatch(a.Flatpak)) return (false, "sem Flatpak para este app");
        await ProcessRunner.RunAsync("flatpak", new[] { "remote-add", "--user", "--if-not-exists", "flathub", "https://dl.flathub.org/repo/flathub.flatpakrepo" }, ct, 120000, onLine);
        var r = await ProcessRunner.RunAsync("flatpak", new[] { "install", "-y", "--noninteractive", "--user", "flathub", a.Flatpak }, ct, 1800000, onLine);
        return r.Ok ? (true, "Instalado") : (false, LastLine(r.Err.Length > 0 ? r.Err : r.Out));
    }

    private static string LastLine(string s) => s.Split('\n', StringSplitOptions.RemoveEmptyEntries).LastOrDefault()?.Trim() ?? "falhou";
}
