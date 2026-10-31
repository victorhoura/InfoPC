using System.Text.RegularExpressions;

namespace InfoPC;

/// <summary>
/// Um pacote instalado. Só o nome não basta: as variantes amd64 e i386 do mesmo pacote, ou duas versões dele, seriam a mesma coisa na comparação
/// e o desfazer não saberia o que devolver. No pacman, <see cref="Arch"/> fica vazio (o sistema tem uma arquitetura só).
/// </summary>
public sealed record PackageId(string Name, string Arch, string Version)
{
    public override string ToString() => (Arch.Length > 0 ? $"{Name}:{Arch}" : Name) + (Version.Length > 0 ? $" {Version}" : "");
}

/// <summary>O que dá para saber sobre os instaladores de pacotes: nenhum rodando, algum rodando, ou o sistema não deixa ver (hidepid).</summary>
public enum InstallerState { Idle, Active, Unknown }

/// <summary>Pacotes que uma ação do InfoPC instalou e removeu, para o desfazer (requisito 4). Fica em <see cref="AppSettings.PackageChanges"/>.</summary>
public sealed class PackageChange
{
    /// <summary>De onde veio: "gpu-driver" (ubuntu-drivers) ou "brltty" (Maker).</summary>
    public string Kind { get; set; } = "";
    public string What { get; set; } = "";
    public string Manager { get; set; } = "";
    public DateTime When { get; set; }
    public List<PackageId> Added { get; set; } = new();
    public List<PackageId> Removed { get; set; } = new();
    /// <summary>A ação começou e a lista de depois ainda não foi comparada: enquanto isso, <see cref="Before"/> guarda a lista de antes e não há o que desfazer.</summary>
    public bool Pending { get; set; }
    public List<PackageId>? Before { get; set; }
    /// <summary>A lista de depois só foi tirada mais tarde, então pode incluir o que o usuário mudou por fora nesse meio-tempo.</summary>
    public bool Recovered { get; set; }
}

/// <summary>
/// Desfazer de instalação e remoção de pacotes: guarda a lista de pacotes de antes no disco, roda a ação, compara com a de depois e devolve o contrário
/// (remove o que entrou, reinstala o que saiu, na versão de antes). Reinstalar baixa os pacotes de novo, então precisa de internet, sempre por clique.
/// </summary>
public static class Packages
{
    /// <summary>Nomes de pacote do Debian, Fedora, Arch e openSUSE. Um nome que comece com "-" seria lido como opção.</summary>
    private static readonly Regex SafeName = new(@"^[A-Za-z0-9][A-Za-z0-9+._-]{0,127}$", RegexOptions.Compiled);
    private static readonly Regex SafeArch = new(@"^([A-Za-z0-9][A-Za-z0-9_-]{0,31})?$", RegexOptions.Compiled);
    private static readonly Regex SafeVersion = new(@"^[A-Za-z0-9][A-Za-z0-9.+~:_^-]{0,127}$", RegexOptions.Compiled);

    /// <summary>Como eles aparecem no /proc (em comm, no máximo 15 letras). O pkexec fica nessa lista enquanto espera a senha: depois dela, vira o próprio instalador.</summary>
    private static readonly HashSet<string> InstallerNames = new(StringComparer.Ordinal)
    {
        "pkexec", "ubuntu-drivers", "apt", "apt-get", "aptitude", "dpkg", "unattended-upgr", "dnf", "dnf5", "yum", "rpm", "zypper", "pacman",
    };

    /// <summary>1 enquanto uma ação ou um desfazer roda: a comparação de antes e depois não pode se misturar com outra mudança do próprio InfoPC.</summary>
    private static int _busy;

    public static bool IsSafe(PackageId p) => SafeName.IsMatch(p.Name) && SafeArch.IsMatch(p.Arch) && SafeVersion.IsMatch(p.Version);

