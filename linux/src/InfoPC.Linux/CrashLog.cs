using Avalonia.Threading;

namespace InfoPC.Linux;

/// <summary>Diagnóstico local e limitado; nunca envia dados nem impede o encerramento por erro fatal.</summary>
internal static class CrashLog
{
    private static readonly object Gate = new();

    internal const int BurstLimit = 5;
    internal const long BurstWindowMs = 10_000;
    private static readonly Queue<long> Burst = new();

    /// <summary>Uma ação da interface falhou e o programa continuou; o texto é o caminho do registro. Chamado na thread da tela.</summary>
    public static event Action<string>? Recovered;

    // Não acesse Dispatcher.UIThread aqui: antes do AppBuilder ele fica ligado ao NullDispatcherImpl.

    public static void Install()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex) Write("Erro fatal", ex);
        };
        TaskScheduler.UnobservedTaskException += (_, e) => Write("Tarefa em segundo plano", e.Exception);
    }

    public static void InstallDispatcher() =>
        Dispatcher.UIThread.UnhandledException += (_, e) =>
        {
            Write("Interface", e.Exception);
            if (!CanContinue(e.Exception, Environment.TickCount64)) return;
            e.Handled = true;
            Recovered?.Invoke(Path.Combine(AppSettings.Folder, "erros.log"));
        };

    /// <summary>
    /// Falha isolada numa ação de página não fecha o programa (o stress em andamento segue com seus limites de segurança, que encerrar o processo abandonaria).
    /// Falta de memória ou uma rajada, típica de erro em todo layout ou desenho, encerra: repetir para sempre seria travar a janela.
    /// </summary>
    internal static bool CanContinue(Exception ex, long nowMs)
    {
        if (ex is OutOfMemoryException or AccessViolationException) return false;
        lock (Gate)
        {
            while (Burst.Count > 0 && nowMs - Burst.Peek() > BurstWindowMs) Burst.Dequeue();
            Burst.Enqueue(nowMs);
            return Burst.Count <= BurstLimit;
        }
    }

    internal static void ResetForTests()
    {
        lock (Gate) Burst.Clear();
    }

    public static void Write(string context, Exception ex)
    {
        try
        {
            lock (Gate)
            {
                string file = Path.Combine(AppSettings.Folder, "erros.log");
                Directory.CreateDirectory(AppSettings.Folder);
                if (File.Exists(file) && new FileInfo(file).Length >= 256 * 1024)
                    File.Move(file, file + ".anterior", overwrite: true);
                string detail = ex.ToString();
                if (detail.Length > 16000) detail = detail[..16000] + "…";
                File.AppendAllText(file, $"{DateTimeOffset.Now:O} · InfoPC {AppInfo.Version} · {context}\n{detail}\n\n");
            }
        }
        catch { /* falha de gravação não pode causar outra exceção */ }
    }
}
