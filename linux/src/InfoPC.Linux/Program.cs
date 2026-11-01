using Avalonia;

namespace InfoPC.Linux;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        CrashLog.Install();
        // Modo texto (--info, --sensores, --relatorio...): não abre janela, serve para terminal, SSH e scripts
        if (Cli.Run(args) is { } code) return code;

        Locale.Init();
        try { return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args); }
        catch (Exception ex) when (Cli.WhyNoWindow(ex) is { } why)
        {
            // sem tela ou sem biblioteca gráfica: explica em vez de abortar com core dump
            Console.Error.WriteLine(why);
            return 1;
        }
        catch (Exception ex)
        {
            CrashLog.Write("Encerramento inesperado", ex);
            Console.Error.WriteLine(ex);
            Console.Error.WriteLine("O InfoPC encontrou um erro. Detalhes em " + Path.Combine(AppSettings.Folder, "erros.log"));
            return 1;
        }
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont().LogToTrace();
}
