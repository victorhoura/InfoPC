using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace InfoPC.Linux;

/// <summary>Stress test de CPU e RAM (e GPU, com vkmark/glmark2), que para sozinho no limite de temperatura, ao fim do tempo ou se a RAM errar.</summary>
public sealed class StressPage : PageBase
{
    public override string Title => "Stress test";
    public override string Subtitle => "Carga máxima para achar instabilidade e problemas de refrigeração. Use com boa ventilação e com o limite de temperatura ligado.";

    private readonly CheckBox _cpu = new() { Content = "CPU (todas as threads)", IsChecked = true };
    private readonly CheckBox _ram = new() { Content = "RAM (grava e verifica padrões)", IsChecked = false };
    private readonly CheckBox _gpu = new() { Content = "GPU (vkmark / glmark2)", IsChecked = false };
    private int _durIdx = 1, _cpuLimitIdx = 3, _gpuLimitIdx = 3, _ramIdx = 1;
    private static readonly int[] Minutes = { 5, 15, 30, 60, 0 };
    private static readonly float[] CpuLimits = { 80, 85, 90, 95 };
    private static readonly float[] GpuLimits = { 75, 80, 85, 88 };
    private static readonly double[] RamFractions = { 0.4, 0.6, 0.8 };

    private readonly Button _btn;
    private readonly TextBlock _status = Ui.Muted(""), _cpuNow = new(), _cpuMax = new(), _load = new(), _power = new(), _gpuNow = new(), _gpuMax = new(), _mem = new(), _time = new();
    private readonly LineChart _chart = new() { Height = 200 };
    private readonly StackPanel _log = new() { Spacing = 3 };
    private readonly StressTester _stress = new();
    private readonly GpuStress _gpuStress = new();
    private bool _running;
    private DateTime _start;
    private TimeSpan _duration;
    private float _maxCpu, _maxGpu;
    private float _cpuLimit, _gpuLimit;
    private string? _failure;
    private string? _memNotice;
    private readonly List<float?> _cpuSeries = new(), _gpuSeries = new(), _loadSeries = new();

