using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;

namespace InfoPC.Linux;

/// <summary>Benchmarks: render de CPU (path tracer), simulação de jogo, memória e carregamento, disco e GPU (vkmark/glmark2).</summary>
public sealed class BenchPage : PageBase
{
    public override string Title => "Benchmark";
    public override string Subtitle => "Notas repetíveis: a mesma cena e a mesma carga em todo teste, para comparar antes e depois de um ajuste.";

    private static readonly string[] Modes = { "CPU · render", "CPU · simulação de jogo", "Memória e carregamento", "Disco", "GPU" };
    private readonly ContentControl _panel = new();
    private readonly Control[] _panels = new Control[5];
    private DispatcherTimer? _timer;

    // CPU render
    private readonly BenchRenderer _bench = new();
    private WriteableBitmap? _bitmap;
    private readonly Image _image = new() { Stretch = Stretch.Uniform, MaxHeight = 340 };
    private readonly BucketOverlay _buckets = new();
    private readonly TextBlock _cpuStatus = Ui.Muted(""), _cpuMt = new(), _cpuSt = new(), _cpuRatio = Ui.Muted("");
    private readonly ProgressBar _cpuProgress = new() { Minimum = 0, Maximum = 100 };
    private readonly Button _cpuBtn;
    private readonly StackPanel _cpuHistory = new() { Spacing = 8 };
    private readonly StackPanel _cpuLive = new() { Spacing = 4 };
    private int _cpuMode, _cpuDur;
    private CancellationTokenSource? _cpuCts;
    private string _phase = "";
    private DateTime _phaseStart, _runStart;

    // simulação
    private readonly TextBlock _simStatus = Ui.Muted(""), _simScore = new(), _simDetail = Ui.Muted("");
    private readonly ProgressBar _simProgress = new() { Minimum = 0, Maximum = 100 };
    private readonly Button _simBtn;
    private GameSimBench? _sim;
    private CancellationTokenSource? _simCts;
    private int _simDur = 1;

    // memória
    private readonly TextBlock _memStatus = Ui.Muted(""), _memLat = new(), _memRead = new(), _memCopy = new(), _loadMap = new(), _loadDetail = Ui.Muted("");
    private readonly ProgressBar _memProgress = new() { Minimum = 0, Maximum = 100 };
    private readonly Button _memBtn;
    private CancellationTokenSource? _memCts;

    // disco
    private readonly ComboBox _diskTarget = new() { MinWidth = 340 };
    private readonly TextBlock _diskStatus = Ui.Muted(""), _diskNote = Ui.Muted("");
    private readonly ProgressBar _diskProgress = new() { Minimum = 0, Maximum = 100 };
    private readonly Button _diskBtn;
    private readonly StackPanel _diskResult = new() { Spacing = 6 };
    private List<BenchTarget> _targets = new();
    private int _diskSize = 1;
    private CancellationTokenSource? _diskCts;

    // GPU
    private readonly TextBlock _gpuStatus = Ui.Muted(""), _gpuScore = new(), _gpuHint = Ui.Muted("");
    private readonly Button _gpuBtn;
    private CancellationTokenSource? _gpuCts;

    private readonly StackPanel _history = new() { Spacing = 8 };