    /// <summary>
    /// Se algum instalador está rodando agora. Fechar o InfoPC não encerra o instalador, que roda como root: a lista de pacotes no meio da instalação
    /// parece uma lista final, e só isto mostra a diferença. Sem poder ler o processo 1, que nunca é do usuário, os de root estão escondidos.
    /// </summary>
    public static InstallerState InstallerActivity()
    {
        if (Sys.Read("/proc/1/comm") == null) return InstallerState.Unknown;
        foreach (var n in Sys.List("/proc"))
            if (n.All(char.IsDigit) && IsInstaller(n)) return InstallerState.Active;
        return InstallerState.Idle;
    }

    /// <summary>Precisa dos dois nomes: o zypper troca o comm para "Zypp-main" enquanto roda, e um roteiro como o ubuntu-drivers tem o interpretador no começo da cmdline.</summary>
    private static bool IsInstaller(string pid) =>
        (Sys.Read($"/proc/{pid}/comm") is { } comm && InstallerNames.Contains(comm))
        || (Sys.Read($"/proc/{pid}/cmdline") is { } cmd && InstallerNames.Contains(System.IO.Path.GetFileName(cmd.Split('\0')[0])));

    /// <summary>
    /// A lista para concluir uma operação, ou null se ainda não dá para confiar nela. A segunda conferência pega o instalador que começou durante a leitura.
    /// Com <paramref name="endConfirmed"/>, o fim da ação já é sabido, e só um instalador visível impede.
    /// </summary>
    private static HashSet<PackageId>? FinalList(string manager, bool endConfirmed)
    {
        bool Settled() => InstallerActivity() is var st && (st == InstallerState.Idle || (st == InstallerState.Unknown && endConfirmed));
        if (!Settled()) return null;
        return Installed(manager) is { } list && Settled() ? list : null;
    }

    /// <summary>Pacotes instalados agora, ou null se não deu para listar (sem a lista não há como desfazer).</summary>
    public static HashSet<PackageId>? Installed(string? manager)
    {
        switch (manager)
        {
            case "apt":
                // "install ok installed": só o que está instalado de fato (não "deinstall ok config-files")
                return Rows(Shell.Run("dpkg-query", new[] { "-W", "-f=${Status}\\t${Package}\\t${Architecture}\\t${Version}\\n" }, 60000), '\t',
                            f => f.Length == 4 && f[0].EndsWith(" installed", StringComparison.Ordinal) ? new PackageId(f[1], f[2], f[3]) : null);
            case "dnf" or "zypper":
                // a época 0 é a falta de época; "(none)" é a chave gpg que o rpm lista como pacote
                return Rows(Shell.Run("rpm", new[] { "-qa", "--qf", "%{NAME}\\t%{ARCH}\\t%{EPOCHNUM}:%{VERSION}-%{RELEASE}\\n" }, 60000), '\t',
                            f => f.Length == 3 && f[1] != "(none)" ? new PackageId(f[0], f[1], f[2].StartsWith("0:", StringComparison.Ordinal) ? f[2][2..] : f[2]) : null);
            case "pacman":
                return Rows(Shell.Run("pacman", new[] { "-Q" }, 60000), ' ', f => f.Length == 2 ? new PackageId(f[0], "", f[1]) : null);
            default:
                return null;
        }
    }

    private static HashSet<PackageId>? Rows(ProcResult r, char separator, Func<string[], PackageId?> parse)
    {
        if (!r.Ok) return null;
        var set = r.Out.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                       .Select(l => parse(l.Split(separator))).OfType<PackageId>().ToHashSet();
        return set.Count > 0 ? set : null;   // uma lista vazia pareceria que tudo saiu
    }