    public StressPage()
    {
        _btn = Ui.Primary("INICIAR", Toggle);
        foreach (var t in new[] { _cpuNow, _cpuMax, _load, _power, _gpuNow, _gpuMax, _mem, _time }) { t.Classes.Add("big"); t.Text = "—"; t.FontSize = 18; }

        var opts = Ui.VStack(14,
            Ui.Label("O que estressar"), Ui.VStack(6, _cpu, _ram, _gpu),
            Ui.Label("Duração"), Ui.Chips("stressdur", new[] { "5 min", "15 min", "30 min", "1 hora", "Até parar" }, _durIdx, i => _durIdx = i),
            Ui.Label("Parar se a CPU passar de"), Ui.Chips("stresscpu", new[] { "80 °C", "85 °C", "90 °C", "95 °C" }, _cpuLimitIdx, i => _cpuLimitIdx = i),
            Ui.Label("Parar se a GPU passar de"), Ui.Chips("stressgpu", new[] { "75 °C", "80 °C", "85 °C", "88 °C" }, _gpuLimitIdx, i => _gpuLimitIdx = i),
            Ui.Label("Memória a usar no teste de RAM"), Ui.Chips("stressram", new[] { "40%", "60%", "80%" }, _ramIdx, i => _ramIdx = i),
            Ui.Note("A memória usada é uma fração da RAM disponível. O limite de temperatura protege o hardware: não o suba sem necessidade."),
            Ui.HStack(10, _btn), _status);
        var optCard = Ui.Card(opts, "Opções");
        optCard.VerticalAlignment = VerticalAlignment.Top;

        var tiles = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*,*"), RowDefinitions = new RowDefinitions("Auto,Auto"), RowSpacing = 14 };
        var items = new[]
        {
            Ui.Metric("CPU agora", _cpuNow), Ui.Metric("CPU máxima", _cpuMax), Ui.Metric("Carga da CPU", _load), Ui.Metric("Consumo da CPU", _power),
            Ui.Metric("GPU agora", _gpuNow), Ui.Metric("GPU máxima", _gpuMax), Ui.Metric("RAM (passadas / erros)", _mem), Ui.Metric("Tempo", _time),
        };
        for (int i = 0; i < items.Length; i++) { Grid.SetColumn(items[i], i % 4); Grid.SetRow(items[i], i / 4); tiles.Children.Add(items[i]); }

        _chart.Unit = " °C"; _chart.FixedMin = 20; _chart.MinSpan = 60; _chart.Span = 300;
        var live = Ui.VStack(14, Ui.Card(tiles, "Ao vivo"), Ui.Card(_chart, "Temperatura (últimos 5 min)"), Ui.Card(_log, "Registro"));
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("340,*"), ColumnSpacing = 16 };
        Grid.SetColumn(live, 1);
        grid.Children.Add(optCard); grid.Children.Add(live);
        Body.Children.Add(grid);
        Log("Pronto. Escolha o que testar e clique em INICIAR.");
    }

    // o acompanhamento (limite de temperatura, erros de RAM, tempo) fica ligado do INICIAR ao fim do teste, mesmo com outra página aberta
    public override void OnShown() { S.Loaded -= Refresh; S.Loaded += Refresh; Refresh(); }
    public override void OnHidden() { S.Loaded -= Refresh; }

    public override void OnClosed()
    {
        Finish("programa fechado");
        base.OnClosed();
    }

    private void Refresh()
    {
        _gpu.IsEnabled = GpuBench.FindTool() != null;
        ToolTip.SetTip(_gpu, _gpu.IsEnabled ? "Usa o vkmark ou o glmark2 em laço." : "Instale o vkmark ou o glmark2 (página Instalar apps).");
    }

    private void Log(string text)
    {
        _log.Children.Insert(0, Ui.HStack(12, Ui.Muted(DateTime.Now.ToString("HH:mm:ss")).Cls("mono"), new TextBlock { Text = text, FontSize = 12.5, TextWrapping = Avalonia.Media.TextWrapping.Wrap }));
        while (_log.Children.Count > 40) _log.Children.RemoveAt(_log.Children.Count - 1);
    }

    private void Toggle()
    {
        if (_running) { Finish("parado por você"); return; }
        bool cpu = _cpu.IsChecked == true, ram = _ram.IsChecked == true, gpu = _gpu.IsChecked == true;
        if (!cpu && !ram && !gpu) { _status.Text = "Marque pelo menos um alvo."; return; }
        if (!Busy.TryBegin("stress test", out var msg)) { _status.Text = msg; return; }
        _cpuLimit = CpuLimits[_cpuLimitIdx]; _gpuLimit = GpuLimits[_gpuLimitIdx];
        _duration = Minutes[_durIdx] == 0 ? TimeSpan.Zero : TimeSpan.FromMinutes(Minutes[_durIdx]);
        _maxCpu = _maxGpu = 0; _failure = null;
        _cpuSeries.Clear(); _gpuSeries.Clear(); _loadSeries.Clear();
        if (gpu && !_gpuStress.Start(GpuBench.FindTool()!)) { Log("A GPU não iniciou: " + _gpuStress.Error); gpu = false; }
        if (cpu || ram) _stress.Start(cpu, ram, RamFractions[_ramIdx]);
        _start = DateTime.Now;
        _running = true;
        S.Tick -= Sample; S.Tick += Sample;
        _btn.Content = "PARAR"; _btn.Classes.Remove("primary"); _btn.Classes.Add("outline");
        _status.Text = "";
        Log($"Iniciado: {(cpu ? "CPU " : "")}{(ram ? $"RAM ({_stress.MemoryAllocatedBytes / 1048576:N0} MB) " : "")}{(gpu ? "GPU " : "")}· limite {_cpuLimit:0} °C" + (_duration > TimeSpan.Zero ? $" · {_duration.TotalMinutes:0} min" : " · até parar"));
        if (S.Hub.CpuTemperature == null) Log("Aviso: sem leitura de temperatura da CPU; o limite de segurança não pode agir. " + S.Hub.DescribeCpuTempProblem());
    }

    private void Sample()
    {
        if (!_running) return;
        float? cpuT = S.Hub.CpuTemperature, gpuT = S.GpuValue("temp");
        if (cpuT is float c) _maxCpu = Math.Max(_maxCpu, c);
        if (gpuT is float g) _maxGpu = Math.Max(_maxGpu, g);
        _cpuSeries.Add(cpuT); _gpuSeries.Add(gpuT); _loadSeries.Add(S.Hub.CpuLoad);
        var elapsed = DateTime.Now - _start;

        _cpuNow.Text = Ui.Num0(cpuT, " °C"); _cpuMax.Text = Ui.Num0(_maxCpu > 0 ? _maxCpu : null, " °C");
        _load.Text = Ui.Num0(S.Hub.CpuLoad, " %"); _power.Text = Ui.Num0(S.Hub.CpuPower, " W");
        _gpuNow.Text = Ui.Num0(gpuT, " °C"); _gpuMax.Text = Ui.Num0(_maxGpu > 0 ? _maxGpu : null, " °C");
        _mem.Text = $"{_stress.MemoryPasses} / {_stress.MemoryErrors}";
        if (_stress.MemoryNotice is { } notice && notice != _memNotice) { _memNotice = notice; Log(notice); }
        _time.Text = _duration > TimeSpan.Zero ? $"{elapsed:mm\\:ss} / {_duration:mm\\:ss}" : $"{elapsed:mm\\:ss}";

        int span = Math.Min(300, _cpuSeries.Count);
        _chart.Series.Clear();
        _chart.Series.Add(new ChartSeries { Name = "CPU", Values = _cpuSeries.TakeLast(span).ToArray(), BrushKey = "Fg", Fill = true });
        if (_gpuSeries.Any(v => v != null)) _chart.Series.Add(new ChartSeries { Name = "GPU", Values = _gpuSeries.TakeLast(span).ToArray(), BrushKey = "Muted", Dashed = true });
        _chart.Refresh();

        if (_cpu.IsChecked == true && cpuT >= _cpuLimit) { _failure = $"a CPU chegou a {cpuT:0} °C (limite {_cpuLimit:0} °C)"; Finish(_failure); return; }
        if (_gpuStress.Running && gpuT >= _gpuLimit) { _failure = $"a GPU chegou a {gpuT:0} °C (limite {_gpuLimit:0} °C)"; Finish(_failure); return; }
        if (_stress.MemoryErrors > 0) { _failure = $"{_stress.MemoryErrors:N0} erros de memória (RAM/EXPO instável)"; Finish(_failure); return; }
        // a carga de GPU morreu (sem display, sem driver 3D ou o driver caiu): o teste não segue como se a GPU estivesse sob carga
        if (_gpuStress.Failed) { _failure = $"a carga de GPU parou ({_gpuStress.Error})"; Finish(_failure); return; }
        // só a RAM estava sob teste e ela devolveu toda a memória ao sistema: não há o que seguir medindo
        if (_ram.IsChecked == true && _cpu.IsChecked != true && !_gpuStress.Running && _stress.MemoryNotice != null && !_stress.MemoryWorkerRunning) { _failure = "o sistema ficou sem memória livre para o teste de RAM"; Finish(_failure); return; }
        if (_duration > TimeSpan.Zero && elapsed >= _duration) Finish("tempo concluído");
    }

    private void Finish(string reason)
    {
        if (!_running) return;
        _running = false;
        S.Tick -= Sample;
        var elapsed = DateTime.Now - _start;
        long passes = _stress.MemoryPasses, errs = _stress.MemoryErrors;
        _stress.Stop(); _gpuStress.Stop();
        Busy.End("stress test");
        _btn.Content = "INICIAR"; _btn.Classes.Remove("outline"); _btn.Classes.Add("primary");
        Log("Encerrado: " + reason + ".");
        string summary = $"{(_cpu.IsChecked == true ? "CPU " : "")}{(_ram.IsChecked == true ? "RAM " : "")}{(_gpu.IsChecked == true ? "GPU " : "")}· {elapsed:mm\\:ss} · CPU máx {_maxCpu:0} °C"
                         + (_maxGpu > 0 ? $" · GPU máx {_maxGpu:0} °C" : "") + (_ram.IsChecked == true ? $" · RAM {passes} passadas, {errs} erros" : "") + (_failure != null ? " · INTERROMPIDO: " + _failure : " · sem problemas");
        S.Settings.BenchRecords.Insert(0, new BenchRecord(DateTime.Now, "stress", elapsed.TotalMinutes, summary));
        S.Save();
        _status.Text = _failure != null ? "Interrompido: " + _failure + "." : "Concluído sem problemas.";
    }
}
