using Avalonia;
using Avalonia.Headless;
using InfoPC.Linux;

[assembly: AvaloniaTestApplication(typeof(InfoPC.Tests.TestAppBuilder))]

namespace InfoPC.Tests;

public static class TestAppBuilder
{
    // Render real com Skia (sem janela): permite salvar capturas de tela das paginas para conferir o layout.
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
                  .WithInterFont()
                  .UseSkia()
                  .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