    /// <summary>
    /// Roda a ação e guarda o que ela mudou em <paramref name="s"/>. Antes da ação, a lista de pacotes vai para o disco como operação pendente: sem ler ou
    /// sem gravar, nada roda. Sem a lista de depois (falhou, ou o instalador continua rodando), a operação fica pendente e <see cref="CompletePending"/> a termina depois.
    /// Mesmo uma ação que falhou no meio fica registrada, se mudou algum pacote.
    /// </summary>
    public static (RootResult Result, string? Warning) Track(string kind, string what, string? manager, AppSettings s, Action persist, Func<RootResult> action)
    {
        if (Interlocked.Exchange(ref _busy, 1) == 1) return (Refused("outra alteração de pacotes ainda está em andamento."), null);
        try
        {
            // uma ação anterior sem lista final absorveria esta na comparação
            Complete(s, persist);
            if (s.PackageChanges.Any(c => c.Pending)) return (Refused("a alteração anterior ainda não tem a lista final de pacotes, e as duas se misturariam."), null);
            if (InstallerActivity() == InstallerState.Active) return (Refused("outro instalador de pacotes está rodando, e as mudanças dele entrariam neste desfazer. Tente de novo quando ele terminar."), null);
            var before = manager == null ? null : Installed(manager);
            if (manager == null || before == null) return (Refused("não consegui listar os pacotes instalados, e sem essa lista não daria para desfazer."), null);

            var change = new PackageChange { Kind = kind, What = what, Manager = manager, When = DateTime.Now, Pending = true, Before = before.ToList() };
            s.PackageChanges.Add(change);
            try { persist(); }
            catch (Exception ex)
            {
                s.PackageChanges.Remove(change);
                return (Refused($"não consegui guardar a lista de pacotes de antes ({ex.Message})."), null);
            }

            var r = action();
            // esgotado o tempo, o pkexec morre sem levar o instalador junto, porque um usuário comum não mata um processo de root
            var after = FinalList(manager, endConfirmed: !r.TimedOut);
            if (after == null)
            {
                string why = r.TimedOut || InstallerActivity() == InstallerState.Active ? "O instalador ainda pode estar rodando" : "Não consegui listar os pacotes agora";
                return (r, $"{why}, mas a lista de antes está guardada: o desfazer aparece ao abrir esta página de novo, depois que ele terminar.");
            }
            Conclude(change, after, s);
            try { persist(); }
            catch (Exception ex)
            {
                // no disco a operação continua pendente, e CompletePending a termina ao reabrir
                return (r, change.Added.Count + change.Removed.Count == 0 ? null : $"Não consegui guardar o desfazer ({ex.Message}). Para desfazer à mão: {ManualCommand(change)}");
            }
            return (r, null);
        }
        finally { Volatile.Write(ref _busy, 0); }
    }

    /// <summary>
    /// Termina as operações que ficaram sem lista final (falha na leitura, InfoPC fechado no meio), se nenhum instalador estiver rodando.
    /// As páginas chamam ao abrir; não muda nada no sistema.
    /// </summary>
    public static void CompletePending(AppSettings s, Action persist) => CompleteGuarded(s, persist, false);

    /// <summary>O usuário confirma que o instalador terminou, quando o sistema não deixa ver os processos de root (<see cref="InstallerState.Unknown"/>). Um instalador visível continua impedindo.</summary>
    public static void ConfirmFinished(AppSettings s, Action persist) => CompleteGuarded(s, persist, true);

    private static void CompleteGuarded(AppSettings s, Action persist, bool endConfirmed)
    {
        if (!s.PackageChanges.Any(c => c.Pending)) return;
        if (Interlocked.Exchange(ref _busy, 1) == 1) return;   // a ação ainda está rodando
        try { Complete(s, persist, endConfirmed); }
        finally { Volatile.Write(ref _busy, 0); }
    }

    /// <summary>Texto da tela para uma operação pendente, conforme o que se sabe do instalador.</summary>
    public static string PendingHint(InstallerState state) => state switch
    {
        InstallerState.Active => "Um instalador de pacotes está rodando. Quando ele terminar, clique em ATUALIZAR.",
        InstallerState.Unknown => "O sistema não deixa ver se o instalador ainda está rodando. Se ele já terminou, confirme no botão abaixo.",
        _ => "Não consegui ler a lista agora. Clique em ATUALIZAR para tentar de novo.",
    };