    public BenchPage()
    {
        _cpuBtn = Ui.Primary("INICIAR", () => RunCpu());
        _simBtn = Ui.Primary("INICIAR", () => RunSim());
        _memBtn = Ui.Primary("INICIAR", () => RunMem());
        _diskBtn = Ui.Primary("INICIAR", () => RunDisk());
        _gpuBtn = Ui.Primary("INICIAR", () => RunGpu());
        _bitmap = new WriteableBitmap(new PixelSize(BenchRenderer.Width, BenchRenderer.Height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
        _image.Source = _bitmap;
        _buckets.Provider = () => _bench.Active.Where(b => b >= 0 && b < _bench.Buckets.Count).Select(b => _bench.Buckets[b]).ToList();

        _panels[0] = BuildCpu(); _panels[1] = BuildSim(); _panels[2] = BuildMem(); _panels[3] = BuildDisk(); _panels[4] = BuildGpu();
        Body.Children.Add(Ui.Chips("benchmode", Modes, 0, i => _panel.Content = _panels[i]));
        _panel.Content = _panels[0];
        Body.Children.Add(_panel);
        Body.Children.Add(Ui.Card(_history, "Resultados recentes"));
        RefreshHistory();
    }

    public override void OnShown()
    {
        S.Tick -= Live; S.Tick += Live;
        S.Loaded -= RefreshTargets; S.Loaded += RefreshTargets;
        Live();
        RefreshHistory();
        RefreshTargets();
    }

    public override void OnHidden() { S.Tick -= Live; S.Loaded -= RefreshTargets; }

    public override void OnClosed()
    {
        _cpuCts?.Cancel(); _simCts?.Cancel(); _memCts?.Cancel(); _diskCts?.Cancel(); _gpuCts?.Cancel();
        _timer?.Stop();
        base.OnClosed();
    }

    // ======================================================== CPU · render

    private Control BuildCpu()
    {
        var mode = Ui.Chips("cpumode", new[] { "Multi-core + single-core", "Só multi-core", "Só single-core" }, 0, i => _cpuMode = i);
        var dur = Ui.Chips("cpudur", new[] { "1 passada", "10 minutos (sustentado)" }, 0, i => _cpuDur = i);
        var scores = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*") };
        var mt = Ui.Metric("Multi-core", _cpuMt); var st = Ui.Metric("Single-core", _cpuSt);
        _cpuMt.Text = _cpuSt.Text = "—";
        Grid.SetColumn(st, 1);
        scores.Children.Add(mt); scores.Children.Add(st);

        var preview = new Grid();
        preview.Children.Add(_image);
        preview.Children.Add(_buckets);

        var left = Ui.VStack(14, Ui.Label("Modo"), mode, Ui.Label("Duração"), dur,
                             Ui.HStack(10, _cpuBtn), _cpuProgress, _cpuStatus, scores, _cpuRatio);
        var right = Ui.VStack(14, Ui.Card(preview), Ui.Card(_cpuLive, "Agora"));
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("360,*"), ColumnSpacing = 16 };
        var lc = Ui.Card(left, "Render de CPU (path tracer)");
        lc.VerticalAlignment = VerticalAlignment.Top;
        Grid.SetColumn(right, 1);
        grid.Children.Add(lc); grid.Children.Add(right);
        return grid;
    }

    private async void RunCpu()
    {
        if (_cpuCts != null) { _cpuCts.Cancel(); return; }
        if (!Busy.TryBegin("benchmark de CPU", out var msg)) { _cpuStatus.Text = msg; return; }
        bool sustained = _cpuDur == 1;
        var minDuration = sustained ? TimeSpan.FromMinutes(10) : TimeSpan.Zero;
        int threads = Environment.ProcessorCount;
        bool doMt = _cpuMode is 0 or 1, doSt = _cpuMode is 0 or 2;
        _cpuCts = new CancellationTokenSource();
        var ct = _cpuCts.Token;
        _cpuBtn.Content = "PARAR"; _cpuBtn.Classes.Remove("primary"); _cpuBtn.Classes.Add("outline");
        _cpuMt.Text = _cpuSt.Text = "—"; _cpuRatio.Text = "";
        _runStart = DateTime.Now;
        _timer ??= new DispatcherTimer(TimeSpan.FromMilliseconds(100), DispatcherPriority.Render, (_, _) => UpdateCpuView());
        _timer.Start();

        double? mt = null, st = null;
        string? failed = null;
        try
        {
            if (doMt)
            {
                _phase = $"Multi-core · {threads} threads"; _phaseStart = DateTime.Now;
                var r = await Task.Run(() => _bench.Run(threads, minDuration, ct), ct);
                if (!ct.IsCancellationRequested) { mt = r.Score; _cpuMt.Text = $"{r.Score:N0}"; }
            }
            if (!ct.IsCancellationRequested && doSt)
            {
                _phase = "Single-core · 1 thread"; _phaseStart = DateTime.Now;
                var r = await Task.Run(() => _bench.Run(1, minDuration, ct), ct);
                if (!ct.IsCancellationRequested) { st = r.Score; _cpuSt.Text = $"{r.Score:N0}"; }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { failed = ex.Message; CrashLog.Write("Benchmark de CPU", ex); }

        _timer.Stop();
        bool cancelled = ct.IsCancellationRequested;
        _cpuCts = null;
        UpdateCpuView(final: true);
        _cpuBtn.Content = "INICIAR"; _cpuBtn.Classes.Remove("outline"); _cpuBtn.Classes.Add("primary");
        Busy.End("benchmark de CPU");
        _buckets.InvalidateVisual();
        if (failed != null) { _cpuStatus.Text = "O teste falhou: " + failed; return; }
        if (cancelled) { _cpuStatus.Text = "Cancelado"; return; }
        _cpuStatus.Text = $"Concluído em {DateTime.Now - _runStart:m\\:ss}";
        if (mt is double m && st is double s) _cpuRatio.Text = $"Multi ÷ single: {m / s:0.0}x com {threads} threads (núcleos com SMT costumam render 8–12x).";

        S.Settings.BenchRuns.Insert(0, new BenchRun(DateTime.Now, mt, st, sustained, GameCatalog.ShortCpuName(S.Cpu?.Model ?? "CPU")));
        if (S.Settings.BenchRuns.Count > 10) S.Settings.BenchRuns.RemoveRange(10, S.Settings.BenchRuns.Count - 10);
        S.Save();
        RefreshHistory();
    }

    private void UpdateCpuView(bool final = false)
    {
        if (_bitmap == null) return;
        using (var fb = _bitmap.Lock())
        {
            if (fb.RowBytes == BenchRenderer.Width * 4) Marshal.Copy(_bench.Pixels, 0, fb.Address, _bench.Pixels.Length);
            else
                for (int y = 0; y < BenchRenderer.Height; y++)
                    Marshal.Copy(_bench.Pixels, y * BenchRenderer.Width, fb.Address + y * fb.RowBytes, BenchRenderer.Width);
        }
        _image.InvalidateVisual();
        _buckets.InvalidateVisual();
        if (_cpuCts != null && !final)
        {
            double frac = _bench.BucketsTotal > 0 ? (double)_bench.BucketsDone / _bench.BucketsTotal : 0;
            _cpuProgress.Value = frac * 100;
            var elapsed = DateTime.Now - _phaseStart;
            string eta = frac >= 0.03 ? $" · faltam ~{TimeSpan.FromSeconds(elapsed.TotalSeconds * (1 - frac) / frac):m\\:ss}" : "";
            _cpuStatus.Text = $"{_phase} · {frac:P0}{eta} · {DateTime.Now - _runStart:mm\\:ss} no total";
        }
        else _cpuProgress.Value = 0;
    }

    // ======================================================== simulação de jogo

    private Control BuildSim()
    {
        _simScore.Text = "—";
        var dur = Ui.Chips("simdur", new[] { "30 s", "60 s" }, _simDur, i => _simDur = i);
        var body = Ui.VStack(14,
            Ui.Note("30 mil unidades em frente de batalha: a thread principal faz grade, lógica e preparo do desenho, e as demais fazem física, colisão e IA. A nota é o FPS da simulação."),
            Ui.Label("Duração"), dur, Ui.HStack(10, _simBtn), _simProgress, _simStatus, Ui.Metric("FPS da simulação", _simScore), _simDetail);
        return Ui.Card(body, "Simulação de jogo (CPU)");
    }

    private async void RunSim()
    {
        if (_simCts != null) { _simCts.Cancel(); return; }
        if (!Busy.TryBegin("simulação de jogo", out var msg)) { _simStatus.Text = msg; return; }
        _simCts = new CancellationTokenSource();
        var ct = _simCts.Token;
        _simBtn.Content = "PARAR"; _simBtn.Classes.Remove("primary"); _simBtn.Classes.Add("outline");
        _sim = new GameSimBench();
        var sim = _sim;
        var tm = new DispatcherTimer(TimeSpan.FromMilliseconds(250), DispatcherPriority.Render, (_, _) =>
        {
            _simProgress.Value = sim.Progress * 100;
            _simStatus.Text = $"Simulando… {sim.CurrentFps:0} FPS agora · {sim.FramesDone:N0} quadros";
        });
        tm.Start();
        GameSimResult? res = null;
        string? failed = null;
        try { res = await Task.Run(() => sim.Run(TimeSpan.FromSeconds(_simDur == 0 ? 30 : 60), ct), ct); }
        catch (OperationCanceledException) { }
        catch (Exception ex) { failed = ex.Message; CrashLog.Write("Simulação de jogo", ex); }
        tm.Stop();
        _simCts = null;
        _simProgress.Value = 0;
        _simBtn.Content = "INICIAR"; _simBtn.Classes.Remove("outline"); _simBtn.Classes.Add("primary");
        Busy.End("simulação de jogo");
        if (failed != null) { _simStatus.Text = "O teste falhou: " + failed; return; }
        if (res == null || ct.IsCancellationRequested) { _simStatus.Text = "Cancelado"; return; }
        _simScore.Text = $"{res.Score:N0}";
        _simDetail.Text = $"1% low {res.Low1Fps:0} FPS · thread principal {res.MainThreadMs:0.00} ms · trabalho paralelo {res.ParallelMs:0.00} ms · {res.Frames:N0} quadros em {res.Seconds:0} s";
        _simStatus.Text = "Concluído";
        AddRecord("sim", res.Score, $"{res.Score:N0} FPS · 1% low {res.Low1Fps:0}");
    }

    // ======================================================== memória e carregamento

    private Control BuildMem()
    {
        _memLat.Text = _memRead.Text = _memCopy.Text = _loadMap.Text = "—";
        var ram = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*") };
        var a = Ui.Metric("Latência", _memLat); var b = Ui.Metric("Leitura", _memRead); var c = Ui.Metric("Cópia", _memCopy);
        Grid.SetColumn(b, 1); Grid.SetColumn(c, 2);
        ram.Children.Add(a); ram.Children.Add(b); ram.Children.Add(c);
        var body = Ui.VStack(14,
            Ui.Note("Latência e banda da RAM (todas as threads) e o tempo de \"carregar um mapa\": um pacote de 512 MB mais mil arquivos pequenos, lidos do disco sem o cache do sistema (O_DIRECT). Os arquivos de teste ficam na pasta de cache da sua conta e são apagados no fim."),
            Ui.HStack(10, _memBtn), _memProgress, _memStatus, ram, Ui.Separator(), Ui.Metric("Carregar um mapa", _loadMap), _loadDetail);
        return Ui.Card(body, "Memória e carregamento");
    }

    private async void RunMem()
    {
        if (_memCts != null) { _memCts.Cancel(); return; }
        if (!Busy.TryBegin("teste de memória", out var msg)) { _memStatus.Text = msg; return; }
        _memCts = new CancellationTokenSource();
        var ct = _memCts.Token;
        _memBtn.Content = "PARAR"; _memBtn.Classes.Remove("primary"); _memBtn.Classes.Add("outline");
        _memProgress.IsIndeterminate = true;
        _memLat.Text = _memRead.Text = _memCopy.Text = _loadMap.Text = "—"; _loadDetail.Text = "";
        try
        {
            _memStatus.Text = "Medindo a latência da RAM…";
            double lat = await Task.Run(() => MemoryBench.MeasureLatencyNs(ct), ct);
            _memLat.Text = $"{lat:0} ns";
            _memStatus.Text = "Medindo a banda da RAM…";
            var (rd, cp) = await Task.Run(() => MemoryBench.MeasureBandwidth(ct), ct);
            _memRead.Text = $"{rd:0.0} GB/s"; _memCopy.Text = $"{cp:0.0} GB/s";
            ct.ThrowIfCancellationRequested();

            var target = await Task.Run(BenchTargets.Default, ct);
            LoadResult? load = null;
            if (target == null) _loadDetail.Text = "Nenhuma unidade com 1,5 GB livres e pasta gravável: o teste de carregamento foi pulado.";
            else
            {
                _memProgress.IsIndeterminate = false;
                load = await Task.Run(() => MemoryBench.MeasureLoading(target, (t, f) => Dispatcher.UIThread.Post(() => { _memStatus.Text = t; _memProgress.Value = f * 100; }), ct), ct);
                _loadMap.Text = $"{load.MapLoadSeconds:0.0} s";
                _loadDetail.Text = $"Pacote {load.SeqReadMBs:N0} MB/s · assets pequenos {load.SmallFilesMBs:N0} MB/s ({load.FilesPerSec:N0} arquivos/s) · em {load.Drive}"
                                   + (load.Direct ? "" : " · aviso: este sistema de arquivos não aceita leitura direta, os números podem estar inflados pelo cache");
            }
            _memStatus.Text = "Concluído";
            AddRecord("mem", 1000 / Math.Max(lat, 1), $"{lat:0} ns · {rd:0.0} GB/s leitura" + (load != null ? $" · mapa {load.MapLoadSeconds:0.0} s" : ""));
        }
        catch (OperationCanceledException) { _memStatus.Text = "Cancelado"; }
        catch (Exception ex) { _memStatus.Text = "O teste falhou: " + ex.Message; }
        finally
        {
            _memCts = null; _memProgress.IsIndeterminate = false; _memProgress.Value = 0;
            _memBtn.Content = "INICIAR"; _memBtn.Classes.Remove("outline"); _memBtn.Classes.Add("primary");
            Busy.End("teste de memória");
        }
    }

    // ======================================================== disco

    private Control BuildDisk()
    {
        _diskTarget.SelectionChanged += (_, _) => { };
        var size = Ui.Chips("disksize", new[] { "512 MB", "1 GB", "4 GB" }, _diskSize, i => _diskSize = i);
        var body = Ui.VStack(14,
            Ui.Note("Leitura e escrita sequenciais (1 MB) e acesso aleatório de 4 KB, no estilo do CrystalDiskMark, com O_DIRECT para medir o disco e não o cache. O arquivo de teste é criado numa pasta gravável da unidade e apagado no fim."),
            Ui.Label("Unidade"), _diskTarget, Ui.Label("Tamanho do arquivo de teste"), size,
            Ui.HStack(10, _diskBtn), _diskProgress, _diskStatus, _diskResult, _diskNote);
        return Ui.Card(body, "Disco");
    }

    private async void RefreshTargets()
    {
        _targets = await Task.Run(BenchTargets.List);
        int sel = Math.Max(0, _diskTarget.SelectedIndex);
        _diskTarget.ItemsSource = _targets.Select(t => t.Label).ToList();
        if (_targets.Count > 0) _diskTarget.SelectedIndex = Math.Min(sel, _targets.Count - 1);
        _diskNote.Text = _targets.Count == 0 ? "Nenhuma unidade com 1,5 GB livres e uma pasta onde o seu usuário possa gravar." : "";
    }

    private async void RunDisk()
    {
        if (_diskCts != null) { _diskCts.Cancel(); return; }
        if (_diskTarget.SelectedIndex < 0 || _diskTarget.SelectedIndex >= _targets.Count) { _diskStatus.Text = "Escolha uma unidade."; return; }
        if (!Busy.TryBegin("benchmark de disco", out var msg)) { _diskStatus.Text = msg; return; }
        var target = _targets[_diskTarget.SelectedIndex];
        long mb = new[] { 512L, 1024L, 4096L }[_diskSize];
        _diskCts = new CancellationTokenSource();
        var ct = _diskCts.Token;
        _diskBtn.Content = "PARAR"; _diskBtn.Classes.Remove("primary"); _diskBtn.Classes.Add("outline");
        _diskResult.Children.Clear(); _diskNote.Text = "";
        try
        {
            var r = await Task.Run(() => DiskBench.Run(target, mb, (t, f) => Dispatcher.UIThread.Post(() => { _diskStatus.Text = t; _diskProgress.Value = f * 100; }), ct), ct);
            _diskResult.Children.Add(Ui.KeyValues(new[]
            {
                new KeyValuePair<string, string>("Leitura sequencial (1 MB)", $"{r.SeqReadMBs:N0} MB/s"),
                new KeyValuePair<string, string>("Escrita sequencial (1 MB)", $"{r.SeqWriteMBs:N0} MB/s"),
                new KeyValuePair<string, string>("Leitura 4 KB (1 por vez)", $"{r.ReadIopsQ1:N0} IOPS · {r.ReadLatencyUs:0} µs"),
                new KeyValuePair<string, string>("Escrita 4 KB (1 por vez)", $"{r.WriteIopsQ1:N0} IOPS · {r.WriteLatencyUs:0} µs"),
                new KeyValuePair<string, string>("Leitura 4 KB (8 em paralelo)", $"{r.ReadIopsQ8:N0} IOPS"),
            }, 260));
            if (!r.Direct) _diskNote.Text = "Aviso: este sistema de arquivos não aceita leitura direta (O_DIRECT). O programa limpou o cache entre as fases, mas os números podem estar um pouco inflados.";
            _diskStatus.Text = "Concluído";
            AddRecord("disk", r.Score, $"{r.Drive} · {r.SeqReadMBs:N0}/{r.SeqWriteMBs:N0} MB/s · 4K {r.ReadIopsQ1:N0}/{r.WriteIopsQ1:N0} IOPS");
        }
        catch (OperationCanceledException) { _diskStatus.Text = "Cancelado"; }
        catch (Exception ex) { _diskStatus.Text = "O teste falhou: " + ex.Message; }
        finally
        {
            _diskCts = null; _diskProgress.Value = 0;
            _diskBtn.Content = "INICIAR"; _diskBtn.Classes.Remove("outline"); _diskBtn.Classes.Add("primary");
            Busy.End("benchmark de disco");
        }
    }

    // ======================================================== GPU

    private Control BuildGpu()
    {
        _gpuScore.Text = "—";
        string? tool = GpuBench.FindTool();
        _gpuHint.Text = tool != null ? $"Ferramenta encontrada: {tool}. O teste abre uma janela por alguns minutos; não use o computador enquanto isso."
                                       : "Não há benchmark de GPU instalado. O Linux não tem Direct3D, então o InfoPC usa o vkmark (Vulkan) ou o glmark2 (OpenGL). Instale um deles na página Instalar apps.";
        var body = Ui.VStack(14,
            Ui.Note("A nota do vkmark/glmark2 não é comparável com a do Windows nem entre ferramentas: serve para comparar a mesma máquina antes e depois de um ajuste de driver ou overclock."),
            _gpuHint,
            Ui.HStack(10, _gpuBtn, Ui.Outline("ABRIR INSTALAR APPS", () => { if (TopLevel.GetTopLevel(this) is MainWindow w) w.Navigate("apps"); })),
            _gpuStatus, Ui.Metric("Nota da GPU", _gpuScore));
        return Ui.Card(body, "GPU");
    }

    private async void RunGpu()
    {
        if (_gpuCts != null) { _gpuCts.Cancel(); return; }
        string? tool = GpuBench.FindTool();
        if (tool == null) { _gpuStatus.Text = "Instale o vkmark ou o glmark2 primeiro."; return; }
        if (!Busy.TryBegin("benchmark de GPU", out var msg)) { _gpuStatus.Text = msg; return; }
        _gpuCts = new CancellationTokenSource();
        _gpuBtn.Content = "PARAR"; _gpuBtn.Classes.Remove("primary"); _gpuBtn.Classes.Add("outline");
        _gpuStatus.Text = $"Rodando o {tool}…";
        S.Paused = true; // consultar o driver durante o teste causa engasgos e piora o resultado
        try
        {
            var r = await GpuBench.RunAsync(tool, _gpuCts.Token);
            if (r.Score is double sc)
            {
                _gpuScore.Text = $"{sc:N0}";
                _gpuStatus.Text = "Concluído";
                AddRecord("gpu", sc, $"{tool} · {sc:N0} pontos · {S.MainGpu?.Name ?? "GPU"}");
            }
            else _gpuStatus.Text = r.Error == "cancelado" ? "Cancelado" : "O teste falhou: " + r.Error;
        }
        catch (Exception ex) { _gpuStatus.Text = "O teste falhou: " + ex.Message; CrashLog.Write("Benchmark de GPU", ex); }
        finally
        {
            S.Paused = false;
            _gpuCts = null;
            _gpuBtn.Content = "INICIAR"; _gpuBtn.Classes.Remove("outline"); _gpuBtn.Classes.Add("primary");
            Busy.End("benchmark de GPU");
        }
    }

    // ======================================================== histórico e leituras

    private void AddRecord(string test, double score, string summary)
    {
        S.Settings.BenchRecords.Insert(0, new BenchRecord(DateTime.Now, test, score, summary));
        if (S.Settings.BenchRecords.Count > 30) S.Settings.BenchRecords.RemoveRange(30, S.Settings.BenchRecords.Count - 30);
        S.Save();
        RefreshHistory();
    }

    private void RefreshHistory()
    {
        _history.Children.Clear();
        var runs = S.Settings.BenchRuns;
        double bestMt = runs.Max(r => r.Multi) ?? 1, bestSt = runs.Max(r => r.Single) ?? 1;
        foreach (var r in runs)
        {
            double frac = r.Multi is double mm ? mm / bestMt : r.Single is double ss ? ss / bestSt : 0;
            _history.Children.Add(HistoryRow($"{r.Date:dd/MM HH:mm}{(r.Sustained ? " · 10 min" : "")}", "Render de CPU",
                                             $"{(r.Multi is double m ? $"{m:N0}" : "—")} / {(r.Single is double s ? $"{s:N0}" : "—")}", frac));
        }
        foreach (var g in S.Settings.BenchRecords.GroupBy(r => r.Test))
        {
            double best = g.Max(r => r.Score);
            foreach (var r in g.Take(4))
                _history.Children.Add(HistoryRow($"{r.Date:dd/MM HH:mm}", DashboardPage.TestName(r.Test), r.Summary, best > 0 ? r.Score / best : 0));
        }
        if (_history.Children.Count == 0) _history.Children.Add(Ui.Muted("Nenhum teste ainda. Rode um acima."));
    }

    private static Control HistoryRow(string when, string what, string value, double frac)
    {
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("110,190,*,160"), ColumnSpacing = 10 };
        var a = Ui.Muted(when).Cls("mono"); a.FontSize = 12;
        var b = new TextBlock { Text = what, FontSize = 13, FontWeight = FontWeight.SemiBold };
        var c = Ui.Muted(value); c.FontSize = 12.5;
        var bar = Ui.Bar(frac * 100); bar.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(b, 1); Grid.SetColumn(c, 2); Grid.SetColumn(bar, 3);
        g.Children.Add(a); g.Children.Add(b); g.Children.Add(c); g.Children.Add(bar);
        return g;
    }

    private void Live()
    {
        _cpuLive.Children.Clear();
        float? clock = S.Hub.Role("cpu.clock");
        foreach (var kv in new[]
        {
            ("CPU", GameCatalog.ShortCpuName(S.Cpu?.Model ?? "—")), ("Threads", Environment.ProcessorCount.ToString()),
            ("Temperatura", Ui.Num1(S.Hub.CpuTemperature, " °C")), ("Clock médio", Ui.Num0(clock, " MHz")),
            ("Consumo", Ui.Num1(S.Hub.CpuPower, " W")), ("Carga", Ui.Num0(S.Hub.CpuLoad, " %")),
        })
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("140,*") };
            var k = Ui.Muted(kv.Item1); k.FontSize = 12.5;
            var v = new TextBlock { Text = kv.Item2, FontSize = 12.5 };
            Grid.SetColumn(v, 1);
            row.Children.Add(k); row.Children.Add(v);
            _cpuLive.Children.Add(row);
        }
    }
}
