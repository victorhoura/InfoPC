using System.ComponentModel;
using System.Text.RegularExpressions;

namespace InfoPC;

/// <summary>
/// Ajuste do Linux para jogos: lê o estado atual, aplica e guarda o valor original (em AppSettings.TweakBackup) na primeira alteração,
/// para "Restaurar originais" desfazer tudo. Só ajustes documentados e reversíveis, que valem até reiniciar o PC (nada vai para /etc).
/// Nada que reduza a segurança. Cada gravação pede a senha de administrador (pkexec) e usa comandos fixos, sem texto livre.
/// </summary>
public sealed class Tweak : INotifyPropertyChanged
{
    public string Id { get; init; } = "";
    public string Title { get; init; } = "";
    public string Description { get; init; } = "";
    public string Effect { get; init; } = "";
    public bool NeedsRoot { get; init; } = true;
    public Func<bool> Available { get; init; } = () => true;
    internal Func<bool?> Read { get; init; } = () => null;
    /// <summary>Guarda o valor atual em TweakBackup, se ainda não estiver guardado, sem mudar nada (ver LinuxTweaks.Apply).</summary>
    internal Action<AppSettings> Remember { get; init; } = _ => { };
    internal Func<bool, AppSettings, RootResult> Write { get; init; } = (_, _) => new RootResult(true, "", "");
    internal Func<AppSettings, RootResult> Restore { get; init; } = _ => new RootResult(true, "", "");

    private bool _isOn;
    public bool IsOn { get => _isOn; set { _isOn = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsOn))); } }
    private string _status = "";
    public string Status { get => _status; set { _status = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Status))); } }
    public string Badge => NeedsRoot ? "PEDE SENHA" : "";

    public void Refresh() => IsOn = Read() ?? false;
    public event PropertyChangedEventHandler? PropertyChanged;
}

public static class LinuxTweaks
{
    public const string MaxMapCountValue = "2147483642";

    private static readonly Regex Safe = new(@"^[A-Za-z0-9_.\-]{1,40}$", RegexOptions.Compiled);

    public static List<Tweak> All() => new()
    {
        new Tweak
        {
            Id = "governor", Title = "Perfil de CPU: desempenho",
            Description = "Mantém a CPU pronta para subir o clock na hora, sem a economia agressiva do perfil equilibrado.",
            Effect = "Menos engasgos e 1% low melhor em alguns jogos. Em notebooks gasta mais bateria e esquenta mais. O GameMode faz isso só durante o jogo.",
            NeedsRoot = false, // com o power-profiles-daemon não precisa de senha; sem ele, pede
            Available = () => Shell.Has("powerprofilesctl") || Sys.FileExists("/sys/devices/system/cpu/cpu0/cpufreq/scaling_governor"),
            Read = ReadGovernor, Remember = RememberGovernor, Write = WriteGovernor, Restore = s => Restore(s, "governor"),
        },
        new Tweak
        {
            Id = "swappiness", Title = "Menos uso de swap (vm.swappiness = 10)",
            Description = "Diz ao kernel para preferir tirar o cache de arquivos da RAM antes de mandar programas para o disco.",
            Effect = "Reduz travadas quando a RAM está quase cheia, em PCs com SSD. Sem efeito se há RAM de sobra.",
            Available = () => Sys.FileExists("/proc/sys/vm/swappiness"),
            Read = () => Sys.Long("/proc/sys/vm/swappiness") is { } v ? v <= 10 : null,
            Remember = s => RememberSysctl("vm.swappiness", "/proc/sys/vm/swappiness", "swappiness", s),
            Write = (on, s) => WriteSysctl("vm.swappiness", "swappiness", on ? "10" : null, s), Restore = s => Restore(s, "swappiness"),
        },
        new Tweak
        {
            Id = "max_map_count", Title = $"Mais mapas de memória (vm.max_map_count = {MaxMapCountValue})",
            Description = "Alguns jogos (muitos do Proton) abrem mais de 65.530 regiões de memória e fecham sem aviso quando o limite é baixo.",
            Effect = "Evita travamentos de jogos como Hogwarts Legacy, DayZ e outros. É o valor que o SteamOS usa; sem custo mensurável.",
            Available = () => Sys.FileExists("/proc/sys/vm/max_map_count"),
            Read = () => Sys.Long("/proc/sys/vm/max_map_count") is { } v ? v >= 2147483642 : null,
            Remember = s => RememberSysctl("vm.max_map_count", "/proc/sys/vm/max_map_count", "max_map_count", s),
            Write = (on, s) => WriteSysctl("vm.max_map_count", "max_map_count", on ? MaxMapCountValue : null, s), Restore = s => Restore(s, "max_map_count"),
        },
        new Tweak
        {
            Id = "split_lock", Title = "Sem penalidade por split lock (kernel.split_lock_mitigate = 0)",
            Description = "Desliga o atraso que o kernel 6.2+ aplica a programas que fazem acessos atômicos desalinhados (split lock).",
            Effect = "Corrige quedas fortes de FPS em alguns jogos do Windows (via Proton). Só existe em kernels recentes.",
            Available = () => Sys.FileExists("/proc/sys/kernel/split_lock_mitigate"),
            Read = () => Sys.Long("/proc/sys/kernel/split_lock_mitigate") is { } v ? v == 0 : null,
            Remember = s => RememberSysctl("kernel.split_lock_mitigate", "/proc/sys/kernel/split_lock_mitigate", "split_lock", s),
            Write = (on, s) => WriteSysctl("kernel.split_lock_mitigate", "split_lock", on ? "0" : null, s), Restore = s => Restore(s, "split_lock"),
        },
    };

