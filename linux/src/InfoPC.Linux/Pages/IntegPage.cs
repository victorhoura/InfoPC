using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace InfoPC.Linux;

/// <summary>Integração: iluminação RGB pelo servidor do OpenRGB (placas-mãe, RAM, ventoinhas, teclados e mouses de várias marcas).</summary>
public sealed class IntegPage : PageBase
{
    public override string Title => "Integração";
    public override string Subtitle => "Controle de iluminação e efeitos pelo OpenRGB. Corsair iCUE e Razer Synapse não existem no Linux; o OpenRGB (e o OpenRazer) cobrem essas marcas.";

    private static readonly (string Name, byte R, byte G, byte B)[] Swatches =
    {
        ("Branco", 255, 255, 255), ("Vermelho", 255, 0, 0), ("Laranja", 255, 110, 0), ("Amarelo", 255, 210, 0),
        ("Verde", 0, 255, 60), ("Ciano", 0, 220, 255), ("Azul", 0, 80, 255), ("Roxo", 150, 0, 255), ("Rosa", 255, 0, 160),
    };
    private static readonly (string Label, string Key)[] Effects =
    {
        ("Cor fixa", "static"), ("Respiração", "breath"), ("Arco-íris", "rainbow"), ("Temperatura da CPU", "cpu"),
        ("Temperatura da GPU", "gpu"), ("Carga da CPU", "load"), ("Apagar", "off"),
    };

    private readonly OpenRgbBackend _orgb = new();
    private readonly LightEngine _engine = new();
    private readonly List<(LightDevice Device, CheckBox Box)> _devs = new();
    private readonly StackPanel _devList = new() { Spacing = 6 };
    private readonly StatusText _status = new();
    private readonly StatusText _hint = new();
    private readonly TextBlock _live = Ui.Muted("");
    private readonly TextBlock _diag = new() { FontSize = 11, TextWrapping = TextWrapping.Wrap };
    private readonly Slider _r = new() { Minimum = 0, Maximum = 255, Value = 255, Width = 220 };
    private readonly Slider _g = new() { Minimum = 0, Maximum = 255, Value = 255, Width = 220 };
    private readonly Slider _b = new() { Minimum = 0, Maximum = 255, Value = 255, Width = 220 };
    private readonly Slider _bright = new() { Minimum = 0, Maximum = 100, Value = 100, Width = 220 };
    private readonly Slider _speed = new() { Minimum = 1, Maximum = 10, Value = 5, Width = 220 };
    private readonly Border _preview = new() { Width = 56, Height = 28, CornerRadius = new CornerRadius(6), Background = Brushes.White };
    private readonly Button _run;
    private readonly Queue<string> _log = new();
    private DispatcherTimer? _timer;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private bool _running;

    public IntegPage()
    {
        _run = Ui.Primary("INICIAR", ToggleRun);
        _diag.Classes.Add("mono"); _diag.Classes.Add("muted");
        _engine.Diag = line => Post(() => { _log.Enqueue($"{DateTime.Now:HH:mm:ss} {line}"); while (_log.Count > 40) _log.Dequeue(); _diag.Text = string.Join("\n", _log); });
        _engine.LiveValue = effect => effect switch { "cpu" => S.Hub.CpuTemperature, "gpu" => S.GpuValue("temp"), "load" => S.Hub.CpuLoad, _ => null };

        foreach (var s in new[] { _r, _g, _b, _bright, _speed }) s.ValueChanged += (_, _) => Colors();

        var connect = Ui.VStack(12,
            Ui.Note("O OpenRGB precisa estar com o servidor SDK ligado (porta 6742). O botão abaixo abre o OpenRGB minimizado com o servidor e conecta. Tudo é local: nada sai do computador."),
            Ui.Wrap(Ui.Primary("CONECTAR", async () => await ConnectAsync()).WithMargin(0, 0, 8, 8), Ui.Outline("ABRIR O OPENRGB (servidor)", OpenOrgb).WithMargin(0, 0, 8, 8),
                    Ui.Outline("INSTALAR O OPENRGB", () => { if (TopLevel.GetTopLevel(this) is MainWindow w) w.Navigate("apps"); }).WithMargin(0, 0, 8, 8)),
            _status, _hint);
        Body.Children.Add(Ui.Card(connect, "OpenRGB"));
        Body.Children.Add(Ui.Card(_devList, "Dispositivos"));

        var fx = Ui.VStack(14,
            Ui.Label("Efeito"), Ui.Chips("lightfx", Effects.Select(e => e.Label).ToList(), 0, i => { _engine.Effect = Effects[i].Key; _engine.Reset(); }),
            Ui.Label("Cor (efeitos de cor fixa e respiração)"), Ui.Wrap(Swatches.Select(s => SwatchButton(s)).ToArray()),
            Ui.HStack(14, Ui.Muted("R"), _r, Ui.Muted("G"), _g, Ui.Muted("B"), _b, _preview),
            Ui.HStack(14, Ui.Muted("Brilho"), _bright, Ui.Muted("Velocidade"), _speed),
            Ui.HStack(10, _run), _live);
        Body.Children.Add(Ui.Card(fx, "Efeitos"));
        Body.Children.Add(Ui.Card(_diag, "Diagnóstico"));
        _hint.Text = OpenRgbLocator.SetupHint() ?? "";
    }

