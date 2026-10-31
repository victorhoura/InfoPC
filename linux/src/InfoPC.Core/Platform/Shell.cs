using System.Diagnostics;
using System.Text;

namespace InfoPC;

public sealed record ProcResult(int ExitCode, string Out, string Err, bool TimedOut = false, bool NotFound = false)
{
    public bool Ok => ExitCode == 0 && !TimedOut && !NotFound;
    public static ProcResult Missing(string file) => new(127, "", $"{file}: comando não encontrado", false, true);
}

/// <summary>
/// Executa programas externos SEM shell: os argumentos vão em lista (nada de montar comando por texto),
/// com limite de tempo e idioma fixo (LC_ALL=C) para a saída ser estável.
/// </summary>
public static class Shell
{
    /// <summary>Só para testes: devolve uma saída pronta no lugar de executar (null = executa de verdade).</summary>
    public static Func<string, IReadOnlyList<string>, ProcResult?>? Override { get; set; }

    private static readonly string[] ExtraDirs = { "/usr/local/sbin", "/usr/sbin", "/sbin", "/usr/local/bin", "/usr/bin", "/bin" };

    /// <summary>Diretórios do sistema (donos: root). Só o que está aqui pode rodar como root.</summary>
    private static readonly string[] TrustedDirs = ExtraDirs.Concat(new[] { "/run/current-system/sw/bin", "/run/wrappers/bin" }).ToArray();

    /// <summary>
    /// Como <see cref="Which"/>, mas só nos diretórios do sistema, ignorando o PATH do usuário. Usado antes de rodar algo como root:
    /// um programa com o mesmo nome em ~/.local/bin ou em outra pasta do PATH não pode ganhar privilégios pela senha do usuário.
    /// </summary>
    public static string? WhichTrusted(string cmd)
    {
        if (cmd.Contains('/') || Path.IsPathRooted(cmd)) return TrustedDirs.Any(d => cmd.StartsWith(d + "/", StringComparison.Ordinal)) && File.Exists(cmd) ? cmd : null;
        foreach (var d in TrustedDirs)
        {
            try
            {
                string p = Path.Combine(d, cmd);
                if (File.Exists(p)) return p;
            }
            catch { }
        }
        return null;
    }

    /// <summary>Caminho completo do programa, procurando no PATH e nos diretórios de administração (/usr/sbin não costuma estar no PATH de usuário).</summary>
    public static string? Which(string cmd)
    {
        if (cmd.Contains('/')) return File.Exists(cmd) ? cmd : null;
        var dirs = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(':', StringSplitOptions.RemoveEmptyEntries).Concat(ExtraDirs);
        foreach (var d in dirs)
        {
            try
            {
                string p = Path.Combine(d, cmd);
                if (File.Exists(p)) return p;
            }
            catch { }
        }
        return null;
    }

    public static bool Has(string cmd)
    {
        if (Override?.Invoke(cmd, Array.Empty<string>()) is { } fake) return !fake.NotFound;
        return Which(cmd) != null;
    }

    public static ProcResult Run(string file, IEnumerable<string>? args = null, int timeoutMs = 8000, string? stdin = null,
                                 IDictionary<string, string>? env = null)
    {
        var list = args?.ToList() ?? new List<string>();
        if (Override?.Invoke(file, list) is { } fake) return fake;
        string? exe = Which(file);
        if (exe == null) return ProcResult.Missing(file);

        var psi = new ProcessStartInfo(exe)
        {
            RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = stdin != null,
            UseShellExecute = false, CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var a in list) psi.ArgumentList.Add(a);
        psi.Environment["LC_ALL"] = "C";
        psi.Environment["LANG"] = "C";
        if (env != null) foreach (var kv in env) psi.Environment[kv.Key] = kv.Value;

        try
        {
            using var p = Process.Start(psi);
            if (p == null) return ProcResult.Missing(file);
            var so = new StringBuilder(); var se = new StringBuilder();
            p.OutputDataReceived += (_, e) => { if (e.Data != null) lock (so) so.AppendLine(e.Data); };
            p.ErrorDataReceived += (_, e) => { if (e.Data != null) lock (se) se.AppendLine(e.Data); };
            p.BeginOutputReadLine(); p.BeginErrorReadLine();
            if (stdin != null) { p.StandardInput.Write(stdin); p.StandardInput.Close(); }
            if (!p.WaitForExit(timeoutMs))
            {
                try { p.Kill(true); } catch { }
                p.WaitForExit(1000);
                lock (so) lock (se) return new ProcResult(-1, so.ToString(), se.ToString(), TimedOut: true);
            }
            p.WaitForExit(); // garante o fim da leitura assíncrona
            lock (so) lock (se) return new ProcResult(p.ExitCode, so.ToString(), se.ToString());
        }
        catch (Exception ex) { return new ProcResult(126, "", ex.Message); }
    }

    public static Task<ProcResult> RunAsync(string file, IEnumerable<string>? args = null, int timeoutMs = 8000, string? stdin = null,
                                            IDictionary<string, string>? env = null) =>
        Task.Run(() => Run(file, args, timeoutMs, stdin, env));

    /// <summary>Abre um endereço, arquivo ou pasta com o programa padrão do desktop (xdg-open).</summary>
    public static bool Open(string target)
    {
        if (Which("xdg-open") is not { } xdg) return false;
        try
        {
            var psi = new ProcessStartInfo(xdg) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            psi.ArgumentList.Add(target);
            Process.Start(psi);
            return true;
        }
        catch { return false; }
    }

    /// <summary>Aspas simples para mostrar/copiar um comando de terminal (nunca é usado para executar).</summary>
    public static string Quote(string s) =>
        s.Length > 0 && s.All(c => char.IsLetterOrDigit(c) || "-_./=:,+@%".Contains(c)) ? s : "'" + s.Replace("'", "'\\''") + "'";

    public static string CommandLine(string file, IEnumerable<string> args) => string.Join(' ', new[] { file }.Concat(args).Select(Quote));
}
