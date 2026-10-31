using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace InfoPC.Linux;

/// <summary>Visão geral: componentes ao vivo, gráfico CPU × GPU, memória e disco, última nota do benchmark e checklist de saúde do PC.</summary>
public sealed class DashboardPage : PageBase
{
    public override string Title => "Visão geral";
    public override string Subtitle => "O estado do computador agora, atualizado a cada segundo.";

    private readonly TextBlock _cpuName = Ui.Muted(""), _gpuName = Ui.Muted("");
    private readonly TextBlock _cpuTemp = new(), _cpuLoad = new(), _cpuClock = new(), _cpuPower = new();
    private readonly TextBlock _gpuTemp = new(), _gpuLoad = new(), _gpuClock = new(), _gpuPower = new();
    private readonly LineChart _chart = new() { Height = 220 };
    private readonly TextBlock _legend = Ui.Muted("");
    private readonly StackPanel _bars = new() { Spacing = 12 };
    private readonly StackPanel _health = new() { Spacing = 8 };
    private readonly StackPanel _events = new() { Spacing = 6 };
    private readonly TextBlock _score = new(), _scoreSub = Ui.Muted(""), _scoreDelta = Ui.Muted("");
    private int _rangeIdx = 1, _metricIdx;
    private static readonly int[] Ranges = { 60, 300, 900 };
    private DateTime _healthAt = DateTime.MinValue;
    private bool _healthBusy;

    public DashboardPage()
    {
        foreach (var t in new[] { _cpuTemp, _cpuLoad, _cpuClock, _cpuPower, _gpuTemp, _gpuLoad, _gpuClock, _gpuPower, _score }) t.Classes.Add("big");
        _score.FontSize = 40;

        // linha 1: nota + CPU + GPU
        var top = new Grid { ColumnDefinitions = new ColumnDefinitions("1.5*,2*,2*"), ColumnSpacing = 16 };
        var scoreCard = Ui.Card(Ui.VStack(4, _score, _scoreDelta, _scoreSub, Ui.Separator(),
                                          Ui.Primary("RODAR BENCHMARK", () => Go("bench")).WithMargin(0, 6, 0, 0)), "Nota do benchmark");
        var cpuCard = Ui.Card(Ui.VStack(10, _cpuName, MetricsRow(_cpuTemp, _cpuLoad, _cpuClock, _cpuPower)), "Processador");
        var gpuCard = Ui.Card(Ui.VStack(10, _gpuName, MetricsRow(_gpuTemp, _gpuLoad, _gpuClock, _gpuPower)), "Placa de vídeo");
        Grid.SetColumn(scoreCard, 0); Grid.SetColumn(cpuCard, 1); Grid.SetColumn(gpuCard, 2);
        top.Children.Add(scoreCard); top.Children.Add(cpuCard); top.Children.Add(gpuCard);
        Body.Children.Add(top);

        // linha 2: gráfico
        var rangeChips = Ui.Chips("dashrange", new[] { "1 MIN", "5 MIN", "15 MIN" }, _rangeIdx, i => { _rangeIdx = i; DrawChart(); });
        var metricChips = Ui.Chips("dashmetric", new[] { "Temperatura", "Carga", "Consumo" }, _metricIdx, i => { _metricIdx = i; DrawChart(); });
        var chartHead = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        Grid.SetColumn(metricChips, 0); Grid.SetColumn(rangeChips, 1);
        chartHead.Children.Add(metricChips); chartHead.Children.Add(rangeChips);
        Body.Children.Add(Ui.Card(Ui.VStack(10, chartHead, _chart, Legend()), "CPU × GPU"));

        // linha 3: memória e disco | saúde
        var row3 = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 16 };
        var memCard = Ui.Card(_bars, "Memória e disco");
        var healthCard = Ui.Card(_health, "Saúde do PC");
        Grid.SetColumn(memCard, 0); Grid.SetColumn(healthCard, 1);
        row3.Children.Add(memCard); row3.Children.Add(healthCard);
        Body.Children.Add(row3);