    private static void Complete(AppSettings s, Action persist, bool endConfirmed = false)
    {
        bool changed = false;
        foreach (var c in s.PackageChanges.Where(c => c.Pending).ToList())
        {
            if (c.Before == null) { s.PackageChanges.Remove(c); changed = true; continue; }   // sem a lista de antes não há o que comparar
            if (FinalList(c.Manager, endConfirmed) is not { } after) continue;                // tenta de novo na próxima vez, e a lista de antes continua guardada
            Conclude(c, after, s);
            c.Recovered = true;
            changed = true;
        }
        if (changed) try { persist(); } catch { }   // se não gravar, a próxima abertura repete a comparação
    }

    private static void Conclude(PackageChange c, HashSet<PackageId> after, AppSettings s)
    {
        var before = c.Before?.ToHashSet() ?? new HashSet<PackageId>();
        c.Added = Sorted(after.Except(before));
        c.Removed = Sorted(before.Except(after));
        c.Before = null;
        c.Pending = false;
        if (c.Added.Count == 0 && c.Removed.Count == 0) s.PackageChanges.Remove(c);
    }

    private static List<PackageId> Sorted(IEnumerable<PackageId> ids) =>
        ids.OrderBy(p => p.Name, StringComparer.Ordinal).ThenBy(p => p.Arch, StringComparer.Ordinal).ThenBy(p => p.Version, StringComparer.Ordinal).ToList();

    private static RootResult Refused(string why) => new(false, "", "Nada foi alterado: " + why);

    /// <summary>O que entrou e o que saiu, para a tela.</summary>
    public static string Describe(PackageChange c) =>
        string.Join(" · ", new[] { c.Added.Count > 0 ? "entraram: " + string.Join(", ", c.Added) : "", c.Removed.Count > 0 ? "saíram: " + string.Join(", ", c.Removed) : "" }.Where(x => x.Length > 0));

    /// <summary>Remove o que a ação instalou e reinstala o que ela removeu, na versão de antes. Só esquece o registro se tudo der certo.</summary>
    public static RootResult Undo(PackageChange c, AppSettings s, Action persist)
    {
        if (Interlocked.Exchange(ref _busy, 1) == 1) return Refused("outra alteração de pacotes ainda está em andamento.");
        try
        {
            if (c.Pending) return Refused("esta alteração ainda não tem a lista final de pacotes.");
            if (c.Added.Concat(c.Removed).FirstOrDefault(p => !IsSafe(p)) is { } bad)
                return new RootResult(false, "", $"Pacote inválido no registro: {bad}");
            var (install, remove, downgrade) = Plan(c);
            if (c.Manager == "pacman" && downgrade)
                return Refused("o pacman só instala a versão atual do repositório, então não dá para voltar a versão de: " + string.Join(", ", install));
            RootResult r;
            switch (c.Manager)
            {
                case "apt":
                    // uma transação só: "nome-" no apt-get install remove o pacote
                    r = Elevate.Run("apt-get", new[] { "install", "-y" }.Concat(downgrade ? new[] { "--allow-downgrades" } : Array.Empty<string>())
                                               .Concat(install.Select(p => InstallSpec("apt", p))).Concat(remove.Select(p => RemoveSpec("apt", p))), 1800000);
                    break;
                case "dnf": r = Two("dnf", new[] { "remove", "-y" }, new[] { "install", "-y" }, install, remove); break;
                case "pacman": r = Two("pacman", new[] { "-R", "--noconfirm" }, new[] { "-S", "--noconfirm" }, install, remove); break;
                case "zypper":
                    r = Two("zypper", new[] { "--non-interactive", "remove" }, new[] { "--non-interactive", "install" }.Concat(downgrade ? new[] { "--oldpackage" } : Array.Empty<string>()).ToArray(), install, remove);
                    break;
                default: return new RootResult(false, "", "Não reconheci o gerenciador de pacotes. Para desfazer à mão: " + ManualCommand(c));
            }
            if (!r.Ok)
            {
                // a versão guardada pode ter saído do repositório: instalar outra seria decisão do usuário, não do desfazer
                if (r.Cancelled || r.Unavailable || install.Count == 0 || c.Manager == "pacman") return r;
                return r with { Error = $"{r.Error.Trim()} Se a versão guardada não está mais no repositório, o comando com a versão atual é: {ManualCommand(c, exactVersions: false)}".Trim() };
            }
            s.PackageChanges.Remove(c);
            try { persist(); } catch { } // se não gravar, desfazer de novo só repete o que já voltou
            return r;
        }
        finally { Volatile.Write(ref _busy, 0); }
    }

