using Avalonia;

namespace InfoPC.Linux;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // Modo texto (--info, --sensores, --relatorio...): não abre janela, serve para terminal, SSH e scripts
        if (Cli.Run(args) is { } code) return code;

        Locale.Init();
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont().LogToTrace();
}
