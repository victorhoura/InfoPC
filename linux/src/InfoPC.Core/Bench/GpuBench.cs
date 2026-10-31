using System.Diagnostics;
using System.Text.RegularExpressions;

namespace InfoPC;

public sealed record GpuBenchResult(string Tool, double? Score, string Output, string? Error);

/// <summary>
/// GPU no Linux: não há Direct3D, então o benchmark e o stress usam o vkmark (Vulkan) ou o glmark2 (OpenGL), se estiverem instalados.
/// A nota deles não é comparável com a do Windows; serve para comparar a mesma máquina antes e depois de um ajuste.
/// </summary>
public static class GpuBench
{
    public static readonly string[] Tools = { "vkmark", "glmark2", "glmark2-es2", "glmark2-wayland" };

    public static string? FindTool() => Tools.FirstOrDefault(Shell.Has);

    private static readonly Regex ScoreRx = new(@"(?:vkmark|glmark2)\S*\s+Score:\s*(\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static double? ParseScore(string output)
    {
        var m = ScoreRx.Match(output);
        return m.Success ? Num.D(m.Groups[1].Value) : null;
    }

    public static async Task<GpuBenchResult> RunAsync(string tool, CancellationToken ct, Action<string>? onLine = null)
    {
        var r = await ProcessRunner.RunAsync(tool, Array.Empty<string>(), ct, 600000, onLine);
        if (ct.IsCancellationRequested) return new GpuBenchResult(tool, null, r.Out, "cancelado");
        if (r.NotFound) return new GpuBenchResult(tool, null, "", $"{tool} não está instalado");
        double? score = ParseScore(r.Out);
        if (score == null) return new GpuBenchResult(tool, null, r.Out, r.TimedOut ? "o teste demorou demais" : (r.Err.Trim().Length > 0 ? LastLine(r.Err) : "o teste não devolveu nota (sem display ou sem driver 3D?)"));
        return new GpuBenchResult(tool, score, r.Out, null);
    }

    private static string LastLine(string s) => s.Split('\n', StringSplitOptions.RemoveEmptyEntries).LastOrDefault()?.Trim() ?? "";
}

/// <summary>Carga contínua na GPU: roda o vkmark/glmark2 em laço (reinicia quando termina) até <see cref="Stop"/>.</summary>
public sealed class GpuStress
{
    private CancellationTokenSource? _cts;
    private Process? _proc;
    private volatile bool _ended;
    public string? Tool { get; private set; }
    public string? Error { get; private set; }
    public bool Running => _cts != null && !_ended;
    /// <summary>O programa de carga parou sozinho (não foi o <see cref="Stop"/>): a GPU ficou sem carga. O motivo está em <see cref="Error"/>.</summary>
    public bool Failed => _cts != null && _ended;

    public bool Start(string tool)
    {
        if (Running) return true;
        string? exe = Shell.Which(tool);
        if (exe == null) { Error = $"{tool} não está instalado"; return false; }
        Tool = tool; Error = null; _ended = false;
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        bool glmark = tool.StartsWith("glmark2", StringComparison.Ordinal);
        _ = Task.Run(async () =>
        {
            int quick = 0;
            while (!token.IsCancellationRequested)
            {
                var started = DateTime.Now;
                var p = ProcessRunner.Start(exe, glmark ? new[] { "--run-forever" } : Array.Empty<string>(), null, redirect: true);
                _proc = p;
                if (p == null) { Error = "não consegui iniciar " + tool; break; }
                p.OutputDataReceived += (_, _) => { };
                p.ErrorDataReceived += (_, _) => { };
                p.BeginOutputReadLine(); p.BeginErrorReadLine();
                try { await p.WaitForExitAsync(token); } catch (OperationCanceledException) { break; }
                if (token.IsCancellationRequested) break;
                // código 0 é o fim normal de uma rodada, e o laço começa outra. Qualquer outro é falha, mesmo depois de horas: pode ser o driver caindo
                if (p.ExitCode != 0) { Error = $"{tool} fechou com erro (código {p.ExitCode}; sem display, sem driver 3D ou o driver caiu)"; break; }
                // saiu em menos de 3 s várias vezes seguidas: sem display ou sem driver, não adianta insistir
                quick = (DateTime.Now - started).TotalSeconds < 3 ? quick + 1 : 0;
                if (quick >= 3) { Error = tool + " fechou sozinho (sem display ou sem driver 3D?)"; break; }
            }
            if (!token.IsCancellationRequested) _ended = true;   // quem acompanha o teste fica sabendo que a carga parou
        });
        return true;
    }

    public void Stop()
    {
        _cts?.Cancel();
        ProcessRunner.KillTree(_proc);
        _cts = null; _proc = null;
    }
}