    private Control SwatchButton((string Name, byte R, byte G, byte B) s)
    {
        var b = new Button { Width = 34, Height = 34, CornerRadius = new CornerRadius(17), Margin = new Thickness(0, 0, 8, 8), Background = new SolidColorBrush(Color.FromRgb(s.R, s.G, s.B)), BorderThickness = new Thickness(1) };
        ToolTip.SetTip(b, s.Name);
        b.Click += (_, _) => { _r.Value = s.R; _g.Value = s.G; _b.Value = s.B; };
        return b;
    }

    private void Colors()
    {
        _engine.Color = ((byte)_r.Value, (byte)_g.Value, (byte)_b.Value);
        _engine.Brightness = _bright.Value; _engine.Speed = _speed.Value;
        _preview.Background = new SolidColorBrush(Color.FromRgb(_engine.Color.R, _engine.Color.G, _engine.Color.B));
        _engine.Reset();
    }

    public override void OnHidden() { }

    // ---------------- conexão ----------------

    private async Task ConnectAsync()
    {
        _status.Text = "Conectando ao OpenRGB…";
        var (devices, msg) = await Task.Run(() => _orgb.Connect("127.0.0.1", 6742));
        _status.Text = "OpenRGB: " + msg;
        _devs.Clear(); _devList.Children.Clear();
        foreach (var d in devices)
        {
            var cb = new CheckBox { IsChecked = true, Content = $"{d.Name}  —  {d.Detail}" };
            _devs.Add((d, cb));
            _devList.Children.Add(cb);
        }
        if (devices.Count == 0) _devList.Children.Add(Ui.Muted("Nenhum dispositivo com LEDs encontrado."));
        _hint.Text = devices.Count == 0 ? OpenRgbLocator.SetupHint() ?? "" : "";
    }

    private async void OpenOrgb()
    {
        var l = OpenRgbLocator.Find();
        if (l == null)
        {
            Shell.Open("https://openrgb.org/");
            _status.Text = "OpenRGB não encontrado: abri o site openrgb.org. Prefira instalar pelo pacote da distribuição ou pelo Flatpak (página Instalar apps).";
            return;
        }
        if (!OpenRgbLocator.Start(l)) { _status.Text = "Não consegui abrir: " + l.Description; return; }
        _status.Text = $"Abrindo {l.Description} com o servidor ligado…";
        await Task.Delay(4000);
        await ConnectAsync();
    }

    // ---------------- execução ----------------

    private void ToggleRun()
    {
        if (_running) { Stop(release: true); return; }
        var sel = _devs.Where(d => d.Box.IsChecked == true).Select(d => d.Device).ToList();
        if (sel.Count == 0) { _status.Text = "Conecte ao OpenRGB e marque pelo menos um dispositivo."; return; }
        foreach (var d in sel) d.Backend.Acquire(d, exclusive: false);
        Colors();
        _running = true;
        _run.Content = "PARAR"; _run.Classes.Remove("primary"); _run.Classes.Add("outline");
        _timer ??= new DispatcherTimer(TimeSpan.FromMilliseconds(33), DispatcherPriority.Background, (_, _) => Tick());
        _timer.Start();
    }

    private void Tick()
    {
        var sel = _devs.Where(d => d.Box.IsChecked == true).Select(d => d.Device);
        _live.Text = _engine.Tick(sel, _clock.Elapsed.TotalSeconds, Environment.TickCount64);
    }

    private void Stop(bool release)
    {
        _timer?.Stop();
        _running = false;
        _run.Content = "INICIAR"; _run.Classes.Remove("outline"); _run.Classes.Add("primary");
        if (release) foreach (var (d, _) in _devs) d.Backend.Release(d);   // devolve o dispositivo ao modo que tinha
        _live.Text = "";
    }
}