        Body.Children.Add(Ui.Card(_events, "Resultados recentes"));
        UpdateScores();
    }

    private static Control Swatch(string brushKey, bool dashed)
    {
        var line = new Border { Width = 22, Height = 3, CornerRadius = new CornerRadius(2), VerticalAlignment = VerticalAlignment.Center, Opacity = dashed ? 0.7 : 1 };
        line[!Border.BackgroundProperty] = new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension(brushKey);
        return line;
    }

    private Control Legend()
    {
        var l = Ui.HStack(8, Swatch("Fg", false), new TextBlock { Text = "CPU", FontSize = 12 }, Swatch("Muted", true).WithMargin(14, 0, 0, 0), new TextBlock { Text = "GPU", FontSize = 12 }, _legend.WithMargin(14, 0, 0, 0));
        return l;
    }

    private static Control MetricsRow(TextBlock temp, TextBlock load, TextBlock clock, TextBlock power)
    {
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), RowDefinitions = new RowDefinitions("Auto,Auto"), RowSpacing = 12 };
        var items = new[] { Ui.Metric("Temperatura", temp), Ui.Metric("Uso", load), Ui.Metric("Clock", clock), Ui.Metric("Consumo", power) };
        for (int i = 0; i < items.Length; i++) { Grid.SetColumn(items[i], i % 2); Grid.SetRow(items[i], i / 2); g.Children.Add(items[i]); }
        return g;
    }

    private void Go(string key) { if (TopLevel.GetTopLevel(this) is MainWindow w) w.Navigate(key); }

    public override void OnShown()
    {
        S.Tick -= Update; S.Tick += Update;
        S.Loaded -= Update; S.Loaded += Update;
        Update();
        UpdateScores();
    }

    public override void OnHidden() { S.Tick -= Update; S.Loaded -= Update; }

    private void Update()
    {
        var cpu = S.Cpu;
        _cpuName.Text = cpu != null ? GameCatalog.ShortCpuName(cpu.Model) : "Lendo…";
        _cpuTemp.Text = Ui.Num0(S.Hub.CpuTemperature, " °C");
        _cpuLoad.Text = Ui.Num0(S.Hub.CpuLoad, " %");
        _cpuClock.Text = Ui.Num0(S.Hub.Role("cpu.clock"), " MHz");
        _cpuPower.Text = Ui.Num0(S.Hub.CpuPower, " W");

        _gpuName.Text = S.MainGpu != null ? GameCatalog.ShortGpuName(S.MainGpu.Name) : S.Ready ? "Nenhuma GPU detectada" : "Lendo…";
        _gpuTemp.Text = Ui.Num0(S.GpuValue("temp"), " °C");
        _gpuLoad.Text = Ui.Num0(S.GpuValue("load"), " %");
        _gpuClock.Text = Ui.Num0(S.GpuValue("clock"), " MHz");
        _gpuPower.Text = Ui.Num0(S.GpuValue("power"), " W");

        DrawChart();
        UpdateBars();
        if (DateTime.Now - _healthAt > TimeSpan.FromSeconds(60)) UpdateHealth();
    }

    private void DrawChart()
    {
        string[] keys = { "temp", "load", "power" };
        string k = keys[_metricIdx];
        int span = Ranges[_rangeIdx];
        _chart.Span = span;
        _chart.Series.Clear();
        _chart.Series.Add(new ChartSeries { Name = "CPU", Values = S.History.Last("cpu." + k, span), BrushKey = "Fg", Fill = true });
        if (S.MainGpu != null)
            _chart.Series.Add(new ChartSeries { Name = "GPU", Values = S.History.Last($"gpu{S.MainGpu.Index}.{k}", span), BrushKey = "Muted", Dashed = true, Fill = true });
        _chart.Unit = _metricIdx switch { 0 => " °C", 1 => " %", _ => " W" };
        _chart.FixedMin = _metricIdx == 0 ? 20 : 0;
        _chart.MinSpan = _metricIdx == 0 ? 50 : _metricIdx == 2 ? 20 : 0;
        _chart.FixedMax = _metricIdx == 1 ? 100 : null;
        _chart.Refresh();
        _legend.Text = S.Hub.CpuTemperature == null && _metricIdx == 0 ? "sem leitura de temperatura da CPU" : "";
    }

    private void UpdateBars()
    {
        _bars.Children.Clear();
        var mem = MemInfo.Read();
        if (mem != null)
        {
            _bars.Children.Add(BarRow("Memória RAM", $"{Fmt.Bytes(mem.UsedKb * 1024.0)} de {Fmt.Bytes(mem.TotalKb * 1024.0)}", mem.UsedPct));
            if (mem.SwapTotalKb > 0)
                _bars.Children.Add(BarRow("Swap", $"{Fmt.Bytes(mem.SwapUsedKb * 1024.0)} de {Fmt.Bytes(mem.SwapTotalKb * 1024.0)}", 100.0 * mem.SwapUsedKb / mem.SwapTotalKb));
        }
        foreach (var m in S.Disks.SelectMany(d => d.Mounts).Where(m => m.TotalBytes > 0).Take(4))
            _bars.Children.Add(BarRow($"Disco {m.MountPoint}", $"{Fmt.Bytes(m.FreeBytes)} livres de {Fmt.Bytes(m.TotalBytes)}", m.UsedPct));
        if (_bars.Children.Count == 0) _bars.Children.Add(Ui.Muted("Sem dados de memória ou disco."));
    }

    private static Control BarRow(string label, string detail, double pct)
    {
        var head = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        var l = new TextBlock { Text = label, FontSize = 13 };
        var r = Ui.Muted($"{detail} · {pct:0}%");
        r.FontSize = 12;
        Grid.SetColumn(r, 1);
        head.Children.Add(l); head.Children.Add(r);
        return Ui.VStack(5, head, Ui.Bar(pct));
    }

    private void UpdateScores()
    {
        var runs = S.Settings.BenchRuns.Where(r => r.Multi != null).OrderByDescending(r => r.Date).ToList();
        if (runs.Count > 0)
        {
            _score.Text = $"{runs[0].Multi:N0}";
            _scoreSub.Text = $"pts · multi-core · {runs[0].Date:dd/MM HH:mm}";
            _scoreDelta.Text = runs.Count > 1 ? (runs[0].Multi!.Value >= runs[1].Multi!.Value ? $"▲ {runs[0].Multi!.Value - runs[1].Multi!.Value:N0}" : $"▼ {runs[1].Multi!.Value - runs[0].Multi!.Value:N0}") : "";
        }
        else { _score.Text = "—"; _scoreSub.Text = "rode o Benchmark para ter uma nota"; _scoreDelta.Text = ""; }

        _events.Children.Clear();
        var recs = S.Settings.BenchRecords.OrderByDescending(r => r.Date).Take(6).ToList();
        foreach (var r in recs)
            _events.Children.Add(Ui.HStack(14, Ui.Muted($"{r.Date:dd/MM HH:mm}").Cls("mono"), new TextBlock { Text = TestName(r.Test), FontWeight = FontWeight.SemiBold }, Ui.Muted(r.Summary)));
        foreach (var r in runs.Take(Math.Max(0, 6 - recs.Count)))
            _events.Children.Add(Ui.HStack(14, Ui.Muted($"{r.Date:dd/MM HH:mm}").Cls("mono"), new TextBlock { Text = "Benchmark de CPU", FontWeight = FontWeight.SemiBold },
                                           Ui.Muted($"multi {r.Multi:N0}" + (r.Single != null ? $" · single {r.Single:N0}" : ""))));
        if (_events.Children.Count == 0) _events.Children.Add(Ui.Muted("Nada ainda. Os resultados de benchmarks e stress tests aparecem aqui."));
    }

    public static string TestName(string t) => t switch
    {
        "cpu" => "Benchmark de CPU", "sim" => "Simulação de jogo (CPU)", "mem" => "Memória e carregamento", "disk" => "Disco", "gpu" => "GPU", "stress" => "Stress test", "oc" => "Estabilidade", _ => t
    };

    // ---------- checklist de saúde ----------

    private async void UpdateHealth()
    {
        if (_healthBusy) return;
        _healthBusy = true;
        _healthAt = DateTime.Now;
        var items = await Task.Run(BuildHealth);
        _health.Children.Clear();
        foreach (var (ok, title, detail) in items)
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("22,*") };
            var icon = new TextBlock { Text = ok ? "✓" : "!", FontWeight = FontWeight.Bold, Foreground = Ui.Brush(this, ok ? "Good" : "Warn") };
            var text = Ui.VStack(0, new TextBlock { Text = title, FontSize = 13, FontWeight = FontWeight.SemiBold }, Ui.Muted(detail).Cls("sub"));
            Grid.SetColumn(text, 1);
            row.Children.Add(icon); row.Children.Add(text);
            _health.Children.Add(row);
        }
        _healthBusy = false;
    }

    private List<(bool Ok, string Title, string Detail)> BuildHealth()
    {
        var list = new List<(bool, string, string)>();
        bool temp = S.Hub.CpuTemperature != null;
        list.Add((temp, "Sensores da CPU", temp ? "Temperatura disponível." : S.Hub.DescribeCpuTempProblem()));
        if (S.Hub.Rapl?.Blocked == true) list.Add((false, "Consumo da CPU", "O kernel só deixa o administrador ler o consumo (RAPL). Os demais sensores funcionam."));

        var errs = KernelLog.ErrorsSince(7);
        list.Add((errs.Error == null && errs.Total == 0, "Sem erros de hardware",
                  errs.Error != null ? "Não foi possível ler o log do kernel: " + errs.Error + "."
                  : errs.Total == 0 ? "Nenhum erro de CPU, vídeo, disco ou memória no log dos últimos 7 dias." : errs.Summary + " em 7 dias."));

        string profile = BatteryReader.PowerProfile();
        bool saver = profile.Contains("Economia");
        list.Add((!saver, "Perfil de energia", saver ? "Em economia de energia: o PC limita o clock. Troque para Desempenho na página Otimização." : $"Perfil atual: {profile}."));

        bool gm = Shell.Has("gamemoderun");
        list.Add((gm, "GameMode instalado", gm ? "Os jogos podem pedir prioridade ao sistema." : "Falta o GameMode: instale na página Instalar apps."));

        var full = S.Disks.SelectMany(d => d.Mounts).FirstOrDefault(m => m.UsedPct >= 92 && m.TotalBytes > 0);
        if (full != null) list.Add((false, "Disco quase cheio", $"{full.MountPoint} está com {full.UsedPct:0}% de uso."));
        return list;
    }
}
