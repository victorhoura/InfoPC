using System.Diagnostics;
using System.Text;

namespace InfoPC;

/// <summary>
/// Programas externos de longa duração (benchmarks de GPU, instalações): cancelável, com saída linha a linha para a tela,
/// e processo em segundo plano que o programa encerra depois. Nunca usa shell.
/// </summary>
public static class ProcessRunner
{
    public static async Task<ProcResult> RunAsync(string file, IEnumerable<string> args, CancellationToken ct, int timeoutMs = 900000,
                                                  Action<string>? onLine = null, IDictionary<string, string>? env = null)
    {
        var list = args.ToList();
        if (Shell.Override?.Invoke(file, list) is { } fake) return fake;
        string? exe = Shell.Which(file);
        if (exe == null) return ProcResult.Missing(file);

        using var p = Start(exe, list, env, redirect: true);
        if (p == null) return ProcResult.Missing(file);
        var so = new StringBuilder(); var se = new StringBuilder();
        p.OutputDataReceived += (_, e) => { if (e.Data != null) { lock (so) so.AppendLine(e.Data); onLine?.Invoke(e.Data); } };
        p.ErrorDataReceived += (_, e) => { if (e.Data != null) { lock (se) se.AppendLine(e.Data); onLine?.Invoke(e.Data); } };
        p.BeginOutputReadLine(); p.BeginErrorReadLine();

        using var reg = ct.Register(() => KillTree(p));
        using var timeout = new CancellationTokenSource(timeoutMs);
        using var treg = timeout.Token.Register(() => KillTree(p));
        try { await p.WaitForExitAsync(); }
        catch { /* processo já encerrado */ }
        lock (so) lock (se)
            return new ProcResult(ct.IsCancellationRequested || timeout.IsCancellationRequested ? -1 : p.ExitCode, so.ToString(), se.ToString(),
                                  TimedOut: timeout.IsCancellationRequested && !ct.IsCancellationRequested);
    }

    /// <summary>Inicia um programa e devolve o processo (o chamador encerra com <see cref="KillTree"/>).</summary>
    public static Process? Start(string exe, IEnumerable<string> args, IDictionary<string, string>? env = null, bool redirect = false)
    {
        try
        {
            var psi = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = redirect, RedirectStandardError = redirect };
            foreach (var a in args) psi.ArgumentList.Add(a);
            if (env != null) foreach (var kv in env) psi.Environment[kv.Key] = kv.Value;
            return Process.Start(psi);
        }
        catch { return null; }
    }

    public static void KillTree(Process? p)
    {
        try { if (p is { HasExited: false }) p.Kill(true); } catch { }
    }
}
