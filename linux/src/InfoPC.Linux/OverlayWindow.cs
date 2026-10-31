using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace InfoPC.Linux;

/// <summary>
/// Overlay do InfoPC: janela transparente, sem borda, sempre no topo e que ignora cliques (no X11), com os itens que o usuário escolheu.
/// O FPS não aparece aqui (não há como medi-lo de fora, sem injetar no jogo): para ele, use o MangoHud.
/// </summary>
public sealed class OverlayWindow : Window
{
    private readonly StackPanel _lines = new() { Spacing = 2 };
    private readonly LayoutTransformControl _scaleHost = new();
    private HashSet<string> _items = OverlayMetrics.Defaults();
    private List<LibGame> _library = new();
    private DateTime _gameAt;
    private string _game = "";

    public OverlayWindow()
    {
        SystemDecorations = SystemDecorations.None;
        Topmost = true; ShowInTaskbar = false; CanResize = false; ShowActivated = false; Focusable = false;
        Background = Brushes.Transparent;
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
        SizeToContent = SizeToContent.WidthAndHeight;
        _scaleHost.Child = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(185, 0, 0, 0)), CornerRadius = new CornerRadius(10), Padding = new Thickness(12, 8),
            Child = _lines,
        };
        Content = _scaleHost;
        Opened += (_, _) =>
        {
            Place();
            X11.MakeClickThrough(this);
        };
    }

    public void Configure(IEnumerable<string> items, string corner, double scale, List<LibGame> library)
    {
        _items = items.ToHashSet();
        _library = library;
        _corner = corner; _scale = scale;
        _scaleHost.LayoutTransform = new ScaleTransform(scale, scale);
        Update();
        Place();
    }

    private string _corner = "TL";
    private double _scale = 1;

    public void Place()
    {
        var screen = Screens.Primary;
        if (screen == null) return;
        var wa = screen.WorkingArea;
        double s = screen.Scaling;
        int w = (int)(Bounds.Width * s), h = (int)(Bounds.Height * s);
        if (w == 0 || h == 0) { w = 220; h = 200; }
        int margin = 16;
        int x = _corner is "TR" or "BR" ? wa.Right - w - margin : wa.X + margin;
        int y = _corner is "BL" or "BR" ? wa.Bottom - h - margin : wa.Y + margin;
        Position = new PixelPoint(x, y);
    }

    private static TextBlock Cell(string t, bool strong) => new()
    {
        Text = t, FontSize = 13, FontFamily = new FontFamily("DejaVu Sans Mono, Liberation Mono, monospace"),
        Foreground = strong ? Brushes.White : new SolidColorBrush(Color.FromRgb(170, 170, 170)),
    };

    /// <summary>Linhas prontas (rótulo, valor) a partir dos sensores; itens sem leitura ficam de fora.</summary>
    public static List<OverlayLine> Lines(IEnumerable<string> items, SensorHub hub, GpuDevice? gpu, string game)
    {
        var set = items.ToHashSet();
        string g = gpu != null ? $"gpu{gpu.Index}." : "none.";
        float? V(string r) => hub.Role(r);
        var lines = new List<OverlayLine>();
        void Add(string key, string label, string? value) { if (set.Contains(key) && value != null) lines.Add(new OverlayLine(label, value)); }
        string? F(float? v, string fmt, string unit) => v is float x ? x.ToString(fmt) + unit : null;

        Add("game", "JOGO", game.Length > 0 ? game : null);
        Add("gpu_load", "GPU", Join(F(V(g + "load"), "0", "%"), F(V(g + "temp"), "0", "°C"), F(V(g + "clock"), "0", " MHz"), F(V(g + "power"), "0", " W")));
        Add("gpu_temp", "GPU temp", set.Contains("gpu_load") ? null : F(V(g + "temp"), "0", " °C"));
        Add("gpu_hotspot", "GPU hotspot", F(V(g + "hotspot"), "0", " °C"));
        Add("gpu_memclock", "GPU memória", F(V(g + "memclock"), "0", " MHz"));
        Add("gpu_volt", "GPU tensão", F(V(g + "volt"), "0.000", " V"));
        Add("gpu_fan", "GPU fan", F(V(g + "fan"), "0", gpu?.HasNvml == true ? " %" : " RPM"));
        if (set.Contains("gpu_perfcap") && gpu is { HasNvml: true, Suspended: false } && NvmlLib.ThrottleReasons(gpu.Nvml) is ulong r) lines.Add(new OverlayLine("GPU limite", NvmlLib.PerfCap(r)));
        Add("vram_used", "VRAM", V(g + "vram.used") is float vu ? $"{vu:0.0} GB" + (set.Contains("vram_pct") && V(g + "vram.pct") is float vp ? $" ({vp:0}%)" : "") : null);
        Add("vram_temp", "VRAM temp", F(V(g + "memtemp"), "0", " °C"));
        Add("cpu_load", "CPU", Join(F(hub.CpuLoad, "0", "%"), F(hub.CpuTemperature, "0", "°C"), F(V("cpu.clock"), "0", " MHz"), F(hub.CpuPower, "0", " W")));
        Add("cpu_maxcore", "CPU thread", F(V("cpu.maxcore"), "0", "%"));
        Add("cpu_temp", "CPU temp", set.Contains("cpu_load") ? null : F(hub.CpuTemperature, "0", " °C"));
        Add("cpu_clockmax", "CPU clock máx.", F(V("cpu.clockmax"), "0", " MHz"));
        Add("ram_used", "RAM", V("ram.used") is float ru ? $"{ru:0.0} GB" + (set.Contains("ram_pct") && hub.RamPercent is float rp ? $" ({rp:0}%)" : "") : null);
        Add("clock", "HORA", DateTime.Now.ToString("HH:mm"));
        return lines;
    }

    private static string? Join(params string?[] parts) { var p = parts.Where(x => x != null).ToArray(); return p.Length == 0 ? null : string.Join("  ", p); }

    public void Update()
    {
        var s = Services.Current;
        if (DateTime.Now - _gameAt > TimeSpan.FromSeconds(3))
        {
            _gameAt = DateTime.Now;
            _game = _library.Count > 0 ? GameDetector.Any(_library)?.Name ?? "" : "";
        }
        var lines = Lines(_items, s.Hub, s.MainGpu, _game);
        _lines.Children.Clear();
        foreach (var l in lines)
            _lines.Children.Add(Ui.HStack(10, Cell(l.Label, false), Cell(l.Value, true)));
        if (lines.Count == 0) _lines.Children.Add(Cell("sem itens para mostrar", false));
    }
}