    // ---------------- sysctl ----------------

    private static void RememberSysctl(string key, string procPath, string backupKey, AppSettings s)
    {
        if (s.TweakBackup.ContainsKey(backupKey)) return;
        s.TweakBackup[backupKey] = Sys.Read(procPath) is { Length: > 0 } v ? v : throw new InvalidOperationException($"não consegui ler o valor atual de {key}");
    }

    private static RootResult WriteSysctl(string key, string backupKey, string? value, AppSettings s)
    {
        string? target = value ?? s.TweakBackup.GetValueOrDefault(backupKey);
        if (target == null || !Safe.IsMatch(target)) return new RootResult(false, "", "Valor original desconhecido: não há o que restaurar.");
        return Elevate.Run("sysctl", new[] { "-w", $"{key}={target}" });
    }

    // ---------------- perfil / governador de CPU ----------------

    private static bool UsesPpd => Shell.Has("powerprofilesctl") && Shell.Run("powerprofilesctl", new[] { "list" }, 3000) is { Ok: true } r && r.Out.Contains("performance");

    private static bool? ReadGovernor()
    {
        if (UsesPpd) return Shell.Run("powerprofilesctl", new[] { "get" }, 3000).Out.Trim() == "performance";
        var govs = Sys.ListNatural("/sys/devices/system/cpu", "cpu").Select(c => Sys.Read($"/sys/devices/system/cpu/{c}/cpufreq/scaling_governor")).Where(g => g != null).ToList();
        return govs.Count == 0 ? null : govs.All(g => g == "performance");
    }

    private static void RememberGovernor(AppSettings s)
    {
        if (s.TweakBackup.ContainsKey("governor")) return;
        bool ppd = UsesPpd;
        string current = ppd ? Shell.Run("powerprofilesctl", new[] { "get" }, 3000).Out.Trim() : Sys.Read("/sys/devices/system/cpu/cpu0/cpufreq/scaling_governor") ?? "";
        if (current.Length == 0) throw new InvalidOperationException("não consegui ler o perfil de CPU atual");
        s.TweakBackup["governor"] = (ppd ? "ppd:" : "gov:") + current;
    }

    private static RootResult WriteGovernor(bool on, AppSettings s) =>
        SetGovernor(on ? (UsesPpd ? "ppd:performance" : "gov:performance") : s.TweakBackup.GetValueOrDefault("governor"));

    private static RootResult SetGovernor(string? spec)
    {
        if (spec == null) return new RootResult(false, "", "Valor original desconhecido.");
        string value = spec[(spec.IndexOf(':') + 1)..];
        if (!Safe.IsMatch(value)) return new RootResult(false, "", "Valor inválido.");
        if (spec.StartsWith("ppd:", StringComparison.Ordinal))
        {
            var r = Shell.Run("powerprofilesctl", new[] { "set", value }, 5000);   // sem senha
            return new RootResult(r.Ok, r.Out, r.Err);
        }
        return Elevate.RunScript($"for f in /sys/devices/system/cpu/cpu[0-9]*/cpufreq/scaling_governor; do echo {value} > \"$f\"; done");
    }

    private static RootResult Restore(AppSettings s, string key)
    {
        if (!s.TweakBackup.TryGetValue(key, out var original) || original == null) return new RootResult(true, "", "");
        RootResult r = key switch
        {
            "governor" => SetGovernor(original),
            "swappiness" => Elevate.Run("sysctl", new[] { "-w", $"vm.swappiness={original}" }),
            "max_map_count" => Elevate.Run("sysctl", new[] { "-w", $"vm.max_map_count={original}" }),
            "split_lock" => Elevate.Run("sysctl", new[] { "-w", $"kernel.split_lock_mitigate={original}" }),
            _ => new RootResult(true, "", ""),
        };
        if (r.Ok) s.TweakBackup.Remove(key);
        return r;
    }

    /// <summary>
    /// Liga ou desliga um ajuste. O valor original vai para o disco (<paramref name="persist"/>) antes de mexer no sistema: se não der
    /// para ler ou guardar, nada muda. O original só fica guardado enquanto o ajuste está ligado.
    /// </summary>
    public static RootResult Apply(Tweak t, bool on, AppSettings s, Action persist)
    {
        bool hadBackup = s.TweakBackup.ContainsKey(t.Id);
        try { t.Remember(s); }
        catch (Exception ex) { return new RootResult(false, "", $"Nada foi alterado: {ex.Message}."); }
        if (!hadBackup && s.TweakBackup.ContainsKey(t.Id))
        {
            try { persist(); }
            catch (Exception ex)
            {
                s.TweakBackup.Remove(t.Id);
                return new RootResult(false, "", $"Nada foi alterado: não consegui guardar o valor original ({ex.Message}).");
            }
        }
        var r = t.Write(on, s);
        if (r.Ok && !on) s.TweakBackup.Remove(t.Id);
        if (!r.Ok && !hadBackup) s.TweakBackup.Remove(t.Id);   // não deixa um "original" que nunca chegou a ser trocado
        return r;
    }

    public static RootResult RestoreAll(List<Tweak> tweaks, AppSettings s)
    {
        RootResult last = new(true, "Nada para restaurar.", "");
        foreach (var t in tweaks.Where(t => s.TweakBackup.ContainsKey(t.Id)).ToList())
        {
            last = t.Restore(s);
            if (!last.Ok) return last;
        }
        return last;
    }
}
