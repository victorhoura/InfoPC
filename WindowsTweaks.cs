using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace InfoPC;

/// <summary>
/// Ajuste do Windows para jogos: lê o estado atual, aplica e guarda o valor original (em AppSettings.TweakBackup)
/// na primeira vez que é alterado, para "Restaurar originais" desfazer tudo.
/// Só ajustes documentados e reversíveis; nada que reduza a segurança (Defender, VBS/HVCI, UAC, Windows Update).
/// </summary>
public sealed class Tweak : INotifyPropertyChanged
{
    public string Id { get; init; } = "";
    public string Title { get; init; } = "";
    public string Description { get; init; } = "";
    public string Effect { get; init; } = "";
    public bool NeedsRestart { get; init; }
    internal Func<bool?> Read { get; init; } = () => null;
    /// <summary>Guarda em TweakBackup o que ainda não está guardado, sem mudar nada no Windows (ver WindowsTweaks.Apply).</summary>
    internal Action<AppSettings> Remember { get; init; } = _ => { };
    internal Action<bool, AppSettings> Write { get; init; } = (_, _) => { };
    internal Action<AppSettings> Restore { get; init; } = _ => { };

    private bool _isOn;
    public bool IsOn { get => _isOn; set { _isOn = value; Changed(nameof(IsOn)); } }
    private string _status = "";
    public string Status { get => _status; set { _status = value; Changed(nameof(Status)); } }
    public string Badge => NeedsRestart ? "REQUER REINICIAR" : "";

    public void Refresh() => IsOn = Read() ?? false;

    private void Changed(string p) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));
    public event PropertyChangedEventHandler? PropertyChanged;
}

public static class WindowsTweaks
{
    private const string HighPerformance = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";

