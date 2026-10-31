using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;

namespace InfoPC.Linux;

/// <summary>FPS esperado em 32 jogos populares para a sua GPU e CPU (estimativa local, ou dados reais da HowManyFPS com chave).</summary>
public sealed class GamesPage : PageBase
{
    public override string Title => "FPS estimado";
    public override string Subtitle => _sub;
    private string _sub = "FPS esperado no seu PC";

    private static readonly string[] ResValues = { "1920x1080", "2560x1440", "3840x2160" };
    private static readonly string[] PresetValues = { "Baixo", "Médio", "Alto", "Ultra" };
    private static readonly string[] UpValues = { "Native", "Quality", "Performance" };

    private readonly HowManyFpsClient _api = new();
    private List<GameRow> _rows = new();
    private readonly StackPanel _list = new() { Spacing = 8 };
    private readonly TextBox _search = new() { Watermark = "Buscar jogo…", MaxWidth = 340, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly TextBox _key = new() { PasswordChar = '•', Watermark = "chave da API (opcional)", MinWidth = 320 };
    private readonly TextBlock _sourceTitle = new() { FontWeight = FontWeight.SemiBold }, _sourceText = Ui.Note("");
    private readonly Button _saveKey, _fetchBtn;
    private int _res, _preset = 2, _up;
    private double _gpuIdx = 50, _cpuIdx = 80;
    private string _cpuName = "", _gpuName = "";
    private CancellationTokenSource? _fetch;
    private bool _requested; // a API só é consultada depois de um clique nesta sessão (requisito 5)

    public GamesPage()
    {
        _saveKey = Ui.Outline("SALVAR CHAVE", SaveKey).Small();
        _fetchBtn = Ui.Primary("BUSCAR FPS REAL", () => { _requested = true; Refresh(); }).Small();
        _search.TextChanged += (_, _) => ApplyFilter();
        var opts = Ui.VStack(6,
            Ui.Label("Resolução"), Ui.Chips("gres", new[] { "1080p", "1440p", "4K" }, _res, i => { _res = i; Refresh(); }),
            Ui.Label("Gráficos (só na estimativa)"), Ui.Chips("gpreset", PresetValues, _preset, i => { _preset = i; Refresh(); }),
            Ui.Label("Upscaling (DLSS / FSR)"), Ui.Chips("gup", new[] { "Nativo", "Qualidade", "Desempenho" }, _up, i => { _up = i; Refresh(); }));
        var src = Ui.VStack(8, _sourceTitle, _sourceText, Ui.HStack(8, _key, _saveKey, _fetchBtn));
        var top = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 16 };
        var c1 = Ui.Card(opts, "Cenário"); var c2 = Ui.Card(src, "Fonte dos números");
        Grid.SetColumn(c2, 1);
        top.Children.Add(c1); top.Children.Add(c2);
        Body.Children.Add(top);
        Body.Children.Add(_search);
        Body.Children.Add(_list);
    }

    public override void OnShown() { S.Loaded -= Init; S.Loaded += Init; Init(); }
    public override void OnHidden() { S.Loaded -= Init; _fetch?.Cancel(); }

    private void Init()
    {
        _cpuName = S.Cpu?.Model ?? "";
        _gpuName = S.MainGpu?.Name ?? "";
        _gpuIdx = GameCatalog.GpuPerf(_gpuName) ?? 50;
        _cpuIdx = GameCatalog.CpuPerf(_cpuName) ?? 80;
        _sub = $"FPS esperado no seu PC · {(_gpuName.Length > 0 ? GameCatalog.ShortGpuName(_gpuName) : "GPU não detectada")} · {(_cpuName.Length > 0 ? GameCatalog.ShortCpuName(_cpuName) : "CPU não detectada")}";
        RaiseHeaderChanged();
        _api.ApiKey = S.Settings.HowManyFpsKey;
        if (_api.Configured) _key.Text = S.Settings.HowManyFpsKey;
        if (_rows.Count == 0)
        {
            _rows = GameCatalog.Games.Select(g => new GameRow(g)).ToList();
            foreach (var r in _rows) _list.Children.Add(Card(r));
        }
        Refresh();
    }

