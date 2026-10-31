using System.Text.Json;
using System.Text.Json.Serialization;

namespace InfoPC;

public sealed record BenchRun(DateTime Date, double? Multi, double? Single, bool Sustained, string Cpu);

/// <summary>Resultado dos benchmarks (Test = "cpu", "sim", "mem", "disk" ou "gpu"). Score alimenta a barra do histórico.</summary>
public sealed record BenchRecord(DateTime Date, string Test, double Score, string Summary);

/// <summary>Programa extra que abre antes do jogo e pode fechar junto com ele.</summary>
public sealed class HelperProgram
{
    public string Path { get; set; } = "";
    public string Args { get; set; } = "";
    public bool CloseWithGame { get; set; } = true;
    public string Display => System.IO.Path.GetFileName(Path) + (string.IsNullOrWhiteSpace(Args) ? "" : " " + Args);
}

/// <summary>Perfil de um jogo: ferramentas do Linux (GameMode, MangoHud), GPU dedicada, janela e argumentos.</summary>
public sealed class GameProfile
{
    public bool GameMode { get; set; }
    public bool MangoHud { get; set; }
    public bool DedicatedGpu { get; set; }
    public bool ProtonWayland { get; set; }
    public string WinMode { get; set; } = "default";   // default | full | borderless | window
    public string Res { get; set; } = "default";       // default | 1280x720 | 1920x1080 | 2560x1440 | 3840x2160
    public int Hz { get; set; }
    public string Style { get; set; } = "source";      // source | unity | unreal
    public string Extra { get; set; } = "";
    public string? ExePath { get; set; }
    public List<HelperProgram> Helpers { get; set; } = new();
    /// <summary>Opções que o jogo tinha na Steam antes do primeiro APLICAR, por localconfig.vdf (uma conta numa instalação): "" = nenhuma. Vazio = o InfoPC não alterou.</summary>
    public Dictionary<string, string> SteamOriginals { get; set; } = new();
}

/// <summary>Jogo ou programa adicionado à mão (executável ou comando).</summary>
public sealed class CustomGame
{
    public string Name { get; set; } = "";
    public string Exe { get; set; } = "";
    public string Args { get; set; } = "";
}

/// <summary>Programa de inicialização removido, guardado para restaurar (cópia do .desktop original).</summary>
public sealed class StartupBackup
{
    public string FileName { get; set; } = "";
    public string Content { get; set; } = "";
    public string Source { get; set; } = "";
}

/// <summary>
/// Configurações e dados do programa (requisito 3, D-020, D-028): pasta "dados/&lt;identidade&gt;" ao lado do executável; se não for gravável,
/// ~/.local/share/InfoPC. A chave da API de FPS fica no arquivo com permissão 0600 (o Linux não tem DPAPI).
/// </summary>
public sealed class AppSettings
{
    public static string Folder { get; set; } = ResolveFolder();
    /// <summary>O que dizer ao usuário ao abrir, se a pasta de dados mudou ou nada pode ser gravado.</summary>
    public static string? FolderNotice { get; private set; }
    private static string FilePath => Path.Combine(Folder, "settings.json");

    public static string ResolveFolder()
    {
        string? env = Environment.GetEnvironmentVariable("INFOPC_DADOS");
        if (!string.IsNullOrWhiteSpace(env) && DataFolder.Writable(env)) return env;
        string exeDir = Path.GetDirectoryName(Environment.ProcessPath ?? "") ?? AppContext.BaseDirectory;
        string xdg = Environment.GetEnvironmentVariable("XDG_DATA_HOME") is { Length: > 0 } x
            ? x : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
        // antes do D-028 os dados ficavam direto em "dados", e vêm para a pasta deste PC
        string folder = DataFolder.Resolve(exeDir, DataFolder.Identity(MachineId(), Environment.UserName), Path.Combine(xdg, "InfoPC"), out string? notice, Path.Combine(exeDir, "dados"), moveLegacy: true);
        FolderNotice = notice;
        return folder;
    }

    private static string MachineId() =>
        new[] { "/etc/machine-id", "/var/lib/dbus/machine-id" }.Select(Sys.Read).FirstOrDefault(id => !string.IsNullOrEmpty(id)) ?? Environment.MachineName;

    /// <summary>Marcas das últimas gravações: mostram qual cópia dos dados veio depois (<see cref="DataFolder.Advance"/>).</summary>
    public List<string> DataTrail { get; set; } = new();

    public string? HowManyFpsKey { get; set; }

    public List<BenchRun> BenchRuns { get; set; } = new();
    public List<BenchRecord> BenchRecords { get; set; } = new();

    /// <summary>Valores originais dos ajustes do Otimizador antes da primeira alteração ("&lt;ausente&gt;" = não existia).</summary>
    public Dictionary<string, string?> TweakBackup { get; set; } = new();
    public List<StartupBackup> StartupRemoved { get; set; } = new();
    /// <summary>Serviços systemd da conta desativados pelo InfoPC (unidade → descrição): o disable apaga o link que os listava, e assim eles continuam na lista para religar.</summary>
    public Dictionary<string, string> SystemdDisabled { get; set; } = new();
    /// <summary>Grupos em que o Maker pôs o usuário (ADICIONAR AO GRUPO), para o desfazer tirar só desses.</summary>
    public List<string> GroupsAdded { get; set; } = new();
    /// <summary>Pacotes que o InfoPC instalou ou removeu (driver da GPU, brltty), para o desfazer.</summary>
    public List<PackageChange> PackageChanges { get; set; } = new();

    public Dictionary<string, GameProfile> GameProfiles { get; set; } = new();
    public List<CustomGame> CustomGames { get; set; } = new();

    public bool LightTheme { get; set; }

    public string OverlayCorner { get; set; } = "TL";
    public double OverlayScale { get; set; } = 1.0;
    public List<string>? OverlayItems { get; set; }
    public bool DeviceToasts { get; set; } = true;

    [JsonIgnore] public string? LoadError { get; private set; }

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
        }
        catch (Exception ex)
        {
            DataFolder.KeepUnreadable(FilePath);
            return new AppSettings { LoadError = ex.Message };
        }
        return new AppSettings();
    }

    public void Save()
    {
        DataTrail = DataFolder.Advance(DataTrail);
        Directory.CreateDirectory(Folder);
        if (!OperatingSystem.IsWindows())
            try { File.SetUnixFileMode(Folder, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute); } catch { }
        // grava num arquivo temporário já com 0600 e troca no final: a chave nunca fica legível por outros usuários
        string tmp = FilePath + ".tmp";
        var opt = new FileStreamOptions { Mode = FileMode.Create, Access = FileAccess.Write, Share = FileShare.None };
        if (!OperatingSystem.IsWindows()) opt.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite; // 0600 desde a criação
        using (var fs = new FileStream(tmp, opt))
        using (var w = new StreamWriter(fs))
            w.Write(JsonSerializer.Serialize(this, Json));
        File.Move(tmp, FilePath, true);
    }
}
