using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace InfoPC.Linux;

/// <summary>
/// Overlay de jogos. No Linux há dois caminhos: o overlay do InfoPC (janela transparente sobre o jogo, só no X11/XWayland, sem FPS)
/// e o MangoHud (camada dentro do jogo, com FPS e frametime). Esta página liga o primeiro e escreve a configuração do segundo.
/// </summary>
public sealed class OverlayPage : PageBase
{
    public override string Title => "Overlay";
    public override string Subtitle => "Mostre FPS, temperaturas e uso na tela durante o jogo. Escolha os itens uma vez e use os dois caminhos.";

    private readonly Button _toggle;
    private readonly StatusText _status = new();
    private readonly StatusText _mangoStatus = new();
    private readonly StackPanel _groups = new() { Spacing = 12 };
    private OverlayWindow? _win;
    private HashSet<string> _items = new();
    private static readonly string[] Corners = { "TL", "TR", "BL", "BR" };
    private static readonly double[] Scales = { 0.8, 1.0, 1.25, 1.5 };

    public OverlayPage()
    {
        _toggle = Ui.Primary("LIGAR OVERLAY DO INFOPC", Toggle);
        var s = S.Settings;
        _items = (s.OverlayItems ?? OverlayMetrics.Defaults().ToList()).ToHashSet();

        int ci = Math.Max(0, Array.IndexOf(Corners, s.OverlayCorner));
        int si = Math.Max(0, Array.FindIndex(Scales, x => Math.Abs(x - s.OverlayScale) < 0.01));
        var pos = Ui.VStack(10,
            Ui.Label("Canto"), Ui.Chips("ovcorner", new[] { "Superior esquerdo", "Superior direito", "Inferior esquerdo", "Inferior direito" }, ci, i => { S.Settings.OverlayCorner = Corners[i]; Apply(); }),
            Ui.Label("Tamanho"), Ui.Chips("ovscale", new[] { "Pequeno", "Normal", "Grande", "Enorme" }, si, i => { S.Settings.OverlayScale = Scales[i]; Apply(); }));

        var own = Ui.VStack(10,
            Ui.Note(X11.Available
                ? "Janela transparente, sempre no topo, que deixa o mouse passar. Funciona sobre jogos em janela e sem bordas. Em tela cheia exclusiva, ou em jogos nativos de Wayland, o jogo fica por cima: nesse caso use o MangoHud ao lado. O FPS não aparece aqui (medir o FPS de fora exigiria injetar código no jogo)."
                : "Não encontrei um servidor X (DISPLAY): o overlay do InfoPC precisa de X11 ou XWayland. Use o MangoHud ao lado."),
            Ui.HStack(10, _toggle), _status);

        var mango = Ui.VStack(10,
            Ui.Note("O MangoHud é a forma padrão no Linux de ver FPS, frametime e 1% low dentro do jogo. O InfoPC escreve a configuração dele com os itens escolhidos abaixo (o arquivo anterior fica com a extensão .infopc.bak). Para ligar num jogo da Steam, marque MangoHud no perfil do jogo (página Biblioteca) ou use mangohud %command% nas opções de inicialização. Tecla padrão para mostrar/ocultar: Shift direito + F12."),
            Ui.HStack(10, Ui.Primary("GERAR CONFIGURAÇÃO DO MANGOHUD", WriteMango), Ui.Outline("ABRIR INSTALAR APPS", () => { if (TopLevel.GetTopLevel(this) is MainWindow w) w.Navigate("apps"); })),
            Ui.Muted(Shell.Has("mangohud") ? "MangoHud instalado." : "O MangoHud não está instalado (página Instalar apps)."), _mangoStatus);

        var top = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 16 };
        var a = Ui.Card(own, "Overlay do InfoPC"); var b = Ui.Card(mango, "MangoHud");
        Grid.SetColumn(b, 1);
        top.Children.Add(a); top.Children.Add(b);
        Body.Children.Add(top);
        Body.Children.Add(Ui.Card(pos, "Posição"));
        BuildGroups();
        Body.Children.Add(Ui.Card(_groups, "O que mostrar"));
    }

    private void BuildGroups()
    {
        _groups.Children.Clear();
        foreach (var grp in OverlayMetrics.All.GroupBy(m => m.Group))
        {
            var wrap = new WrapPanel();
            foreach (var m in grp)
            {
                var cb = new CheckBox { Content = m.Label, IsChecked = _items.Contains(m.Key), Margin = new Thickness(0, 0, 18, 4) };
                string where = m.Group == "FPS" ? "só no MangoHud" : MangoHud.Supports(m.Key) ? "" : "só no overlay do InfoPC";
                if (where.Length > 0) ToolTip.SetTip(cb, where);
                cb.IsCheckedChanged += (_, _) =>
                {
                    if (cb.IsChecked == true) _items.Add(m.Key); else _items.Remove(m.Key);
                    S.Settings.OverlayItems = _items.ToList();
                    Apply();
                };
                wrap.Children.Add(cb);
            }
            _groups.Children.Add(Ui.VStack(4, Ui.Label(grp.Key == "FPS" ? "FPS (só no MangoHud)" : grp.Key), wrap));
        }
    }

    // A atualização acompanha a janela do overlay, não a página: o overlay continua aberto (e atualizado) com outra página na frente.
    private void Tick() => _win?.Update();

    internal void Attach(OverlayWindow w)
    {
        _win = w;
        S.Tick -= Tick; S.Tick += Tick;
        w.Closed += (_, _) =>
        {
            S.Tick -= Tick;
            if (_win == w) { _win = null; _toggle.Content = "LIGAR OVERLAY DO INFOPC"; }
        };
    }

    private void Apply()
    {
        S.Save();
        _win?.Configure(_items, S.Settings.OverlayCorner, S.Settings.OverlayScale, _library);
    }

    private List<LibGame> _library = new();

    private async void Toggle()
    {
        if (_win != null)
        {
            _win.Close(); _win = null;
            _toggle.Content = "LIGAR OVERLAY DO INFOPC";
            _status.Text = "Overlay desligado.";
            return;
        }
        if (!X11.Available) { _status.Text = "Sem servidor X: o overlay do InfoPC não pode abrir."; return; }
        _library = await Task.Run(() => GameLibrary.Scan(S.Settings.CustomGames.ToList()));
        var win = new OverlayWindow();
        Attach(win);
        win.Show();
        win.Configure(_items, S.Settings.OverlayCorner, S.Settings.OverlayScale, _library);
        _toggle.Content = "DESLIGAR OVERLAY";
        _status.Text = X11.IsWayland ? "Overlay ligado (via XWayland). Jogos nativos de Wayland ou em tela cheia exclusiva ficam por cima dele." : "Overlay ligado.";
    }

    private void WriteMango()
    {
        try
        {
            string path = MangoHud.Write(MangoHud.Build(_items, S.Settings.OverlayCorner, S.Settings.OverlayScale));
            _mangoStatus.Text = $"Configuração gravada em {path}. Abra o jogo com o MangoHud ligado para ver.";
        }
        catch (Exception ex) { _mangoStatus.Text = "Não consegui gravar: " + ex.Message; }
    }
}
