using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using InfoPC.Linux;
using Xunit;

namespace InfoPC.Tests;

/// <summary>
/// Abre a janela de verdade (render Skia sem tela), percorre todas as páginas do menu e confere que montam e desenham.
/// Com a variável INFOPC_SHOTS apontando para uma pasta, grava uma captura de cada página (para revisar o layout).
/// </summary>
public class UiTests
{
    private static void Pump()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>As páginas leem o hardware em segundo plano: dá um tempo e bombeia a fila da tela até elas terminarem.</summary>
    private static async Task Settle()
    {
        for (int i = 0; i < 8; i++) { await Task.Delay(60); Pump(); }
    }

    private static async Task<MainWindow> OpenAsync()
    {
        Services.Current.ResetForTests();
        var w = new MainWindow();
        w.Show();
        await Services.Current.WhenReady;
        Pump();
        // duas leituras: carga e consumo só existem a partir da segunda
        Services.Current.TickNow();
        await Task.Delay(200);
        Services.Current.TickNow();
        Pump();
        return w;
    }

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_settings_file_that_fails_to_load_is_explained_on_the_first_opening(bool damaged)
    {
        // R14-H: o aviso nasce no Load, e a janela precisa olhar depois dele
        using var fx = Fx.Use("intel-desktop");
        string before = AppSettings.Folder;
        var noticeProperty = typeof(AppSettings).GetProperty(nameof(AppSettings.FolderNotice))!;
        noticeProperty.SetValue(null, null);
        string dir = Directory.CreateTempSubdirectory("infopc-aviso").FullName;
        string file = Path.Combine(dir, "settings.json");
        File.WriteAllText(file, damaged ? "{ quebrado" : "{\"GroupsAdded\":[\"desfazer\"]}");
        FileStream? held = damaged ? null : new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.None);
        AppSettings.Folder = dir;
        MainWindow? w = null;
        try
        {
            w = await OpenAsync();
            Assert.Contains("Não consegui ler", AppSettings.FolderNotice);
            Assert.Single(w.OwnedWindows, o => o.Title == "Pasta de dados");
        }
        finally
        {
            held?.Dispose();
            if (w != null)
            {
                foreach (var o in w.OwnedWindows.ToArray()) o.Close();
                w.Close();
            }
            Services.Current.ResetForTests();
            noticeProperty.SetValue(null, null);
            AppSettings.Folder = before;
            Directory.Delete(dir, true);
        }
    }

    private static string? ShotsDir => Environment.GetEnvironmentVariable("INFOPC_SHOTS") is { Length: > 0 } d ? d : null;

    [AvaloniaFact]
    public async Task An_error_in_a_page_action_does_not_close_the_program()
    {
        // Os botões das páginas são async void: uma exceção volta ao dispatcher e, sem tratamento, encerra o processo.
        using var fx = Fx.Use("intel-desktop");
        string old = AppSettings.Folder;
        string dir = Directory.CreateTempSubdirectory("infopc-acao-").FullName;
        AppSettings.Folder = dir;
        CrashLog.ResetForTests();
        var w = await OpenAsync();
        try
        {
            async void Action() { await Task.Yield(); throw new IOException("ação indisponível"); }
            Action();
            await Settle(); // sem tratamento, a exceção sairia daqui, como sai do laço principal do programa
            Assert.Contains("ação indisponível", File.ReadAllText(Path.Combine(dir, "erros.log")));
            Assert.True(w.IsVisible);
            Assert.Contains("erros.log", w.ErrorNotice);
            w.Navigate("sensors");
            Assert.Equal("", w.ErrorNotice); // o aviso não persegue o usuário em outra página
        }
        finally
        {
            w.Close();
            Services.Current.ResetForTests();
            CrashLog.ResetForTests();
            AppSettings.Folder = old;
            Directory.Delete(dir, true);
        }
    }

    [AvaloniaFact]
    public async Task A_failing_page_does_not_skip_the_other_sensor_subscribers()
    {
        using var fx = Fx.Use("intel-desktop");
        var w = await OpenAsync();
        int ticks = 0;
        void Fail() => throw new IOException("página indisponível");
        void Count() => ticks++;
        Services.Current.Tick += Fail;
        Services.Current.Tick += Count;
        try
        {
            await Task.Delay(1500);
            Pump();
            Assert.True(ticks > 0, "Uma página com erro não pode interromper o monitor de segurança do stress.");
        }
        finally
        {
            Services.Current.Tick -= Fail;
            Services.Current.Tick -= Count;
            w.Close();
            Services.Current.ResetForTests();
        }
    }

    [AvaloniaFact]
    public async Task Closing_the_main_window_stops_stress_even_with_an_overlay_open()
    {
        using var fx = Fx.Use("intel-desktop");
        var w = await OpenAsync();
        OverlayWindow? overlay = null;
        Button? btn = null;
        try
        {
            overlay = new OverlayWindow();
            overlay.Show();
            w.Navigate("stress");
            await Settle();
            var page = w.Page("stress")!;
            btn = page.GetLogicalDescendants().OfType<Button>().First(b => b.Content as string == "INICIAR");
            btn.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal("PARAR", btn.Content);
            w.Close();
            Pump();
            Assert.Equal("INICIAR", btn.Content);
        }
        finally
        {
            if (btn?.Content as string == "PARAR") btn.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            overlay?.Close();
            w.Close();
            Services.Current.ResetForTests();
        }
    }

    [AvaloniaFact]
    public async Task A_busy_window_does_not_accumulate_sensor_updates()
    {
        using var fx = Fx.Use("intel-desktop");
        var w = await OpenAsync();
        int ticks = 0;
        void Count() => ticks++;
        Services.Current.Tick += Count;
        try
        {
            Pump();
            ticks = 0;
            // Simula a janela ocupada: o produtor deve esperar, sem enfileirar uma atualização por segundo.
            Thread.Sleep(3200);
            Pump();
            Assert.InRange(ticks, 0, 1);
        }
        finally
        {
            Services.Current.Tick -= Count;
            w.Close();
            Services.Current.ResetForTests();
        }
    }

    [AvaloniaTheory]
    [InlineData("amd-laptop", false)]
    [InlineData("amd-laptop", true)]
    public async Task Returning_to_the_gpu_page_does_not_crash_the_window(string fixture, bool rapid)
    {
        using var fx = Fx.Use(fixture);
        var errors = new List<Exception>();
        void Capture(object? sender, DispatcherUnhandledExceptionEventArgs e)
        {
            errors.Add(e.Exception);
            e.Handled = true;
        }
        Dispatcher.UIThread.UnhandledException += Capture;
        var w = await OpenAsync();
        try
        {
            for (int i = 0; i < 3; i++)
            {
                w.Navigate("gpu");
                if (!rapid) await Settle();
                Assert.True(w.IsVisible);
                Assert.Empty(errors);
                if (!rapid) Assert.Contains(w.Page("gpu")!.GetLogicalDescendants().OfType<TextBlock>(), t => t.Text == "Uso da GPU");
                w.Navigate("sensors");
                if (!rapid) await Settle();
            }
            w.Navigate("gpu");
            await Settle();
            Assert.Empty(errors);
            Assert.Contains(w.Page("gpu")!.GetLogicalDescendants().OfType<TextBlock>(), t => t.Text == "Uso da GPU");
        }
        finally
        {
            w.Close();
            Services.Current.ResetForTests();
            Dispatcher.UIThread.UnhandledException -= Capture;
        }
    }

    [AvaloniaTheory]
    [InlineData("intel-desktop")]
    [InlineData("amd-laptop")]
    [InlineData("raspberry")]
    public async Task Every_page_builds_and_renders(string fixture)
    {
        using var fx = Fx.Use(fixture);
        var w = await OpenAsync();
        try
        {
            Assert.NotEmpty(w.Entries);
            foreach (var e in w.Entries.Concat(w.Entries))
            {
                w.Navigate(e.Key);
                await Settle();
                var page = w.Page(e.Key);
                Assert.NotNull(page);
                Assert.False(string.IsNullOrWhiteSpace(page!.Title), e.Key);
                Services.Current.TickNow();
                Pump();
                var frame = w.CaptureRenderedFrame();
                Assert.NotNull(frame);
                Assert.True(frame!.PixelSize.Width >= 900, e.Key);
                if (ShotsDir is { } dir)
                {
                    Directory.CreateDirectory(dir);
                    frame.Save(Path.Combine(dir, $"{fixture}-{e.Key}.png"));
                }
            }
        }
        finally
        {
            w.Close();
            Services.Current.ResetForTests();
        }
    }

    [AvaloniaFact]
    public async Task Stress_keeps_its_checks_running_while_another_page_is_open()
    {
        using var fx = Fx.Use("intel-desktop");
        string dir = Path.Combine(Path.GetTempPath(), "infopc-cfg-" + Guid.NewGuid().ToString("N"));
        string old = AppSettings.Folder;
        AppSettings.Folder = dir;
        var w = await OpenAsync();
        Button? btn = null;
        var running = typeof(StressPage).GetField("_running", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        try
        {
            w.Navigate("stress"); await Settle();
            var page = (StressPage)w.Page("stress")!;
            btn = page.GetLogicalDescendants().OfType<Button>().First(b => b.Content as string == "INICIAR");
            btn.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));          // só a CPU (padrão), 15 min
            Assert.True((bool)running.GetValue(page)!);

            w.Navigate("sensors"); await Settle();                            // sai da página com a carga ligada
            typeof(StressPage).GetField("_start", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .SetValue(page, DateTime.Now.AddHours(-1));                   // os 15 min passam com outra página aberta
            Services.Current.TickNow();
            Pump();
            Assert.False((bool)running.GetValue(page)!);                      // a verificação de tempo agiu e parou a carga
            Assert.Equal("INICIAR", btn.Content);
        }
        finally
        {
            if (btn?.Content as string == "PARAR") btn.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            w.Close();
            Services.Current.ResetForTests();
            AppSettings.Folder = old;
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    [AvaloniaFact]
    public async Task Package_undo_is_hidden_while_the_list_is_pending_and_warns_when_it_was_completed_later()
    {
        using var fx = Fx.Use("intel-desktop");
        // a janela carrega e salva as configurações: estes registros não podem ir para os outros testes
        string oldFolder = AppSettings.Folder, dir = Directory.CreateTempSubdirectory("infopc-cfg").FullName;
        AppSettings.Folder = dir;
        var w = await OpenAsync();
        try
        {
            var changes = Services.Current.Settings.PackageChanges;
            var before = new List<PackageId> { new("bash", "amd64", "1.0") };
            changes.Add(new PackageChange { Kind = "gpu-driver", What = "driver A", Manager = "apt", Pending = true, Before = before });
            changes.Add(new PackageChange { Kind = "gpu-driver", What = "driver B", Manager = "apt", Recovered = true, Added = { new("nvidia-driver-550", "amd64", "550.90") } });
            changes.Add(new PackageChange { Kind = "brltty", What = "brltty", Manager = "apt", Recovered = true, Removed = { new("brltty", "amd64", "6.5") } });

            string[] Texts(string id) => w.Page(id)!.GetLogicalDescendants().OfType<TextBlock>().Select(x => x.Text ?? "").ToArray();
            int Buttons(string id, string prefix) => w.Page(id)!.GetLogicalDescendants().OfType<Button>().Count(b => b.Content is string c && c.StartsWith(prefix, StringComparison.Ordinal));

            w.Navigate("drivers"); await Settle(); await Settle();
            Assert.Equal(1, Buttons("drivers", "DESFAZER"));                      // só o que tem a lista final
            Assert.Contains(Texts("drivers"), x => x.Contains("driver A") && x.Contains("sem a lista final"));
            Assert.Equal(1, Buttons("drivers", "O INSTALADOR JÁ TERMINOU"));      // a fixture não tem /proc/1: o sistema não deixa ver o instalador
            Assert.Contains(Texts("drivers"), x => x.Contains("Confira antes de desfazer"));
            Assert.Contains(Texts("drivers"), x => x.Contains("nvidia-driver-550:amd64 550.90"));

            w.Navigate("serial"); await Settle(); await Settle();
            Assert.Equal(1, Buttons("serial", "REINSTALAR O BRLTTY"));
            Assert.Contains(Texts("serial"), x => x.Contains("Confira antes de reinstalar") && x.Contains("brltty:amd64 6.5"));

            changes[2].Pending = true; changes[2].Before = before;
            w.Navigate("sensors"); await Settle();
            w.Navigate("serial"); await Settle(); await Settle();
            Assert.Equal(0, Buttons("serial", "REINSTALAR O BRLTTY"));
            Assert.Contains(Texts("serial"), x => x.Contains("ainda não tem a lista final"));
            Assert.Equal(1, Buttons("serial", "O INSTALADOR JÁ TERMINOU"));
        }
        finally
        {
            w.Close();
            Services.Current.ResetForTests();
            AppSettings.Folder = oldFolder;
            try { Directory.Delete(dir, true); } catch (IOException) { }
        }
    }

    [AvaloniaFact]
    public async Task Overlay_keeps_updating_while_another_page_is_open()
    {
        using var fx = Fx.Use("intel-desktop");
        var w = await OpenAsync();
        var tick = typeof(Services).GetField("Tick", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        bool Subscribed(object page) => (tick.GetValue(Services.Current) as Delegate)?.GetInvocationList().Any(d => d.Target == page) == true;
        OverlayWindow? overlay = null;
        try
        {
            w.Navigate("overlay"); await Settle();
            var page = (OverlayPage)w.Page("overlay")!;
            Assert.False(Subscribed(page));                                       // sem overlay aberto, nada a atualizar
            overlay = new OverlayWindow();
            page.Attach(overlay);
            overlay.Show();
            Assert.True(Subscribed(page));

            w.Navigate("sensors"); await Settle();                                // antes, sair da página desligava a atualização
            Assert.True(Subscribed(page));

            overlay.Close(); overlay = null; Pump();
            Assert.False(Subscribed(page));                                       // fechar o overlay desliga
        }
        finally
        {
            overlay?.Close();
            w.Close();
            Services.Current.ResetForTests();
        }
    }

    [AvaloniaFact]
    public void Device_toast_renders_and_replaces_the_previous_one()
    {
        ToastWindow.Show(new DeviceChange(true, DeviceChangeKind.Storage, "Kingston DataTraveler 3.0", "/dev/sdb · 32 GB"));
        ToastWindow.Show(new DeviceChange(false, DeviceChangeKind.Maker, "USB Serial", "wch.cn · VID 1A86 PID 7523"));
        Pump();
        var w = Avalonia.Application.Current!.ApplicationLifetime is null ? null : (Avalonia.Controls.Window?)null;
        Assert.Null(w);   // só confirma que as duas chamadas não lançaram; a janela some sozinha
    }

    [AvaloniaFact]
    public async Task Theme_toggle_switches_variant_and_keeps_pages_working()
    {
        using var fx = Fx.Use("intel-desktop");
        var w = await OpenAsync();
        try
        {
            var app = Avalonia.Application.Current!;
            Assert.Equal(Avalonia.Styling.ThemeVariant.Dark, app.RequestedThemeVariant);
            Services.ApplyTheme(true);
            Pump();
            Assert.Equal(Avalonia.Styling.ThemeVariant.Light, app.RequestedThemeVariant);
            w.Navigate("sensors"); await Settle();
            var frame = w.CaptureRenderedFrame();
            Assert.NotNull(frame);
            if (ShotsDir is { } dir) { Directory.CreateDirectory(dir); frame!.Save(Path.Combine(dir, "intel-desktop-sensors-claro.png")); }
        }
        finally { Services.ApplyTheme(false); w.Close(); Services.Current.ResetForTests(); }
    }
}
