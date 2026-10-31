using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace InfoPC.Linux;

/// <summary>Overclock: limite de energia e trava de clock da GPU NVIDIA, verificador de estabilidade e guia de BIOS.</summary>
public sealed class OcPage : PageBase
{
    public override string Title => "Overclock";
    public override string Subtitle => "Ajustes da GPU NVIDIA (valem até reiniciar), verificador de estabilidade e guia para overclock na BIOS.";

    private readonly StackPanel _gpuBox = new() { Spacing = 12 };
    private readonly Slider _power = new() { Minimum = 100, Maximum = 400, Width = 360 };
    private readonly Slider _clock = new() { Minimum = 500, Maximum = 3500, Width = 360 };
    private readonly TextBlock _powerText = Ui.Muted(""), _clockText = Ui.Muted("");
    private readonly StatusText _gpuStatus = new();

    private int _target, _dur = 1;
    private readonly TextBox _label = new() { Watermark = "descrição do ajuste (ex.: Curve Optimizer -15)", MaxWidth = 420, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly Button _checkBtn;
    private readonly StatusText _checkStatus = new();
    private readonly Border _verdict = new() { IsVisible = false };
    private readonly TextBlock _verdictText = new() { FontSize = 22, FontWeight = FontWeight.Bold }, _verdictWhy = new() { TextWrapping = TextWrapping.Wrap };
    private readonly StackPanel _history = new() { Spacing = 6 };
    private readonly StackPanel _errors = new() { Spacing = 6 };
    private CancellationTokenSource? _cts;
    private readonly StackPanel _guide = new() { Spacing = 10 };

    public OcPage()
    {
        _checkBtn = Ui.Primary("INICIAR VERIFICAÇÃO", RunCheck);
        _power.ValueChanged += (_, _) => _powerText.Text = $"{_power.Value:0} W";
        _clock.ValueChanged += (_, _) => _clockText.Text = $"{_clock.Value:0} MHz";

        Body.Children.Add(Ui.Card(_gpuBox, "Placa de vídeo NVIDIA"));

        _verdict.Child = Ui.VStack(6, _verdictText, _verdictWhy);
        _verdict.Padding = new Thickness(20); _verdict.CornerRadius = new CornerRadius(16);
        var check = Ui.VStack(14,
            Ui.Note("Mede a nota da CPU, aplica carga por alguns minutos e confere o log do kernel. Para sozinho se a temperatura passar de 95 °C (CPU) ou 88 °C (GPU) ou se a RAM errar."),
            Ui.Label("O que verificar"), Ui.Chips("octarget", new[] { "CPU + RAM", "GPU", "CPU + RAM + GPU" }, _target, i => _target = i),
            Ui.Label("Duração"), Ui.Chips("ocdur", new[] { "5 min", "15 min", "30 min" }, _dur, i => _dur = i),
            _label, Ui.HStack(10, _checkBtn), _checkStatus, _verdict, Ui.Separator(), Ui.Label("Verificações anteriores"), _history);
        Body.Children.Add(Ui.Card(check, "Verificador de estabilidade"));
        Body.Children.Add(Ui.Card(_errors, "Erros de hardware recentes (7 dias)"));
        Body.Children.Add(_guide);
        RefreshHistory();
    }

    public override void OnShown() { S.Loaded -= Refresh; S.Loaded += Refresh; Refresh(); }
    public override void OnHidden() => S.Loaded -= Refresh;

    private void Refresh()
    {
        BuildGpuBox();
        BuildGuide();
        LoadErrors();
    }

    // ------------------------------------------------------------ GPU NVIDIA

    private void BuildGpuBox()
    {
        _gpuBox.Children.Clear();
        var g = S.Gpus.FirstOrDefault(x => x.HasNvml && !x.IsIntegrated);
        if (g == null)
        {
            _gpuBox.Children.Add(Ui.Note(S.Ready
                ? "Nenhuma GPU NVIDIA com o driver proprietário foi encontrada. Os ajustes de energia e clock só existem para elas (a NVML não funciona com o driver livre nouveau). Em placas AMD, use o CoreCtrl ou o LACT."
                : "Detectando o hardware…"));
            return;
        }
        var lim = NvmlLib.PowerConstraintsMw(g.Nvml);
        uint? cur = NvmlLib.PowerLimitMw(g.Nvml), def = NvmlLib.PowerDefaultMw(g.Nvml);
        uint? maxClk = NvmlLib.MaxClock(g.Nvml, NvmlLib.ClockGraphics);
        if (lim == null || maxClk == null)
        {
            _gpuBox.Children.Add(Ui.Note("O driver não permite ajustes nesta placa."));
            return;
        }
        _power.Minimum = lim.Value.Min / 1000.0; _power.Maximum = lim.Value.Max / 1000.0; _power.Value = (cur ?? def ?? lim.Value.Max) / 1000.0;
        _clock.Minimum = 500; _clock.Maximum = maxClk.Value; _clock.Value = maxClk.Value;
        _gpuBox.Children.Add(Ui.Muted(g.Name));
        _gpuBox.Children.Add(Ui.Label("Limite de energia"));
        _gpuBox.Children.Add(Ui.HStack(14, _power, _powerText));
        _gpuBox.Children.Add(Ui.Muted($"Padrão da placa: {(def ?? 0) / 1000.0:0} W · faixa {lim.Value.Min / 1000.0:0}–{lim.Value.Max / 1000.0:0} W"));
        _gpuBox.Children.Add(Ui.Label("Clock máximo do núcleo (trava)"));
        _gpuBox.Children.Add(Ui.HStack(14, _clock, _clockText));
        _gpuBox.Children.Add(Ui.Note("Baixar o limite de energia e travar o clock reduz calor e ruído com pouca perda de FPS. Os ajustes valem até reiniciar o PC. A senha de administrador é pedida (pkexec) ao aplicar."));
        _gpuBox.Children.Add(Ui.HStack(10, Ui.Primary("APLICAR", () => ApplyGpu(g)), Ui.Outline("RESTAURAR PADRÃO", () => ResetGpu(g, def))));
        _gpuBox.Children.Add(_gpuStatus);
        _powerText.Text = $"{_power.Value:0} W"; _clockText.Text = $"{_clock.Value:0} MHz";
        if (!GpuTuning.Available) _gpuStatus.Text = "O nvidia-smi não está instalado (vem com o driver proprietário): sem ele os ajustes não funcionam.";
    }

    private async void ApplyGpu(GpuDevice g)
    {
        _gpuStatus.Text = "Aguardando a senha de administrador…";
        int w = (int)Math.Round(_power.Value), mhz = (int)Math.Round(_clock.Value);
        var r1 = await Task.Run(() => GpuTuning.SetPowerLimit(g, w));
        if (!r1.Ok) { _gpuStatus.Text = "Limite de energia: " + r1.Message; return; }
        var r2 = await Task.Run(() => GpuTuning.LockClocks(g, mhz));
        _gpuStatus.Text = r2.Ok ? $"Aplicado: {w} W e clock máximo de {mhz} MHz (até reiniciar)." : $"Limite de energia aplicado ({w} W), mas o clock falhou: {r2.Message}";
    }

    private async void ResetGpu(GpuDevice g, uint? defMw)
    {
        _gpuStatus.Text = "Aguardando a senha de administrador…";
        var r1 = await Task.Run(() => GpuTuning.ResetClocks(g));
        var r2 = defMw is uint d ? await Task.Run(() => GpuTuning.SetPowerLimit(g, (int)(d / 1000))) : r1;
        _gpuStatus.Text = r1.Ok && r2.Ok ? "Valores padrão restaurados." : "Falhou: " + (r1.Ok ? r2.Message : r1.Message);
        BuildGpuBox();
    }

    // ------------------------------------------------------------ verificador

    private async void RunCheck()
    {
        if (_cts != null) { _cts.Cancel(); return; }
        if (!Busy.TryBegin("verificador de estabilidade", out var msg)) { _checkStatus.Text = msg; return; }
        bool cpu = _target is 0 or 2, gpu = _target is 1 or 2;
        if (gpu && GpuBench.FindTool() == null) { _checkStatus.Text = "Para verificar a GPU instale o vkmark ou o glmark2 (página Instalar apps)."; Busy.End("verificador de estabilidade"); return; }
        var duration = TimeSpan.FromMinutes(new[] { 5, 15, 30 }[_dur]);
        string label = _label.Text?.Trim() is { Length: > 0 } l ? l : "sem descrição";
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        _checkBtn.Content = "CANCELAR"; _checkBtn.Classes.Remove("primary"); _checkBtn.Classes.Add("outline");
        _verdict.IsVisible = false;

        var baseline = await Task.Run(() => KernelLog.ErrorsSince(1), ct);
        var check = new StabilityCheck(cpu, gpu, baseline);
        var stress = new StressTester(); var gstress = new GpuStress();
        string? failure = null, noLoad = null; double? cpuScore = null;
        var start = DateTime.Now;
        try
        {
            if (cpu)
            {
                _checkStatus.Text = "Medindo a nota da CPU (render multi-core)…";
                var bench = new BenchRenderer();
                var r = await Task.Run(() => bench.Run(Environment.ProcessorCount, TimeSpan.Zero, ct), ct);
                if (!ct.IsCancellationRequested) cpuScore = r.Score;
            }
            if (!ct.IsCancellationRequested)
            {
                if (cpu) stress.Start(true, true, 0.5);
                if (gpu && !gstress.Start(GpuBench.FindTool()!)) failure = "a GPU não iniciou (" + gstress.Error + ")";
                var t0 = DateTime.Now;
                while (failure == null && noLoad == null && !ct.IsCancellationRequested && DateTime.Now - t0 < duration)
                {
                    await Task.Delay(1000, ct).ContinueWith(_ => { });
                    failure = check.Sample(S.Hub.CpuTemperature, S.GpuValue("temp"), stress.MemoryErrors);
                    if (failure == null && gstress.Failed) noLoad = "a carga de GPU parou (" + gstress.Error + ")";
                    var el = DateTime.Now - t0;
                    _checkStatus.Text = $"Testando… {el:mm\\:ss} de {duration:mm\\:ss} · CPU {Ui.Num0(S.Hub.CpuTemperature, " °C")} · GPU {Ui.Num0(S.GpuValue("temp"), " °C")} · RAM {stress.MemoryErrors} erros";
                }
            }
        }
        catch (OperationCanceledException) { }
        finally { stress.Stop(); gstress.Stop(); }

        bool cancelled = ct.IsCancellationRequested && failure == null;
        _cts = null;
        _checkBtn.Content = "INICIAR VERIFICAÇÃO"; _checkBtn.Classes.Remove("outline"); _checkBtn.Classes.Add("primary");
        Busy.End("verificador de estabilidade");
        if (cancelled) { _checkStatus.Text = "Cancelado."; return; }

        var after = await Task.Run(() => KernelLog.ErrorsSince(1));
        var result = check.Finish(after, failure, DateTime.Now - start, noLoad);
        ShowVerdict(result);
        S.Settings.BenchRecords.Insert(0, new BenchRecord(DateTime.Now, "oc", result.Stable ? 1 : 0,
            $"{result.Verdict} · {label} · CPU máx {result.MaxCpuTemp:0} °C" + (cpuScore != null ? $" · nota {cpuScore:N0}" : "")));
        S.Save();
        RefreshHistory();
        _checkStatus.Text = $"Concluído às {DateTime.Now:HH:mm:ss}.";
    }

    private void ShowVerdict(StabilityResult r)
    {
        _verdictText.Text = r.Verdict;
        _verdictWhy.Text = r.Reason + $" Temperatura máxima: CPU {r.MaxCpuTemp:0} °C" + (r.MaxGpuTemp > 0 ? $", GPU {r.MaxGpuTemp:0} °C." : ".");
        // estável: cartão discreto; instável: cartão cheio, para chamar atenção
        _verdict[!Border.BackgroundProperty] = new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension(r.Stable ? "Panel" : "Fg");
        _verdictText[!TextBlock.ForegroundProperty] = new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension(r.Stable ? "Fg" : "Ink");
        _verdictWhy[!TextBlock.ForegroundProperty] = new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension(r.Stable ? "Muted" : "Ink");
        _verdict.IsVisible = true;
    }

    private void RefreshHistory()
    {
        _history.Children.Clear();
        foreach (var r in S.Settings.BenchRecords.Where(r => r.Test == "oc").Take(8))
            _history.Children.Add(Ui.HStack(14, Ui.Muted($"{r.Date:dd/MM HH:mm}").Cls("mono"), Ui.Text(r.Summary).Cls("sub")));
        if (_history.Children.Count == 0) _history.Children.Add(Ui.Muted("Nenhuma verificação ainda."));
    }

    private async void LoadErrors()
    {
        var e = await Task.Run(() => KernelLog.ErrorsSince(7));
        _errors.Children.Clear();
        if (e.Error != null) _errors.Children.Add(Ui.Note("Não foi possível ler o log do kernel: " + e.Error + "."));
        else if (e.Total == 0) _errors.Children.Add(Ui.Note("Nenhum erro de CPU, placa de vídeo, disco ou memória nos últimos 7 dias. É o que se espera de um PC estável."));
        else
        {
            _errors.Children.Add(Ui.Text(e.Summary).Cls("warn"));
            if (e.LastDetail != null) _errors.Children.Add(Ui.Muted("Último: " + e.LastDetail).Cls("mono"));
            _errors.Children.Add(Ui.Note("Erro de CPU (MCE) depois de um ajuste de Curve Optimizer ou de tensão: volte um passo e teste de novo."));
        }
    }

    // ------------------------------------------------------------ guia de BIOS

    private void BuildGuide()
    {
        _guide.Children.Clear();
        var d = S.Dmi;
        var (title, intro, steps) = OcGuide.Build(d?.BoardVendor ?? "", d?.BoardName ?? "", S.Cpu?.Model ?? "");
        _guide.Children.Add(Ui.Card(Ui.VStack(6, new TextBlock { Text = title, FontSize = 12, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap }, Ui.Note(intro))));
        foreach (var s in steps)
        {
            var head = Ui.HStack(12, new TextBlock { Text = s.Number, FontSize = 22, FontWeight = FontWeight.Bold, Width = 28 },
                                 new TextBlock { Text = s.Title, FontSize = 15, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center });
            _guide.Children.Add(Ui.Card(Ui.VStack(6, head, Ui.Muted("Onde: " + s.Where).Cls("mono"), Ui.Text(s.Text).Cls("sub"))));
        }
    }
}
