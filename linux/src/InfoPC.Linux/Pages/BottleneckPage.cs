using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace InfoPC.Linux;

/// <summary>Marcador de equilíbrio GPU × CPU (escala logarítmica da razão, de 0,4× a 2,5×; o centro é "equilibrado").</summary>
public sealed class BalanceBar : Control
{
    public double Ratio { get; set; } = 1;
    public BalanceBar() { Height = 30; }

    public override void Render(DrawingContext ctx)
    {
        base.Render(ctx);
        double w = Bounds.Width, mid = Bounds.Height / 2;
        var line = Ui.Brush(this, "LineStrong"); var fg = Ui.Brush(this, "Fg"); var muted = Ui.Brush(this, "Muted");
        ctx.DrawLine(new Pen(line, 2), new Point(8, mid), new Point(w - 8, mid));
        ctx.DrawLine(new Pen(muted, 1), new Point(w / 2, mid - 8), new Point(w / 2, mid + 8));
        double t = Math.Clamp((Math.Log(Ratio) - Math.Log(0.4)) / (Math.Log(2.5) - Math.Log(0.4)), 0, 1);
        ctx.DrawEllipse(fg, null, new Point(8 + t * (w - 16), mid), 7, 7);
    }
}

/// <summary>Gargalo: equilíbrio teórico GPU × CPU, leitura ao vivo do que limita o FPS e análise de uma sessão (1, 3 ou 5 min).</summary>
public sealed class BottleneckPage : PageBase
{
    public override string Title => "Gargalo";
    public override string Subtitle => "Descubra o que limita o FPS: CPU, GPU, memória de vídeo, RAM ou temperatura. O FPS dentro do jogo aparece pelo MangoHud (página Overlay).";