    private Control Card(GameRow r)
    {
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,110,200,150"), ColumnSpacing = 14, DataContext = r };
        var name = Ui.VStack(0, new TextBlock { Text = r.Name, FontWeight = FontWeight.SemiBold, FontSize = 14 }, Ui.Muted(r.Genre).Cls("label"));
        var fps = new TextBlock { FontSize = 24, FontFamily = new FontFamily("DejaVu Sans Mono, Liberation Mono, monospace"), TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        fps[!TextBlock.TextProperty] = new Binding(nameof(GameRow.FpsText));
        var middle = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 4 };
        var bar = new ProgressBar { Minimum = 0, Maximum = 1 };
        bar[!ProgressBar.ValueProperty] = new Binding(nameof(GameRow.Bar));
        var rating = new TextBlock { FontSize = 11, FontWeight = FontWeight.SemiBold };
        rating[!TextBlock.TextProperty] = new Binding(nameof(GameRow.Rating));
        middle.Children.Add(bar); middle.Children.Add(rating);
        var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 2 };
        var low = new TextBlock { FontSize = 12 }; low.Classes.Add("muted");
        low[!TextBlock.TextProperty] = new Binding(nameof(GameRow.LowText));
        var source = new TextBlock { FontSize = 10.5, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap }; source.Classes.Add("muted");
        source[!TextBlock.TextProperty] = new Binding(nameof(GameRow.Source));
        var detail = new TextBlock { FontSize = 11, TextWrapping = TextWrapping.Wrap }; detail.Classes.Add("muted");
        detail[!TextBlock.TextProperty] = new Binding(nameof(GameRow.Detail));
        info.Children.Add(low); info.Children.Add(source); info.Children.Add(detail);
        Grid.SetColumn(fps, 1); Grid.SetColumn(middle, 2); Grid.SetColumn(info, 3);
        g.Children.Add(name); g.Children.Add(fps); g.Children.Add(middle); g.Children.Add(info);
        var card = Ui.Card(g, null, 14);
        card.Tag = r.Name;
        return card;
    }

    private void ApplyFilter()
    {
        string q = (_search.Text ?? "").Trim();
        foreach (var c in _list.Children.OfType<Border>())
            c.IsVisible = q.Length == 0 || ((string)c.Tag!).Contains(q, StringComparison.OrdinalIgnoreCase);
    }

    private void SaveKey()
    {
        S.Settings.HowManyFpsKey = string.IsNullOrWhiteSpace(_key.Text) ? null : _key.Text.Trim();
        S.Save();
        _api.ApiKey = S.Settings.HowManyFpsKey;
        _requested = _api.Configured; // salvar a chave é pedir os dados reais
        Refresh();
    }

    /// <summary>
    /// Recalcula a estimativa local de todos os jogos. Com chave configurada, mostra o que já está no cache e só consulta
    /// a API depois que o usuário pede (BUSCAR FPS REAL ou salvar a chave): abrir a página não usa a internet (requisito 5).
    /// </summary>
    private void Refresh()
    {
        string res = ResValues[_res], preset = PresetValues[_preset], up = UpValues[_up];
        foreach (var row in _rows)
        {
            var est = GameCatalog.Estimate(row.Game, _gpuIdx, _cpuIdx, res, preset, up);
            string limiter = est.Limiter switch
            {
                Bottleneck.Cpu => "gargalo: CPU", Bottleneck.Gpu => "gargalo: GPU",
                Bottleneck.Engine => $"trava do jogo em {row.Game.EngineCap:0} FPS", _ => ""
            };
            row.Set(est.Avg, 0, "ESTIMATIVA", limiter);
        }
        _fetch?.Cancel();
        _fetchBtn.IsVisible = _api.Configured;
        if (_api.Configured && _requested)
        {
            _sourceTitle.Text = "Fonte: HowManyFPS (dados reais)";
            _sourceText.Text = "FPS médio e 1% low medidos em sessões reais de jogo. As respostas ficam em cache por 7 dias. \"Gráficos\" vale só para a estimativa.";
            _saveKey.Content = "ATUALIZAR CHAVE";
            _fetch = new CancellationTokenSource();
            _ = FetchAsync(res, up, _fetch.Token);
        }
        else if (_api.Configured)
        {
            string cpu = GameCatalog.ShortCpuName(_cpuName), gpu = GameCatalog.ShortGpuName(_gpuName);
            int cached = 0;
            foreach (var row in _rows)
                if (_api.Cached(cpu, gpu, row.Name, res, up) is { } r) { row.Set(r.AvgFps, r.Min1Fps, "HOWMANYFPS", ""); cached++; }
            _sourceTitle.Text = cached > 0 ? "Fonte: HowManyFPS (cache) e estimativa local" : "Fonte: estimativa local (aproximada)";
            _sourceText.Text = (cached > 0 ? $"{cached} de {_rows.Count} jogos com dados da HowManyFPS consultados nos últimos 7 dias. " : "") +
                               "Clique em BUSCAR FPS REAL para consultar a API (usa a internet).";
            _saveKey.Content = "ATUALIZAR CHAVE";
        }
        else
        {
            _sourceTitle.Text = "Fonte: estimativa local (aproximada)";
            _sourceText.Text = "Calculada a partir do desempenho relativo da sua GPU e CPU, sem usar a internet. Para FPS medido em jogo, peça uma chave da API em info@howmanyfps.com e cole ao lado.";
            _saveKey.Content = "SALVAR CHAVE";
        }
    }

    private async Task FetchAsync(string res, string up, CancellationToken ct)
    {
        string cpu = GameCatalog.ShortCpuName(_cpuName), gpu = GameCatalog.ShortGpuName(_gpuName);
        int ok = 0;
        foreach (var row in _rows)
        {
            if (ct.IsCancellationRequested) return;
            try
            {
                var r = await _api.GetAsync(cpu, gpu, row.Name, res, up, ct);
                row.Set(r.AvgFps, r.Min1Fps, "HOWMANYFPS", "");
                ok++;
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex)
            {
                row.Set(row.Fps, 0, "ESTIMATIVA", $"API: {ex.Message}");
                if (ex.Message.Contains("chave") || ex.Message.Contains("cota"))
                {
                    _sourceText.Text = $"A API recusou a consulta ({ex.Message}). Mostrando a estimativa local.";
                    return;
                }
            }
        }
        if (!ct.IsCancellationRequested) _sourceText.Text = $"{ok} de {_rows.Count} jogos com dados da HowManyFPS; os demais usam a estimativa local.";
    }
}
