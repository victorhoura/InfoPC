using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;

// Processo novo: os testes de UI já têm uma plataforma, e não detectam um dispatcher criado cedo demais.
typeof(InfoPC.Linux.Program).Assembly.GetType("InfoPC.Linux.CrashLog")!
    .GetMethod("Install")!.Invoke(null, null);
AppBuilder.Configure<Application>().UseHeadless(new AvaloniaHeadlessPlatformOptions()).SetupWithoutStarting();
if (!Dispatcher.UIThread.SupportsRunLoops)
{
    Console.Error.WriteLine("O diagnóstico inicializou o dispatcher antes da plataforma e impediu o loop da janela.");
    return 1;
}
Console.WriteLine("Plataforma inicializada; dispatcher pronto para o loop da janela.");
return 0;