    private readonly TextBlock _cpuName = Ui.Muted(""), _gpuName = Ui.Muted(""), _balance = Ui.Note("");
    private readonly BalanceBar _bar = new();
    private readonly Border _live = new() { };
    private readonly TextBlock _liveTitle = new() { FontSize = 20, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _liveGame = new() { FontSize = 12.5, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _liveAdvice = new() { FontSize = 12.5, TextWrapping = TextWrapping.Wrap };
    private readonly StackPanel _meters = new() { Spacing = 10 };
    private readonly StatusText _sessionStatus = new();
    private readonly Button _sessionBtn;
    private readonly StackPanel _result = new() { Spacing = 8 };
    private BottleneckSession? _session;
    private int _durIdx;
    private List<LibGame> _library = new();
    private DateTime _libAt;

    public BottleneckPage()
    {
        _sessionBtn = Ui.Primary("INICIAR ANÁLISE", Toggle);
        _live.Padding = new Thickness(20); _live.CornerRadius = new CornerRadius(16); _live.BorderThickness = new Thickness(1);
        _live.Child = Ui.VStack(6, Ui.Label("Agora"), _liveTitle, _liveGame, _liveAdvice);

        var balance = Ui.VStack(10, Ui.HStack(30, _cpuName, _gpuName), _bar, _balance);
        var top = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 16 };
        var c1 = Ui.Card(balance, "Equilíbrio teórico GPU × CPU");
        var c2 = Ui.Card(_meters, "Uso agora");
        Grid.SetColumn(c2, 1);
        top.Children.Add(c1); top.Children.Add(c2);

        Body.Children.Add(_live);
        Body.Children.Add(top);
        Body.Children.Add(Ui.Card(Ui.VStack(12,
            Ui.Note("Abra o jogo e jogue normalmente durante a análise: o InfoPC amostra a carga uma vez por segundo e diz quanto do tempo cada componente limitou o jogo."),
            Ui.Label("Duração"), Ui.Chips("bndur", new[] { "1 min", "3 min", "5 min" }, _durIdx, i => _durIdx = i),
            Ui.HStack(10, _sessionBtn), _sessionStatus, _result), "Análise de sessão"));
    }

    public override void OnShown()
    {
        S.Tick -= Tick; S.Tick += Tick; S.Loaded -= Init; S.Loaded += Init;
        Init(); Tick();
    }

    public override void OnHidden() { S.Tick -= Tick; S.Loaded -= Init; }

    private void Init()
    {
        string cpu = S.Cpu?.Model ?? "", gpu = S.MainGpu?.Name ?? "";
        _cpuName.Text = "CPU · " + (cpu.Length > 0 ? GameCatalog.ShortCpuName(cpu) : "—");
        _gpuName.Text = "GPU · " + (gpu.Length > 0 ? GameCatalog.ShortGpuName(gpu) : "—");
        double gpuIdx = GameCatalog.GpuPerf(gpu) ?? 50, cpuIdx = GameCatalog.CpuPerf(cpu) ?? 80;
        var (ratio, text) = BottleneckBalance.Compute(gpuIdx, cpuIdx);
        _bar.Ratio = ratio; _bar.InvalidateVisual();
        _balance.Text = text;
        if (_library.Count == 0) Task.Run(() => GameLibrary.Scan(S.Settings.CustomGames.ToList())).ContinueWith(t => { if (t.IsCompletedSuccessfully) _library = t.Result; });
    }

    private static Control Meter(string label, double? pct, string? text = null)
    {
        var head = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        var l = new TextBlock { Text = label, FontSize = 12.5 };
        var v = Ui.Muted(text ?? (pct == null ? "—" : $"{pct:0} %")); v.FontSize = 12.5;
        Grid.SetColumn(v, 1);
        head.Children.Add(l); head.Children.Add(v);
        return Ui.VStack(4, head, Ui.Bar(pct ?? 0));
    }

    private void Tick()
    {
        var s = LinuxBottleneck.Sample(S.Hub, S.MainGpu);
        var lim = s.Classify();

        if (DateTime.Now - _libAt > TimeSpan.FromSeconds(3)) { _libAt = DateTime.Now; RefreshRunningGame(); }
        _liveTitle.Text = LinuxBottleneck.Title(lim);
        _liveAdvice.Text = LinuxBottleneck.Advice(lim);
        _liveGame.Text = _running != null ? $"Jogo detectado: {_running.Name} ({_running.Store})" : "Nenhum jogo da biblioteca em execução. Abra o jogo para a leitura fazer sentido.";
        bool alert = lim is Limiter.Cpu or Limiter.Vram or Limiter.Ram or Limiter.Thermal;
        _live[!Border.BackgroundProperty] = new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension(alert ? "Fg" : "Card");
        _live[!Border.BorderBrushProperty] = new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("Line");
        foreach (var t in new[] { _liveTitle, _liveGame, _liveAdvice })
            t[!TextBlock.ForegroundProperty] = new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension(alert ? "Ink" : t == _liveTitle ? "Fg" : "Muted");

        _meters.Children.Clear();
        _meters.Children.Add(Meter("Carga da GPU", s.GpuLoad));
        _meters.Children.Add(Meter("Carga total da CPU", s.CpuTotal));
        _meters.Children.Add(Meter(s.BusiestCore != null ? $"Thread mais ocupada ({s.BusiestCore})" : "Thread mais ocupada", s.CpuMaxCore));
        _meters.Children.Add(Meter("VRAM em uso", s.VramPct));
        _meters.Children.Add(Meter("RAM em uso", s.RamPct));
        _meters.Children.Add(Meter("Temperaturas (GPU / CPU)", Math.Max(s.GpuTemp ?? 0, s.CpuTemp ?? 0),
                                   $"{(s.GpuTemp is float g ? $"{g:0} °C" : "—")} / {(s.CpuTemp is float c ? $"{c:0} °C" : "—")}"));

        if (_session != null)
        {
            _session.Add(s);
            var el = DateTime.Now - _session.Start;
            _sessionStatus.Text = $"Coletando… {el:m\\:ss} de {_session.Duration:m\\:ss} · {_session.Count} amostras · " + (_running != null ? $"jogo detectado: {_running.Name}" : "abra o jogo agora");
            if (_session.Done) Finish();
        }
    }

    private GameDetector.Running? _running;
    private bool _runningBusy;

    /// <summary>Ler /proc/*/cmdline espera o processo lido, e um processo em swap demora: nunca na thread da janela.</summary>
    private async void RefreshRunningGame()
    {
        if (_runningBusy) return;
        var library = _library;
        if (library.Count == 0) { _running = null; return; }
        _runningBusy = true;
        try { _running = await Task.Run(() => GameDetector.Any(library)); }
        catch (Exception ex) { CrashLog.Write("Gargalo", ex); }
        finally { _runningBusy = false; }
    }

    private void Toggle()
    {
        if (_session != null)
        {
            _session = null;
            _sessionBtn.Content = "INICIAR ANÁLISE"; _sessionBtn.Classes.Remove("outline"); _sessionBtn.Classes.Add("primary");
            _sessionStatus.Text = "Análise cancelada.";
            return;
        }
        _session = new BottleneckSession { Duration = TimeSpan.FromMinutes(new[] { 1, 3, 5 }[_durIdx]) };
        _result.Children.Clear();
        _sessionBtn.Content = "CANCELAR"; _sessionBtn.Classes.Remove("primary"); _sessionBtn.Classes.Add("outline");
        _sessionStatus.Text = "Coletando… abra o jogo agora.";
    }

    private void Finish()
    {
        var sess = _session!;
        _session = null;
        _sessionBtn.Content = "INICIAR ANÁLISE"; _sessionBtn.Classes.Remove("outline"); _sessionBtn.Classes.Add("primary");
        _sessionStatus.Text = $"Análise concluída às {DateTime.Now:HH:mm} ({sess.Count} amostras).";

        var main = sess.Main ?? Limiter.Capped;
        var parts = new List<string>();
        if (sess.Avg(x => x.GpuLoad) is double g) parts.Add($"GPU média {g:0} %");
        if (sess.Avg(x => x.CpuTotal) is double c) parts.Add($"CPU média {c:0} %");
        if (sess.Avg(x => x.CpuMaxCore) is double k) parts.Add($"thread mais ocupada {k:0} %");
        if (sess.Max(x => x.VramPct) is double v) parts.Add($"VRAM máx. {v:0} %");
        if (sess.Max(x => x.GpuTemp) is double gt) parts.Add($"GPU máx. {gt:0} °C");
        _result.Children.Clear();
        _result.Children.Add(new TextBlock { Text = "Resultado: " + LinuxBottleneck.Title(main), FontSize = 18, FontWeight = FontWeight.SemiBold });
        _result.Children.Add(Ui.Muted(string.Join(" · ", parts)));
        foreach (var d in sess.Distribution()) _result.Children.Add(Meter($"{LinuxBottleneck.Title(d.Limiter)} — % do tempo", d.Pct, $"{d.Pct:0} %"));
        _result.Children.Add(Ui.Text(LinuxBottleneck.Advice(main)).Cls("sub"));
    }
}
