using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;

namespace InfoPC.Linux;

/// <summary>Biblioteca: jogos da Steam, Lutris, Heroic e programas manuais, com um perfil por jogo (GameMode, MangoHud, GPU dedicada, janela) e botão JOGAR.</summary>
public sealed class LauncherPage : PageBase
{
    public override string Title => "Biblioteca";
    public override string Subtitle => "Seus jogos instalados e um perfil para cada um. O perfil usa as ferramentas do Linux: GameMode, MangoHud e GPU dedicada.";

    private static readonly (string Label, string Token, string? Hint)[] ExtraPresets =
    {
        ("Pular intro (-novid)", "-novid", null), ("Prioridade alta (-high)", "-high", null), ("Sem limite de FPS (+fps_max 0)", "+fps_max 0", null),
        ("Sem joystick (-nojoy)", "-nojoy", null), ("Abrir console (-console)", "-console", null),
        ("Programas de terceiros (-allow_third_party_software)", "-allow_third_party_software",
         "CS2: libera programas que o modo confiável bloqueia, como a captura de jogo do OBS e alguns overlays.\nAtenção: a Valve diz que usar esta opção pode reduzir o seu Fator de Confiança (Trust Factor) no matchmaking."),
    };

    private List<LibGame> _games = new();
    private readonly ListBox _list = new() { MinHeight = 320, MaxHeight = 560 };
    private readonly TextBox _search = new() { Watermark = "Buscar jogo…" };
    private readonly TextBlock _count = Ui.Muted("");
    private readonly Border _detail = new();
    private readonly Button _scan;
    private LibGame? _selected;
    private CancellationTokenSource? _launchCts;

