using System.IO;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;

namespace InfoPC;

public sealed record BenchRun(DateTime Date, double? Multi, double? Single, bool Sustained, string Cpu);

/// <summary>Resultado dos benchmarks de jogo (Test = "gpu", "sim" ou "mem"). Score é usado para a barra do histórico.</summary>
public sealed record BenchRecord(DateTime Date, string Test, double Score, string Summary);

/// <summary>Programa extra que abre antes do jogo (ex.: ferramenta de comando/monitoramento) e pode fechar junto com ele.</summary>
public sealed class HelperProgram
{
    public string Path { get; set; } = "";
    public string Args { get; set; } = "";
    public bool CloseWithGame { get; set; } = true;
    public string Display => System.IO.Path.GetFileName(Path) + (string.IsNullOrWhiteSpace(Args) ? "" : " " + Args);
}

/// <summary>Perfil de um jogo na Biblioteca de jogos: desempenho, argumentos de gráficos e programas auxiliares.</summary>
public sealed class GameProfile
{
    public string Priority { get; set; } = "normal";   // normal | above | high
    public bool HighPerfPlan { get; set; }
    public bool GpuHigh { get; set; }
    public bool DisableFso { get; set; }
    public string? ExePath { get; set; }
    public string WinMode { get; set; } = "default";   // default | full | borderless | window
    public string Res { get; set; } = "default";       // default | 1280x720 | 1920x1080 | 2560x1440 | 3840x2160
    public int Hz { get; set; }                        // 0 = não definir
    public string Style { get; set; } = "source";      // source | unity | unreal
    public string Extra { get; set; } = "";
    public List<HelperProgram> Helpers { get; set; } = new();
    /// <summary>Opções que o jogo tinha na Steam antes do primeiro APLICAR, por localconfig.vdf (uma conta numa instalação): "" = nenhuma. Vazio = o InfoPC não alterou.</summary>
    public Dictionary<string, string> SteamOriginals { get; set; } = new();
}

/// <summary>Jogo adicionado à mão (qualquer .exe).</summary>
public sealed class CustomGame
{
    public string Name { get; set; } = "";
    public string Exe { get; set; } = "";
    public string Args { get; set; } = "";
}

/// <summary>
/// Configurações na pasta "dados" ao lado do .exe (requisito 3, D-028); sem poder gravar nela, em %AppData%\InfoPC, que também é de onde vêm os dados de versões anteriores.
/// A chave da API é cifrada com DPAPI (só este usuário do Windows a lê).
/// </summary>
public sealed class AppSettings
{
    public static string Folder { get; } = ResolveFolder();
    /// <summary>O que dizer ao usuário ao abrir, se a pasta de dados mudou ou nada pode ser gravado.</summary>
    public static string? FolderNotice { get; private set; }
    private static string FilePath => Path.Combine(Folder, "settings.json");

    private static string ResolveFolder()
    {
        string appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "InfoPC");
        string folder = DataFolder.Resolve(AppContext.BaseDirectory, DataFolder.Identity(MachineId(), UserId()), appData, out string? notice);
        FolderNotice = notice;
        if (!string.Equals(folder, appData, StringComparison.OrdinalIgnoreCase)) Restrict(folder);
        return folder;
    }

    private static string MachineId()
    {
        try { if (Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Cryptography", "MachineGuid", null) is string id && id.Length > 0) return id; }
        catch { }
        return Environment.MachineName;
    }

    private static string UserId()
    {
        try { if (WindowsIdentity.GetCurrent().User?.Value is { } sid) return sid; }
        catch { }
        return Environment.UserName;
    }

    /// <summary>
    /// O InfoPC abre como administrador e confia nestes dados para restaurar registro e inicialização. O %AppData% já era só do usuário; aqui, sem isto, valeria a permissão
    /// da pasta do .exe, que outro usuário pode conseguir alterar. Em pen drive FAT ou exFAT não há permissões, e a falha não impede nada.
    /// </summary>
    private static void Restrict(string folder)
    {
        try
        {
            var acl = new DirectorySecurity();
            acl.SetAccessRuleProtection(true, false);
            const InheritanceFlags all = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
            foreach (var who in new IdentityReference[] { WindowsIdentity.GetCurrent().User!, new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null) })
                acl.AddAccessRule(new FileSystemAccessRule(who, FileSystemRights.FullControl, all, PropagationFlags.None, AccessControlType.Allow));
            new DirectoryInfo(folder).SetAccessControl(acl);
        }
        catch { }
    }

    /// <summary>Marcas das últimas gravações: mostram qual cópia dos dados veio depois (<see cref="DataFolder.Advance"/>).</summary>
    public List<string> DataTrail { get; set; } = new();

    public string? HowManyFpsKeyProtected { get; set; }

    // Benchmark: últimos resultados (mais recente primeiro)
    public List<BenchRun> BenchRuns { get; set; } = new();
    public List<BenchRecord> BenchRecords { get; set; } = new();

    // Otimização: valores originais dos ajustes do Windows antes da primeira alteração ("<ausente>" = não existia)
    public Dictionary<string, string?> TweakBackup { get; set; } = new();
    /// <summary>ComDB original (reservas de números COM) antes da primeira "liberação".</summary>
    public byte[]? ComDbBackup { get; set; }
    /// <summary>Programas de inicialização removidos, para "Restaurar removidos".</summary>
    public List<StartupBackup> StartupRemoved { get; set; } = new();

    // Jogos
    public Dictionary<string, GameProfile> GameProfiles { get; set; } = new();
    /// <summary>Perfis de jogo: valor original das preferências do .exe ("gpu|exe", "fso|exe") antes de o InfoPC mudá-las ("&lt;ausente&gt;" = não existia).</summary>
    public Dictionary<string, string?> ExeBackup { get; set; } = new();
    public List<CustomGame> CustomGames { get; set; } = new();

    // Aparência
    public bool LightTheme { get; set; }

    // Overlay
    public string OverlayCorner { get; set; } = "TL";
    public double OverlayScale { get; set; } = 1.0;
    /// <summary>Itens escolhidos para o overlay (chaves de OverlayMetrics); null = padrão.</summary>
    public List<string>? OverlayItems { get; set; }

    [System.Text.Json.Serialization.JsonIgnore] // nunca gravar a chave em texto puro
    public string? HowManyFpsKey
    {
        get
        {
            if (string.IsNullOrEmpty(HowManyFpsKeyProtected)) return null;
            try
            {
                var bytes = ProtectedData.Unprotect(Convert.FromBase64String(HowManyFpsKeyProtected), null, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(bytes);
            }
            catch { return null; }
        }
        set
        {
            HowManyFpsKeyProtected = string.IsNullOrWhiteSpace(value)
                ? null
                : Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(value.Trim()), null, DataProtectionScope.CurrentUser));
        }
    }

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
        }
        catch
        {
            DataFolder.KeepUnreadable(FilePath);
            return new AppSettings { _unreadOnLoad = true };
        }
        return new AppSettings();
    }

    private bool _unreadOnLoad;

    public void Save()
    {
        // o arquivo que não deu para ler ao abrir pode só ter estado preso: a cópia dele sai antes de ser substituído
        if (_unreadOnLoad) { DataFolder.KeepUnreadable(FilePath); _unreadOnLoad = false; }
        DataTrail = DataFolder.Advance(DataTrail);
        Directory.CreateDirectory(Folder);
        // grava num temporário e troca no final: faltar luz no meio não pode levar os registros de desfazer
        string tmp = FilePath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(tmp, FilePath, true);
    }
}
