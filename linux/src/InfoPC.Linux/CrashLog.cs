using Avalonia.Threading;

namespace InfoPC.Linux;

/// <summary>Diagnóstico local e limitado; nunca envia dados nem impede o encerramento por erro fatal.</summary>
internal static class CrashLog
{
    private static readonly object Gate = new();

    public static void Install()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex) Write("Erro fatal", ex);
        };
        Dispatcher.UIThread.UnhandledException += (_, e) => Write("Interface", e.Exception);
        TaskScheduler.UnobservedTaskException += (_, e) => Write("Tarefa em segundo plano", e.Exception);
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