    public LauncherPage()
    {
        _scan = Ui.Outline("PROCURAR DE NOVO", () => _ = ScanAsync()).Small();
        _search.TextChanged += (_, _) => Filter();
        _list.ItemTemplate = new FuncDataTemplate<LibGame>((g, _) =>
            Ui.VStack(0, new TextBlock { Text = g.Name, FontWeight = FontWeight.SemiBold, FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis },
                      Ui.Muted(g.Subtitle).Cls("label")), true);
        _list.SelectionChanged += (_, _) => Select(_list.SelectedItem as LibGame);

        var left = Ui.VStack(10, _search, Ui.Wrap(_scan.WithMargin(0, 0, 6, 6), Ui.Outline("+ ADICIONAR PROGRAMA", AddProgram).Small().WithMargin(0, 0, 0, 6)), _count, _list);
        var leftCard = Ui.Card(left, "Jogos", 16);
        leftCard.VerticalAlignment = VerticalAlignment.Top;
        _detail.Child = Ui.Card(Ui.Muted("Escolha um jogo na lista para ver e ajustar o perfil."));

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("320,*"), ColumnSpacing = 16 };
        Grid.SetColumn(_detail, 1);
        grid.Children.Add(leftCard); grid.Children.Add(_detail);
        Body.Children.Add(grid);
    }

    public override void OnShown() { if (_games.Count == 0) _ = ScanAsync(); }

    private async Task ScanAsync()
    {
        _scan.IsEnabled = false;
        _count.Text = "Procurando seus jogos…";
        try
        {
            var custom = S.Settings.CustomGames.ToList();
            _games = await Task.Run(() => GameLibrary.Scan(custom));
            Filter();
            var byStore = _games.GroupBy(g => g.Store).Select(x => $"{x.Key} {x.Count()}");
            _count.Text = _games.Count == 0
                ? "Nenhum jogo encontrado. Use + ADICIONAR PROGRAMA para incluir um à mão."
                : $"{_games.Count} jogos · {string.Join(" · ", byStore)}";
        }
        catch (Exception ex) { _count.Text = "Falhou: " + ex.Message; }
        finally { _scan.IsEnabled = true; }
    }

    private void Filter()
    {
        string q = (_search.Text ?? "").Trim();
        string? keep = _selected?.Key;
        var list = _games.Where(g => q.Length == 0 || g.Name.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
        _list.ItemsSource = list;
        if (keep != null) _list.SelectedItem = list.FirstOrDefault(g => g.Key == keep);
    }

    private async void AddProgram()
    {
        string? path = await Dialogs.PickFileAsync(this, "Escolha o jogo ou programa (executável, script, AppImage ou .exe do Windows)");
        if (path == null) return;
        if (!File.Exists(path)) { _count.Text = "Arquivo não encontrado: " + path; return; }
        if (!S.Settings.CustomGames.Any(c => c.Exe == path))
        {
            S.Settings.CustomGames.Add(new CustomGame { Name = Path.GetFileNameWithoutExtension(path), Exe = path });
            S.Save();
        }
        await ScanAsync();
        _list.SelectedItem = _games.FirstOrDefault(g => g.Store == "Manual" && g.Id == path);
    }

    // ---------------- perfil ----------------

    private void Select(LibGame? g)
    {
        _selected = g;
        if (g == null) return;
        if (!S.Settings.GameProfiles.TryGetValue(g.Key, out var p)) { p = new GameProfile(); S.Settings.GameProfiles[g.Key] = p; }
        _detail.Child = BuildDetail(g, p);
    }

    private void SaveProfile() => S.Save();

    private Control BuildDetail(LibGame g, GameProfile p)
    {
        var status = new StatusText();
        var steamNow = new StatusText();
        var preview = Ui.Muted("").Cls("mono");
        preview.FontSize = 12;
        void UpdatePreview()
        {
            if (!g.IsSteam) { preview.Text = ""; return; }
            preview.Text = GameLauncher.BuildSteamOptions(p, S.Gpus);
        }

        var perf = Ui.VStack(8);
        perf.Children.Add(Check("GameMode (prioridade e governador de CPU durante o jogo)", p.GameMode, v => { p.GameMode = v; SaveProfile(); UpdatePreview(); },
                                Shell.Has("gamemoderun") ? null : "O GameMode não está instalado: instale na página Instalar apps."));
        perf.Children.Add(Check("MangoHud (FPS, temperatura e uso dentro do jogo)", p.MangoHud, v => { p.MangoHud = v; SaveProfile(); UpdatePreview(); },
                                Shell.Has("mangohud") ? null : "O MangoHud não está instalado: instale na página Instalar apps."));
        bool twoGpus = S.Gpus.Count(x => !x.IsVirtual) > 1;
        perf.Children.Add(Check("Usar a GPU dedicada (notebooks e PCs com duas placas)", p.DedicatedGpu, v => { p.DedicatedGpu = v; SaveProfile(); UpdatePreview(); },
                                twoGpus ? null : "Só há uma GPU neste computador: a opção não muda nada."));
        if (g.Windows) perf.Children.Add(Check("Proton: usar Wayland (experimental)", p.ProtonWayland, v => { p.ProtonWayland = v; SaveProfile(); UpdatePreview(); }, null));

        var gfx = Ui.VStack(8,
            Ui.Label("Janela"), Chips("lmode", new[] { "Padrão do jogo", "Tela cheia", "Sem bordas", "Janela" }, new[] { "default", "full", "borderless", "window" }, p.WinMode, v => { p.WinMode = v; SaveProfile(); UpdatePreview(); }),
            Ui.Label("Resolução"), Chips("lres", new[] { "Padrão", "1280x720", "1920x1080", "2560x1440", "3840x2160" }, new[] { "default", "1280x720", "1920x1080", "2560x1440", "3840x2160" }, p.Res, v => { p.Res = v; SaveProfile(); UpdatePreview(); }),
            Ui.Label("Taxa de atualização (Hz)"), Chips("lhz", new[] { "Padrão", "60", "120", "144", "165", "240" }, new[] { "0", "60", "120", "144", "165", "240" }, p.Hz.ToString(), v => { p.Hz = int.Parse(v); SaveProfile(); UpdatePreview(); }),
            Ui.Label("Estilo dos argumentos (engine)"), Chips("lstyle", new[] { "Source / CS2", "Unity", "Unreal" }, new[] { "source", "unity", "unreal" }, p.Style, v => { p.Style = v; SaveProfile(); UpdatePreview(); }));

        var extra = new TextBox { Text = p.Extra, Watermark = "ex.: -novid -high +fps_max 0" };
        extra.TextChanged += (_, _) => { p.Extra = extra.Text ?? ""; SaveProfile(); UpdatePreview(); };
        var presets = new WrapPanel();
        foreach (var (label, token, hint) in ExtraPresets)
        {
            var b = Ui.Outline(label, () => { if (!(extra.Text ?? "").Contains(token, StringComparison.OrdinalIgnoreCase)) extra.Text = ((extra.Text ?? "").Trim() + " " + token).Trim(); }).Small();
            b.Margin = new Thickness(0, 0, 6, 6);
            if (hint != null) ToolTip.SetTip(b, hint);
            presets.Children.Add(b);
        }

        // executável (jogos que não são de loja)
        var exeRow = new StackPanel { Spacing = 6 };
        if (!g.IsSteam && g.LaunchUri == null)
        {
            var exe = new TextBox { Text = p.ExePath ?? g.ExePath ?? "", Watermark = "caminho do executável" };
            exe.TextChanged += (_, _) => { p.ExePath = string.IsNullOrWhiteSpace(exe.Text) ? null : exe.Text.Trim(); SaveProfile(); };
            exeRow.Children.Add(Ui.Label("Executável"));
            exeRow.Children.Add(exe);
            exeRow.Children.Add(Ui.Outline("ESCOLHER…", async () => { var f = await Dialogs.PickFileAsync(this, "Executável do jogo"); if (f != null) exe.Text = f; }).Small());
        }

        // programas auxiliares
        var helpers = new StackPanel { Spacing = 6 };
        void RebuildHelpers()
        {
            helpers.Children.Clear();
            foreach (var h in p.Helpers.ToList())
            {
                var hh = h;
                var close = new CheckBox { Content = "fechar junto com o jogo", IsChecked = hh.CloseWithGame, FontSize = 12 };
                close.IsCheckedChanged += (_, _) => { hh.CloseWithGame = close.IsChecked == true; SaveProfile(); };
                helpers.Children.Add(Ui.HStack(12, new TextBlock { Text = hh.Display, FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center }, close,
                                               Ui.Outline("REMOVER", () => { p.Helpers.Remove(hh); SaveProfile(); RebuildHelpers(); }).Small()));
            }
            helpers.Children.Add(Ui.Outline("+ PROGRAMA AUXILIAR", async () =>
            {
                var f = await Dialogs.PickFileAsync(this, "Programa que deve abrir antes do jogo");
                if (f == null) return;
                p.Helpers.Add(new HelperProgram { Path = f });
                SaveProfile(); RebuildHelpers();
            }).Small());
        }
        RebuildHelpers();

        // opções da Steam
        Control steamBox = new StackPanel();
        if (g.IsSteam)
        {
            string? cur = SteamInfo.GetLaunchOptions(g.Id);
            steamNow.Text = "Opções atuais na Steam: " + (string.IsNullOrWhiteSpace(cur) ? "(nenhuma)" : cur);
            Button? restore = null;
            var apply = Ui.Outline("APLICAR NAS OPÇÕES DA STEAM", async () => { await ApplySteam(g, p, steamNow, status); restore!.IsEnabled = p.SteamOriginals.Count > 0; });
            restore = Ui.Outline("RESTAURAR ORIGINAL", async () => { await RestoreSteam(g, p, steamNow, status); restore!.IsEnabled = p.SteamOriginals.Count > 0; },
                                 "Devolve as opções que o jogo tinha na Steam antes do primeiro APLICAR");
            restore.IsEnabled = p.SteamOriginals.Count > 0;
            steamBox = Ui.VStack(8, Ui.Label("Opções de inicialização da Steam"), steamNow, Ui.Muted("O que será gravado:"), preview, Ui.HStack(10, apply, restore),
                                 Ui.Note("A Steam guarda as opções no localconfig.vdf e sobrescreve o arquivo ao sair, por isso o programa pede para fechá-la antes de gravar (com cópia de segurança .infopc.bak). Depois, o JOGAR abre pela Steam normalmente."));
        }
        UpdatePreview();

        var play = Ui.Primary("JOGAR", () => Play(g, p, status));
        var stop = Ui.Outline("PARAR DE ACOMPANHAR", () => _launchCts?.Cancel());

        var note = g.IsSteam ? "Os argumentos só passam a valer na Steam depois de APLICAR NAS OPÇÕES DA STEAM."
                 : g.LaunchUri != null ? $"Esta loja ({g.Store}) abre o jogo por um atalho que não aceita argumentos: use as opções de inicialização do próprio programa. Os programas auxiliares funcionam normalmente."
                 : "Os argumentos são enviados direto ao programa quando você clica em JOGAR.";

        var head = Ui.VStack(2, new TextBlock { Text = g.Name, FontSize = 20, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap },
                             Ui.Muted(g.Subtitle + (g.InstallDir.Length > 0 ? " · " + g.InstallDir : "")).Cls("mono"));
        ((TextBlock)((StackPanel)head).Children[1]).FontSize = 11.5;
        var body = Ui.VStack(18, head, Ui.HStack(10, play, stop), status,
                             Ui.Card(perf, "Desempenho"), Ui.Card(Ui.VStack(10, gfx, Ui.Label("Argumentos extras"), extra, presets, Ui.Note(note)), "Gráficos e argumentos"),
                             exeRow, Ui.Card(helpers, "Programas auxiliares"), steamBox);
        return body;
    }

    private static Control Check(string text, bool value, Action<bool> changed, string? warn)
    {
        var cb = new CheckBox { Content = text, IsChecked = value };
        cb.IsCheckedChanged += (_, _) => changed(cb.IsChecked == true);
        if (warn == null) return cb;
        return Ui.VStack(2, cb, Ui.Muted(warn).WithMargin(30, 0, 0, 0).Cls("label"));
    }

    private static Control Chips(string group, string[] labels, string[] values, string current, Action<string> changed)
    {
        int sel = Math.Max(0, Array.IndexOf(values, current));
        return Ui.Chips(group, labels, sel, i => changed(values[i]));
    }

    /// <summary>Fecha a Steam, se estiver aberta, depois de confirmar (ela sobrescreve o localconfig.vdf ao sair). False se não fechou.</summary>
    private async Task<bool> CloseSteamAsync(StatusText status)
    {
        if (!SteamInfo.IsRunning) return true;
        if (!await Dialogs.ConfirmAsync(this, "Fechar a Steam", "A Steam precisa ser fechada para gravar as opções (ela sobrescreve o arquivo ao sair). Salve o que estiver fazendo nela. Fechar agora?", "FECHAR A STEAM")) return false;
        status.Text = "Fechando a Steam…";
        if (!await SteamInfo.ShutdownAsync()) { status.Text = "A Steam não fechou. Feche-a manualmente e tente de novo."; return false; }
        return true;
    }

    private async Task ApplySteam(LibGame g, GameProfile p, StatusText steamNow, StatusText status)
    {
        string options = GameLauncher.BuildSteamOptions(p, S.Gpus);
        if (!await CloseSteamAsync(status)) return;
        // o original vai para o disco antes de a Steam ser alterada (Settings.Save avisa a falha; S.Save a engoliria)
        string? err = SteamInfo.ApplyLaunchOptions(g.Id, options, p, SteamInfo.IsRunning, S.Settings.Save);
        status.Text = err ?? "Opções gravadas na Steam. Abra a Steam e clique em JOGAR.";
        steamNow.Text = "Opções atuais na Steam: " + (SteamInfo.GetLaunchOptions(g.Id) is { Length: > 0 } cur ? cur : "(nenhuma)");
    }

    private async Task RestoreSteam(LibGame g, GameProfile p, StatusText steamNow, StatusText status)
    {
        if (p.SteamOriginals.Count == 0) return;
        var origs = p.SteamOriginals.Values.ToList();
        if (!await CloseSteamAsync(status)) return;
        string? err = SteamInfo.RestoreLaunchOptions(g.Id, p, SteamInfo.IsRunning, S.Settings.Save);
        status.Text = err ?? (origs.Count > 1 ? $"Opções originais devolvidas à Steam nas {origs.Count} contas em que o InfoPC as trocou."
                             : origs[0].Length > 0 ? $"Opções originais devolvidas à Steam: {origs[0]}" : "Opções da Steam limpas: o jogo não tinha nenhuma antes do InfoPC.");
        steamNow.Text = "Opções atuais na Steam: " + (SteamInfo.GetLaunchOptions(g.Id) is { Length: > 0 } cur ? cur : "(nenhuma)");
    }

    private async void Play(LibGame g, GameProfile p, StatusText status)
    {
        if (_launchCts != null) { status.Text = "Já estou acompanhando um jogo."; return; }
        _launchCts = new CancellationTokenSource();
        try
        {
            await GameLauncher.RunAsync(g, p, S.Gpus, t => Post(() => status.Text = t), exe => { }, _launchCts.Token);
        }
        finally { _launchCts = null; }
    }
}
