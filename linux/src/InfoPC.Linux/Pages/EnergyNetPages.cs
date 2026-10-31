using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace InfoPC.Linux;

/// <summary>Bateria (desgaste, ciclos, autonomia), fonte de energia, perfil e consumo.</summary>
public sealed class EnergyPage : PageBase
{
    public override string Title => "Energia";
    public override string Subtitle => "Bateria, fonte de energia, perfil de desempenho e consumo dos componentes.";

    private readonly StackPanel _list = new() { Spacing = 14 };
    private int _ticks;

    public EnergyPage() => Body.Children.Add(_list);

    public override void OnShown() { S.Tick -= Tick; S.Tick += Tick; Rebuild(); }
    public override void OnHidden() => S.Tick -= Tick;

    private void Tick() { if (++_ticks % 5 == 0) Rebuild(); }

    private async void Rebuild()
    {
        var cards = await Task.Run(() => EnergyCards.Build(S.Hub));
        _list.Children.Clear();
        foreach (var c in cards) _list.Children.Add(HealthCardView.Create(c));
    }
}

/// <summary>Diagnóstico de rede para lag (latência, jitter, perda, DNS, velocidade, bufferbloat) e lista de adaptadores.</summary>
public sealed class NetPage : PageBase
{
    public override string Title => "Rede";
    public override string Subtitle => "Mede a latência até o roteador e a internet, a perda de pacotes, o DNS e a velocidade. Usa speed.cloudflare.com só quando você clica em TESTAR.";

    private readonly StackPanel _result = new() { Spacing = 14 };
    private readonly StackPanel _adapters = new();
    private readonly ProgressBar _progress = new() { Minimum = 0, Maximum = 100, IsVisible = false };
    private readonly StatusText _status = new();
    private readonly Button _run, _stop;
    private CancellationTokenSource? _cts;

    public static List<DiskHealthCard>? Last { get; private set; }

    public NetPage()
    {
        _run = Ui.Primary("TESTAR REDE", Run);
        _stop = Ui.Outline("CANCELAR", () => _cts?.Cancel());
        _stop.IsVisible = false;
        Body.Children.Add(Ui.Wrap(_run.WithMargin(0, 0, 8, 0), _stop));
        Body.Children.Add(_progress);
        Body.Children.Add(_status);
        Body.Children.Add(_result);
        Body.Children.Add(Ui.Card(_adapters, "Adaptadores de rede"));
    }

    public override void OnShown() => LoadAdapters();

    private async void LoadAdapters()
    {
        var nodes = await Task.Run(NetInfo.Adapters);
        _adapters.Children.Clear();
        _adapters.Children.Add(Ui.InfoTree(nodes));
    }

    private async void Run()
    {
        _cts = new CancellationTokenSource();
        _run.IsEnabled = false; _stop.IsVisible = true; _progress.IsVisible = true; _progress.Value = 0;
        _result.Children.Clear();
        try
        {
            var cards = await Task.Run(() => NetworkTest.Run((text, frac) => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                _status.Text = text; _progress.Value = frac * 100;
            }), _cts.Token));
            Last = cards;
            foreach (var c in cards) _result.Children.Add(HealthCardView.Create(c));
            _status.Text = $"Teste concluído às {DateTime.Now:HH:mm:ss}.";
        }
        catch (OperationCanceledException) { _status.Text = "Teste cancelado."; }
        catch (Exception ex) { _status.Text = "O teste falhou: " + ex.Message; }
        finally { _run.IsEnabled = true; _stop.IsVisible = false; _progress.IsVisible = false; }
    }
}
