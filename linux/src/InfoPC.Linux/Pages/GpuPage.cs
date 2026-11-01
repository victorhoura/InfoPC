using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace InfoPC.Linux;

/// <summary>Placa de vídeo no estilo do GPU-Z: chip, memória, clocks, barramento, energia, driver e leituras ao vivo.</summary>
public sealed class GpuPage : PageBase
{
    public override string Title => "Placa de vídeo";
    public override string Subtitle => _data?.Subtitle is { Length: > 0 } s ? s : "Chip, memória, clocks, barramento, driver e leituras ao vivo.";

    private readonly StackPanel _chips = new();
    private readonly StackPanel _content = new() { Spacing = 16 };
    private readonly Dictionary<string, TextBlock> _live = new();
    private StackPanel _extras = new() { Spacing = 4 };
    private GpuReportData? _data;
    private int _index;
    private int _generation;
    private bool _tickBusy;

    public GpuPage()
    {
        Body.Children.Add(_chips);
        Body.Children.Add(_content);
    }

    public override void OnShown()
    {
        S.Loaded -= Rebuild; S.Loaded += Rebuild;
        S.Tick -= Tick; S.Tick += Tick;
        Rebuild();
    }

    public override void OnHidden() { _generation++; S.Loaded -= Rebuild; S.Tick -= Tick; }

    private async void Rebuild()
    {
        int generation = ++_generation;
        _chips.Children.Clear();
        _content.Children.Clear();
        _live.Clear();
        if (!S.Ready) { _content.Children.Add(Ui.Muted("Detectando o hardware…")); return; }
        if (S.Gpus.Count == 0)
        {
            _content.Children.Add(Ui.Card(Ui.VStack(8,
                new TextBlock { Text = "Nenhuma placa de vídeo PCI encontrada", FontSize = 16, FontWeight = FontWeight.SemiBold },
                Ui.Note("O kernel não listou GPUs em /sys/class/drm. Isso é normal em máquina virtual, contêiner, WSL e em computadores ARM com GPU integrada ao SoC."))));
            return;
        }
        if (S.Gpus.Count > 1)
            _chips.Children.Add(Ui.Chips("gpusel", S.Gpus.Select(g => g.Name).ToList(), _index, i => { _index = i; Rebuild(); }));
        _index = Math.Clamp(_index, 0, S.Gpus.Count - 1);
        var gpu = S.Gpus[_index];

        GpuReportData data;
        try { data = await Task.Run(() => GpuInfoBuilder.Build(gpu)); }
        catch (Exception ex)
        {
            CrashLog.Write("Detalhes da GPU", ex);
            if (generation == _generation) _content.Children.Add(Ui.Note("Não consegui ler a placa de vídeo: " + ex.Message));
            return;
        }
        if (generation != _generation) return;
        _data = data;
        _content.Children.Clear();

        // coluna esquerda: seções fixas; direita: leituras ao vivo
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,360"), ColumnSpacing = 16 };
        var left = new StackPanel { Spacing = 16 };
        foreach (var sec in _data.Sections)
            left.Children.Add(Ui.Card(Ui.KeyValues(sec.Children.Select(c => new KeyValuePair<string, string>(c.Header, c.Value)), 200), sec.Header));
        Grid.SetColumn(left, 0);

        var liveBox = new StackPanel { Spacing = 6 };
        foreach (var name in new[] { "Uso da GPU", "Temperatura", "Hotspot", "Clock do núcleo", "Clock da memória", "Consumo", "Tensão", "Ventoinha", "Memória de vídeo" })
        {
            var v = new TextBlock { Text = "—", FontFamily = new FontFamily("DejaVu Sans Mono, Liberation Mono, monospace"), FontSize = 13, TextAlignment = TextAlignment.Right };
            _live[name] = v;
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            var k = Ui.Muted(name); k.FontSize = 12.5;
            Grid.SetColumn(v, 1);
            row.Children.Add(k); row.Children.Add(v);
            liveBox.Children.Add(row);
        }
        liveBox.Children.Add(Ui.Separator());
        // O painel anterior ainda pertence ao liveBox anterior, mesmo fora da janela.
        _extras = new StackPanel { Spacing = 4 };
        liveBox.Children.Add(_extras);
        var right = Ui.Card(liveBox, "Leituras ao vivo");
        right.VerticalAlignment = VerticalAlignment.Top;
        Grid.SetColumn(right, 1);
        grid.Children.Add(left); grid.Children.Add(right);
        _content.Children.Add(grid);
        Tick();
        RaiseHeaderChanged();
    }

    private async void Tick()
    {
        if (_tickBusy || S.Gpus.Count == 0 || _live.Count == 0) return;
        var g = S.Gpus[Math.Clamp(_index, 0, S.Gpus.Count - 1)];
        int generation = _generation;
        bool suspended;
        List<KeyValuePair<string, string>> extras;
        _tickBusy = true;
        try
        {
            (suspended, extras) = await Task.Run(() => (g.Suspended, GpuInfoBuilder.LiveExtras(g)));
        }
        catch (Exception ex) { CrashLog.Write("Leituras da GPU", ex); return; }
        finally { _tickBusy = false; }
        if (generation != _generation || _live.Count == 0) return;
        string p = $"gpu{g.Index}.";
        if (suspended)
        {
            foreach (var t in _live.Values) t.Text = "—";
            _live["Uso da GPU"].Text = "em repouso";
            _extras.Children.Clear();
            return;
        }
        float? V(string r) => S.Hub.Role(p + r);
        _live["Uso da GPU"].Text = Ui.Num0(V("load"), " %");
        _live["Temperatura"].Text = Ui.Num0(V("temp"), " °C");
        _live["Hotspot"].Text = Ui.Num0(V("hotspot"), " °C");
        _live["Clock do núcleo"].Text = Ui.Num0(V("clock"), " MHz");
        _live["Clock da memória"].Text = Ui.Num0(V("memclock"), " MHz");
        _live["Consumo"].Text = Ui.Num1(V("power"), " W");
        _live["Tensão"].Text = V("volt") is float volt ? $"{volt:0.000} V" : "—";
        _live["Ventoinha"].Text = V("fan") is float fan ? (g.HasNvml ? $"{fan:0} %" : $"{fan:0} RPM") : "—";
        _live["Memória de vídeo"].Text = V("vram.used") is float used ? $"{used:0.0} GB" + (V("vram.pct") is float pct ? $" ({pct:0}%)" : "") : "—";

        _extras.Children.Clear();
        foreach (var kv in extras)
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            var k = Ui.Muted(kv.Key); k.FontSize = 12.5;
            var v = new TextBlock { Text = kv.Value, FontSize = 12.5, TextAlignment = TextAlignment.Right };
            Grid.SetColumn(v, 1);
            row.Children.Add(k); row.Children.Add(v);
            _extras.Children.Add(row);
        }
    }
}