    public static List<Tweak> All() => new()
    {
        new Tweak
        {
            Id = "power", Title = "Plano de energia: Alto desempenho",
            Description = "Mantém a CPU pronta para subir o clock na hora, sem economia agressiva de energia.",
            Effect = "Menos engasgos e 1% low melhor em alguns jogos. Em notebooks gasta mais bateria.",
            Read = () => ActiveScheme()?.Equals(HighPerformance, StringComparison.OrdinalIgnoreCase),
            Remember = s =>
            {
                if (!s.TweakBackup.ContainsKey("power"))
                    s.TweakBackup["power"] = ActiveScheme() ?? throw new InvalidOperationException("não consegui ler o plano de energia atual, então nada foi alterado");
                // Alguns PCs vêm sem o plano listado e o InfoPC o cria: o RESTAURAR o apaga de novo.
                if (!s.TweakBackup.ContainsKey("power.criado") && !HasHighPerformance()) s.TweakBackup["power.criado"] = "1";
            },
            Write = (on, s) =>
            {
                if (on)
                {
                    // duplicar o modelo padrão recria o plano com o mesmo GUID
                    if (!HasHighPerformance()) PowerCfg($"-duplicatescheme {HighPerformance} {HighPerformance}");
                    PowerCfg($"/setactive {HighPerformance}");
                }
                else if (s.TweakBackup.GetValueOrDefault("power") is string prev && !prev.Equals(HighPerformance, StringComparison.OrdinalIgnoreCase))
                    PowerCfg($"/setactive {prev}");
                else PowerCfg("/setactive 381b4222-f694-41f0-9685-ff5bb260df2e"); // Equilibrado
            },
            Restore = s =>
            {
                if (s.TweakBackup.GetValueOrDefault("power") is not string prev)
                {
                    if (!s.TweakBackup.ContainsKey("power")) return;
                    // versões antigas guardavam null quando não conseguiam ler o plano: não há o que devolver
                    throw new UnknownOriginalException("o plano de energia original não foi guardado quando o ajuste foi ligado; escolha-o em Opções de energia.");
                }
                PowerCfg($"/setactive {prev}");
                if (s.TweakBackup.ContainsKey("power.criado") && !prev.Equals(HighPerformance, StringComparison.OrdinalIgnoreCase) && HasHighPerformance())
                    PowerCfg($"/delete {HighPerformance}");
            },
        },
        RegTweak("gamemode", "Modo de Jogo",
            "Dá prioridade ao jogo e evita que o Windows Update instale ou reinicie durante a partida.",
            "Recomendado ligado (é o padrão do Windows 11).",
            Registry.CurrentUser, @"Software\Microsoft\GameBar", "AutoGameModeEnabled", on: 1, off: 0, absentMeansOn: true),
        new Tweak
        {
            Id = "gamedvr", Title = "Desativar gravação em segundo plano (Game DVR)",
            Description = "Desliga a captura contínua do Xbox Game Bar, que grava os últimos minutos de jogo o tempo todo.",
            Effect = "Recupera alguns FPS e reduz uso de GPU/disco. Você perde o \"gravar o que aconteceu\" do Game Bar.",
            Read = () => (ReadDword(Registry.CurrentUser, @"System\GameConfigStore", "GameDVR_Enabled") ?? 1) == 0 &&
                         (ReadDword(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\GameDVR", "AppCaptureEnabled") ?? 1) == 0,
            Remember = s =>
            {
                RememberDword(s, "gamedvr.a", Registry.CurrentUser, @"System\GameConfigStore", "GameDVR_Enabled");
                RememberDword(s, "gamedvr.b", Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\GameDVR", "AppCaptureEnabled");
            },
            Write = (on, s) =>
            {
                SetDword(Registry.CurrentUser, @"System\GameConfigStore", "GameDVR_Enabled", on ? 0 : 1);
                SetDword(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\GameDVR", "AppCaptureEnabled", on ? 0 : 1);
            },
            Restore = s =>
            {
                RestoreDword(s, "gamedvr.a", Registry.CurrentUser, @"System\GameConfigStore", "GameDVR_Enabled");
                RestoreDword(s, "gamedvr.b", Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\GameDVR", "AppCaptureEnabled");
            },
        },
        RegTweak("hags", "Agendamento de GPU acelerado por hardware",
            "A própria placa de vídeo gerencia sua fila de trabalho, em vez da CPU.",
            "Necessário para o Frame Generation do DLSS 3/4. Pode reduzir latência. Requer GPU e driver compatíveis.",
            Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "HwSchMode", on: 2, off: 1,
            absentMeansOn: false, needsRestart: true),
        new Tweak
        {
            Id = "windowed", Title = "Otimizações para jogos em janela",
            Description = "Faz jogos DirectX 10/11 em janela ou \"sem bordas\" usarem o modelo de apresentação moderno (flip).",
            Effect = "Menor latência e suporte a VRR/G-Sync em janela. Recurso do Windows 11.",
            Read = () => DxSettings().GetValueOrDefault("SwapEffectUpgradeEnable") == "1",
            Remember = s =>
            {
                if (!s.TweakBackup.ContainsKey("windowed"))
                    s.TweakBackup["windowed"] = Registry.CurrentUser.OpenSubKey(DxPath)?.GetValue("DirectXUserGlobalSettings") as string ?? "<ausente>";
            },
            Write = (on, s) =>
            {
                var d = DxSettings();
                d["SwapEffectUpgradeEnable"] = on ? "1" : "0";
                using var k = Registry.CurrentUser.CreateSubKey(DxPath);
                k.SetValue("DirectXUserGlobalSettings", string.Concat(d.Select(kv => $"{kv.Key}={kv.Value};")));
            },
            Restore = s =>
            {
                if (s.TweakBackup.GetValueOrDefault("windowed") is not string prev) return;
                using var k = Registry.CurrentUser.CreateSubKey(DxPath);
                if (prev == "<ausente>") k.DeleteValue("DirectXUserGlobalSettings", false);
                else k.SetValue("DirectXUserGlobalSettings", prev);
            },
        },
        new Tweak
        {
            Id = "mouse", Title = "Desligar aceleração do mouse",
            Description = "Desmarca \"Aprimorar precisão do ponteiro\": o cursor anda sempre a mesma distância para o mesmo movimento.",
            Effect = "Não muda FPS, mas deixa a mira consistente em jogos de tiro.",
            Read = () => ReadString(Registry.CurrentUser, @"Control Panel\Mouse", "MouseSpeed") == "0",
            Remember = s =>
            {
                foreach (var n in new[] { "MouseSpeed", "MouseThreshold1", "MouseThreshold2" })
                    if (!s.TweakBackup.ContainsKey($"mouse.{n}")) s.TweakBackup[$"mouse.{n}"] = ReadString(Registry.CurrentUser, @"Control Panel\Mouse", n);
            },
            Write = (on, s) =>
            {
                var v = on ? new[] { "0", "0", "0" } : new[] { "1", "6", "10" }; // padrão do Windows
                ApplyMouse(v);
            },
            Restore = s =>
            {
                if (!s.TweakBackup.ContainsKey("mouse.MouseSpeed")) return;
                ApplyMouse(new[] { "MouseSpeed", "MouseThreshold1", "MouseThreshold2" }
                    .Select(n => s.TweakBackup.GetValueOrDefault($"mouse.{n}")).ToArray()); // null = não existia
            },
        },
    };

    /// <summary>
    /// Liga ou desliga um ajuste. O valor original vai para o disco (<paramref name="persist"/>) antes de mexer no Windows:
    /// se não der para ler ou guardar, nada muda e a exceção sobe. Uma leitura que falhou no meio não deixa nada na memória: o que
    /// ficasse ali pareceria já guardado no próximo clique, e o ajuste mudaria sem o original no disco.
    /// </summary>
    public static void Apply(Tweak t, bool on, AppSettings s, Action persist)
    {
        var before = s.TweakBackup.Keys.ToHashSet();
        try { t.Remember(s); }
        catch
        {
            foreach (var k in s.TweakBackup.Keys.Where(k => !before.Contains(k)).ToList()) s.TweakBackup.Remove(k);
            throw;
        }
        var added = s.TweakBackup.Keys.Where(k => !before.Contains(k)).ToList();
        if (added.Count > 0)
        {
            try { persist(); }
            catch (Exception ex)
            {
                foreach (var k in added) s.TweakBackup.Remove(k);
                throw new System.IO.IOException($"não consegui guardar o valor original, então nada foi alterado ({ex.Message})", ex);
            }
        }
        t.Write(on, s);
    }

    /// <summary>Chaves do ajuste em TweakBackup: o Id ou "Id.parte" (gamedvr.a, mouse.MouseSpeed, power.criado...).</summary>
    public static bool HasBackup(Tweak t, AppSettings s) => BackupKeys(t, s).Count > 0;

    private static List<string> BackupKeys(Tweak t, AppSettings s) =>
        s.TweakBackup.Keys.Where(k => k == t.Id || k.StartsWith(t.Id + ".", StringComparison.Ordinal)).ToList();

    /// <summary>
    /// Desfaz tudo que foi alterado pelo app (valores guardados antes da primeira alteração). Só esquece o backup dos ajustes que voltaram:
    /// o de um ajuste que falhou continua guardado para tentar de novo (exceto quando o original nunca foi guardado). Devolve as falhas;
    /// quem chama grava as configurações.
    /// </summary>
    public static List<(Tweak Tweak, string Error)> RestoreAll(IEnumerable<Tweak> tweaks, AppSettings s)
    {
        var failed = new List<(Tweak, string)>();
        foreach (var t in tweaks)
        {
            var keys = BackupKeys(t, s);
            if (keys.Count == 0) continue;
            try
            {
                t.Restore(s);
                foreach (var k in keys) s.TweakBackup.Remove(k);
            }
            catch (UnknownOriginalException ex)
            {
                failed.Add((t, ex.Message));
                foreach (var k in keys) s.TweakBackup.Remove(k); // tentar de novo não teria o que devolver
            }
            catch (Exception ex) { failed.Add((t, ex.Message)); }
        }
        return failed;
    }

    /// <summary>O backup existe, mas o valor original nunca foi lido: não há o que restaurar.</summary>
    private sealed class UnknownOriginalException(string message) : Exception(message);

    // ------------------------------------------------------------------ registro

    private static Tweak RegTweak(string id, string title, string desc, string effect, RegistryKey hive, string path, string name,
                                  int on, int off, bool absentMeansOn, bool needsRestart = false) => new()
    {
        Id = id, Title = title, Description = desc, Effect = effect, NeedsRestart = needsRestart,
        Read = () => (ReadDword(hive, path, name) is int v) ? v == on : absentMeansOn,
        Remember = s => RememberDword(s, id, hive, path, name),
        Write = (enable, s) => SetDword(hive, path, name, enable ? on : off),
        Restore = s => RestoreDword(s, id, hive, path, name),
    };

    private static int? ReadDword(RegistryKey hive, string path, string name)
    {
        using var k = hive.OpenSubKey(path);
        return k?.GetValue(name) is int v ? v : null;
    }

    private static string? ReadString(RegistryKey hive, string path, string name)
    {
        using var k = hive.OpenSubKey(path);
        return k?.GetValue(name)?.ToString();
    }

    private static void RememberDword(AppSettings s, string backupKey, RegistryKey hive, string path, string name)
    {
        if (!s.TweakBackup.ContainsKey(backupKey))
            s.TweakBackup[backupKey] = ReadDword(hive, path, name)?.ToString() ?? "<ausente>";
    }

    private static void SetDword(RegistryKey hive, string path, string name, int value)
    {
        using var k = hive.CreateSubKey(path);
        k.SetValue(name, value, RegistryValueKind.DWord);
    }

    private static void RestoreDword(AppSettings s, string backupKey, RegistryKey hive, string path, string name)
    {
        if (s.TweakBackup.GetValueOrDefault(backupKey) is not string prev) return;
        using var k = hive.CreateSubKey(path);
        if (prev == "<ausente>") k.DeleteValue(name, false);
        else k.SetValue(name, int.Parse(prev), RegistryValueKind.DWord);
    }

    private const string DxPath = @"Software\Microsoft\DirectX\UserGpuPreferences";

    /// <summary>"Chave=Valor;Chave=Valor;" → dicionário (o valor pode ter outras opções do usuário, que preservamos).</summary>
    private static Dictionary<string, string> DxSettings()
    {
        var d = new Dictionary<string, string>();
        string raw = Registry.CurrentUser.OpenSubKey(DxPath)?.GetValue("DirectXUserGlobalSettings") as string ?? "";
        foreach (var part in raw.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = part.Split('=', 2);
            if (kv.Length == 2) d[kv[0].Trim()] = kv[1].Trim();
        }
        return d;
    }

    /// <summary>
    /// Grava MouseSpeed, MouseThreshold1 e MouseThreshold2 (null apaga o valor, como era antes) e aplica na hora.
    /// O registro é gravado aqui; o SPI_SETMOUSE só aplica na sessão (sem SPIF_UPDATEINIFILE), senão o Windows recriaria os valores que não existiam.
    /// </summary>
    private static void ApplyMouse(string?[] v)
    {
        string[] names = { "MouseSpeed", "MouseThreshold1", "MouseThreshold2" };
        using (var k = Registry.CurrentUser.CreateSubKey(@"Control Panel\Mouse"))
        {
            for (int i = 0; i < names.Length; i++)
                if (v[i] is string x) k.SetValue(names[i], x);
                else k.DeleteValue(names[i], false);
        }
        // Aplica na hora, sem precisar sair da conta. Valor ausente vale o padrão do Windows (1, 6, 10).
        int[] defaults = { 1, 6, 10 };
        var n = v.Select((x, i) => int.TryParse(x, out int parsed) ? parsed : defaults[i]).ToArray();
        int[] p = { n[1], n[2], n[0] }; // ordem do SPI_SETMOUSE: limiar 1, limiar 2, aceleração
        if (!SystemParametersInfo(0x0004 /* SPI_SETMOUSE */, 0, p, 0x02 /* SPIF_SENDCHANGE */))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "o registro foi gravado, mas o Windows não aplicou na sessão atual (vale ao entrar de novo na conta)");
    }

    // ------------------------------------------------------------------ plano de energia

    private static string? ActiveScheme()
    {
        var m = System.Text.RegularExpressions.Regex.Match(PowerCfg("/getactivescheme"), @"[0-9a-fA-F\-]{36}");
        return m.Success ? m.Value.ToLowerInvariant() : null;
    }

    private static bool HasHighPerformance() => PowerCfg("/list").Contains(HighPerformance, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Roda o powercfg e devolve a saída. Código de saída diferente de zero ou demora acima de 10 s viram exceção,
    /// para quem chama não confundir falha com sucesso (um plano apagado, por exemplo, não volta).
    /// </summary>
    internal static string PowerCfg(string args)
    {
        var psi = new ProcessStartInfo("powercfg.exe", args)
        {
            RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true
        };
        using var p = Process.Start(psi) ?? throw new InvalidOperationException("não consegui executar o powercfg");
        var output = p.StandardOutput.ReadToEndAsync();
        _ = p.StandardError.ReadToEndAsync(); // só para o powercfg não travar com a saída de erro cheia
        if (!p.WaitForExit(10000))
        {
            try { p.Kill(); } catch { }
            throw new TimeoutException($"o powercfg {args} não terminou em 10 s");
        }
        p.WaitForExit(); // termina de ler a saída
        if (p.ExitCode != 0) throw new InvalidOperationException($"o powercfg {args} falhou (código {p.ExitCode})");
        return output.Result;
    }

    [DllImport("user32.dll", SetLastError = true)] private static extern bool SystemParametersInfo(uint action, uint param, int[] data, uint winIni);
}
