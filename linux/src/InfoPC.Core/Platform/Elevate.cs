using System.Runtime.InteropServices;

namespace InfoPC;

/// <summary>Resultado de uma ação que precisa de root.</summary>
public sealed record RootResult(bool Ok, string Output, string Error, bool Cancelled = false, bool Unavailable = false, string? ManualCommand = null, bool TimedOut = false)
{
    /// <summary>Mensagem pronta para a tela.</summary>
    public string Message =>
        Ok ? (string.IsNullOrWhiteSpace(Output) ? "Concluído." : Output.Trim())
        : Cancelled ? "Cancelado: a senha não foi informada."
        : Unavailable ? "Não há como pedir a senha por aqui (pkexec ausente ou sem sessão gráfica). Rode no terminal: " + ManualCommand
        : string.IsNullOrWhiteSpace(Error) ? "Falhou." : Error.Trim();
}

/// <summary>
/// Privilégios sob demanda (D-019): o programa roda como usuário comum e pede a senha, pelo pkexec do desktop,
/// só na ação que precisa de root. Nada é elevado na abertura.
/// </summary>
public static class Elevate
{
    [DllImport("libc", EntryPoint = "geteuid")] private static extern uint GetEuid();

    public static bool IsRoot
    {
        get
        {
            try { return OperatingSystem.IsLinux() && GetEuid() == 0; }
            catch { return false; }
        }
    }

    /// <summary>Só para testes.</summary>
    public static Func<string, IReadOnlyList<string>, ProcResult?>? Override { get; set; }

    public static bool HasGraphicalSession =>
        !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY")) || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"));

    public static bool CanPrompt => IsRoot || (Shell.Has("pkexec") && HasGraphicalSession);

    /// <summary>Executa um programa como root (direto, se já for root; senão pelo pkexec).</summary>
    public static RootResult Run(string file, IEnumerable<string> args, int timeoutMs = 60000)
    {
        var list = args.ToList();
        string? trusted = Shell.WhichTrusted(file);
        string exe = trusted ?? Shell.Which(file) ?? file;
        string manual = "sudo " + Shell.CommandLine(exe, list);

        ProcResult r;
        if (Override?.Invoke(file, list) is { } fake) r = fake;
        else if (trusted == null)   // só roda como root o que está nos diretórios do sistema (nada do PATH do usuário)
            return new RootResult(false, "", $"{file}: não encontrado nos diretórios do sistema (/usr/sbin, /usr/bin...).", Unavailable: true, ManualCommand: manual);
        else if (IsRoot) r = Shell.Run(exe, list, timeoutMs);
        else if (CanPrompt) r = Shell.Run("pkexec", new[] { exe }.Concat(list), timeoutMs);
        else return new RootResult(false, "", "", Unavailable: true, ManualCommand: manual);

        if (r.NotFound && !IsRoot && file != "pkexec") return new RootResult(false, "", r.Err, Unavailable: true, ManualCommand: manual);
        // pkexec: 126 = o usuário fechou a janela de senha ou não é autorizado; 127 = falha de autenticação / sem agente
        if (!IsRoot && (r.ExitCode == 126 || r.ExitCode == 127) && r.Out.Length == 0)
            return new RootResult(false, "", r.Err, Cancelled: r.ExitCode == 126, Unavailable: r.ExitCode == 127, ManualCommand: manual);
        return new RootResult(r.Ok, r.Out, r.Err, TimedOut: r.TimedOut);
    }

    /// <summary>Roda um pequeno roteiro "sh -c" como root. O roteiro é escrito pelo programa (nunca com texto livre do usuário).</summary>
    public static RootResult RunScript(string script, int timeoutMs = 60000) => Run("sh", new[] { "-c", script }, timeoutMs);

    public static Task<RootResult> RunAsync(string file, IEnumerable<string> args, int timeoutMs = 60000) =>
        Task.Run(() => Run(file, args, timeoutMs));

    public static Task<RootResult> RunScriptAsync(string script, int timeoutMs = 60000) =>
        Task.Run(() => RunScript(script, timeoutMs));
}