    /// <summary>O mesmo pacote com outra versão dos dois lados é uma troca de versão: basta instalar a antiga, sem remover a nova.</summary>
    private static (List<PackageId> Install, List<PackageId> Remove, bool Downgrade) Plan(PackageChange c)
    {
        var back = c.Removed.Select(p => (p.Name, p.Arch)).ToHashSet();
        var remove = c.Added.Where(p => !back.Contains((p.Name, p.Arch))).ToList();
        return (c.Removed, remove, remove.Count < c.Added.Count);
    }

    private static string AptSlot(PackageId p) => p.Arch is "" or "all" ? p.Name : $"{p.Name}:{p.Arch}";

    private static string InstallSpec(string manager, PackageId p, bool exact = true) => manager switch
    {
        "apt" => exact ? $"{AptSlot(p)}={p.Version}" : AptSlot(p),
        "dnf" => exact ? $"{p.Name}-{p.Version}.{p.Arch}" : $"{p.Name}.{p.Arch}",
        "zypper" => exact ? $"{p.Name}.{p.Arch}={p.Version}" : $"{p.Name}.{p.Arch}",
        _ => p.Name,
    };

    private static string RemoveSpec(string manager, PackageId p) => manager switch
    {
        "apt" => AptSlot(p) + "-",
        _ => InstallSpec(manager, p),
    };

    private static RootResult Two(string pm, string[] removeArgs, string[] installArgs, List<PackageId> install, List<PackageId> remove)
    {
        RootResult r = new(true, "", "");
        if (remove.Count > 0) r = Elevate.Run(pm, removeArgs.Concat(remove.Select(p => RemoveSpec(pm, p))), 1800000);
        if (r.Ok && install.Count > 0) r = Elevate.Run(pm, installArgs.Concat(install.Select(p => InstallSpec(pm, p))), 1800000);
        return r;
    }

    /// <summary>O mesmo desfazer, para colar no terminal. Sem <paramref name="exactVersions"/>, reinstala a versão atual do repositório.</summary>
    public static string ManualCommand(PackageChange c, bool exactVersions = true)
    {
        var (install, remove, downgrade) = Plan(c);
        string add = string.Join(' ', install.Select(p => InstallSpec(c.Manager, p, exactVersions))), rm = string.Join(' ', remove.Select(p => RemoveSpec(c.Manager, p)));
        return c.Manager switch
        {
            "apt" => "sudo apt-get install " + (downgrade && exactVersions ? "--allow-downgrades " : "") + string.Join(' ', new[] { add, rm }.Where(x => x.Length > 0)),
            "dnf" => string.Join(" && ", new[] { rm.Length > 0 ? $"sudo dnf remove {rm}" : "", add.Length > 0 ? $"sudo dnf install {add}" : "" }.Where(x => x.Length > 0)),
            "pacman" => string.Join(" && ", new[] { rm.Length > 0 ? $"sudo pacman -R {rm}" : "", add.Length > 0 ? $"sudo pacman -S {add}" : "" }.Where(x => x.Length > 0)),
            "zypper" => string.Join(" && ", new[] { rm.Length > 0 ? $"sudo zypper remove {rm}" : "", add.Length > 0 ? $"sudo zypper install {(downgrade && exactVersions ? "--oldpackage " : "")}{add}" : "" }.Where(x => x.Length > 0)),
            _ => $"remova {string.Join(' ', c.Added)} e instale {string.Join(' ', c.Removed)} pelo gerenciador de pacotes",
        };
    }
}
