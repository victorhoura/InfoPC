using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace InfoPC.Linux;

/// <summary>Otimização: fecha programas em segundo plano que você escolher, libera o cache e aplica ajustes reversíveis para jogos (valem até reiniciar).</summary>
public sealed class OptimizePage : PageBase
{
    public override string Title => "Otimização";
    public override string Subtitle => "Libera recursos antes de jogar. Tudo é por clique seu, nada é fechado ou alterado sozinho, e os ajustes têm \"Restaurar originais\".";

    private readonly StackPanel _apps = new() { Spacing = 4 };
    private readonly CheckBox _dropCache = new() { Content = "Liberar também o cache de arquivos (pede a senha de administrador)" };
    private readonly StatusText _status = new();
    private readonly Button _go;
    private readonly StackPanel _tweaksBox = new() { Spacing = 10 };
    private readonly StatusText _tweakStatus = new();
    private List<Tweak> _tweaks = new();
    private List<BackgroundApp> _list = new();
    private bool _loading;

    public OptimizePage()
    {
        _go = Ui.Primary("OTIMIZAR", RunOptimize);
        var optimize = Ui.VStack(14,
            Ui.Note("Marque o que pode ser fechado. O programa pede para cada um fechar (como o botão de fechar da janela: ele salva e sai) e não força nada. As sugestões vêm marcadas: sincronizadores, indexadores e mensageiros que consomem disco e rede. Só aparecem programas da sua conta; sistema, desktop, Steam, terminais e o próprio InfoPC ficam de fora."),
            _apps, _dropCache,
            Ui.Note("O Linux usa a RAM livre como cache de arquivos e a devolve sozinho quando um programa precisa: liberar o cache raramente dá ganho, mas ajuda a medir benchmarks de disco com o cache frio."),
            Ui.Wrap(_go.WithMargin(0, 0, 8, 0), Ui.Outline("ATUALIZAR LISTA", LoadApps)), _status);
        Body.Children.Add(Ui.Card(optimize, "Programas em segundo plano"));

        var maint = Ui.VStack(10,
            Ui.Note("O TRIM avisa o SSD de quais blocos estão livres, o que mantém a escrita rápida. A maioria das distribuições já faz isso toda semana (fstrim.timer); este botão faz agora."),
            Ui.Outline("EXECUTAR TRIM (pede senha)", RunTrim), Ui.Muted(Shell.Has("fstrim") ? "" : "O fstrim não está instalado (pacote util-linux)."));
        Body.Children.Add(Ui.Card(maint, "Manutenção do SSD"));

        Body.Children.Add(Ui.Card(Ui.VStack(14, Ui.Note("Ajustes do kernel para jogos. Cada um mostra o que muda e pede a senha ao ligar ou desligar (o perfil de CPU não precisa, quando há o power-profiles-daemon). Valem até reiniciar o PC: nada é gravado em /etc."),
            _tweaksBox, Ui.HStack(10, Ui.Outline("RESTAURAR ORIGINAIS", RestoreAll)), _tweakStatus), "Ajustes para jogos"));
    }

    public override void OnShown() { LoadApps(); LoadTweaks(); }

    private async void LoadApps()
    {
        _list = await Task.Run(() => Optimizer.ListApps());
        _apps.Children.Clear();
        if (_list.Count == 0) { _apps.Children.Add(Ui.Muted("Nenhum programa em segundo plano relevante.")); return; }
        foreach (var a in _list)
        {
            var cb = new CheckBox { IsChecked = a.Selected };
            cb.IsCheckedChanged += (_, _) => a.Selected = cb.IsChecked == true;
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("32,*,90") };
            var text = Ui.VStack(0, new TextBlock { Text = a.Title + (a.Count > 1 ? $"  ×{a.Count}" : ""), FontSize = 13, FontWeight = a.Suggested ? FontWeight.SemiBold : FontWeight.Normal },
                                 Ui.Muted(a.Detail).Cls("label"));
            var mem = Ui.Muted(a.MemoryText).Cls("mono"); mem.TextAlignment = TextAlignment.Right; mem.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(text, 1); Grid.SetColumn(mem, 2);
            row.Children.Add(cb); row.Children.Add(text); row.Children.Add(mem);
            _apps.Children.Add(row);
        }
    }

    private async void RunOptimize()
    {
        var selected = _list.Where(a => a.Selected).ToList();
        bool cache = _dropCache.IsChecked == true;
        if (selected.Count == 0 && !cache) { _status.Text = "Marque pelo menos um programa ou a opção do cache."; return; }
        _go.IsEnabled = false;
        _status.Text = "Otimizando…";
        long before = (MemInfo.Read()?.AvailableKb ?? 0) * 1024;
        var parts = new List<string>();
        if (selected.Count > 0)
        {
            var r = await Optimizer.CloseAsync(selected);
            if (r.Closed.Count > 0) parts.Add("Fechados: " + string.Join(", ", r.Closed) + ".");
            if (r.NotClosed.Count > 0) parts.Add("Não fecharam (o programa ignorou o pedido; feche pela janela): " + string.Join(", ", r.NotClosed) + ".");
        }
        if (cache)
        {
            var (res, _) = await Task.Run(Optimizer.DropCaches);
            parts.Add(res.Ok ? "Cache de arquivos liberado." : "Cache: " + res.Message);
        }
        long after = (MemInfo.Read()?.AvailableKb ?? 0) * 1024;
        parts.Add($"RAM disponível: {Fmt.Bytes(before)} → {Fmt.Bytes(after)}" + (after > before ? $" (+{Fmt.Bytes(after - before)})." : "."));
        _status.Text = string.Join(" ", parts);
        _go.IsEnabled = true;
        LoadApps();
    }

    private async void RunTrim()
    {
        _tweakStatus.Text = "";
        _status.Text = "Executando o TRIM… (pode levar alguns minutos)";
        var r = await Task.Run(Optimizer.Trim);
        _status.Text = r.Ok ? "TRIM concluído. " + r.Output.Trim().Replace("\n", " · ") : "TRIM: " + r.Message;
    }

    // ---------------- ajustes ----------------

    private void LoadTweaks()
    {
        _tweaks = LinuxTweaks.All().Where(t => t.Available()).ToList();
        RebuildTweaks();
    }

    private async void RebuildTweaks()
    {
        _loading = true;
        await Task.Run(() => { foreach (var t in _tweaks) t.Refresh(); });
        _tweaksBox.Children.Clear();
        if (_tweaks.Count == 0) _tweaksBox.Children.Add(Ui.Muted("Nenhum ajuste disponível neste kernel."));
        foreach (var t in _tweaks)
        {
            var sw = new ToggleSwitch { IsChecked = t.IsOn, OnContent = "", OffContent = "" };
            var tt = t;
            sw.IsCheckedChanged += async (_, _) =>
            {
                if (_loading) return;
                bool on = sw.IsChecked == true;
                sw.IsEnabled = false;
                _tweakStatus.Text = "Aplicando… (a senha de administrador pode ser pedida)";
                // o original vai para o disco antes do sysctl (Settings.Save avisa a falha; S.Save a engoliria)
                var r = await Task.Run(() => LinuxTweaks.Apply(tt, on, S.Settings, S.Settings.Save));
                S.Save();
                _tweakStatus.Text = r.Ok ? $"{tt.Title}: {(on ? "ligado" : "desligado")}." : "Não consegui: " + r.Message;
                if (!r.Ok) { _loading = true; sw.IsChecked = !on; _loading = false; }
                sw.IsEnabled = true;
            };
            var head = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            var title = Ui.VStack(2, new TextBlock { Text = t.Title, FontSize = 14, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap }, Ui.Text(t.Description).Cls("sub"),
                                  Ui.Text("Efeito: " + t.Effect).Cls("sub"));
            Grid.SetColumn(sw, 1);
            sw.VerticalAlignment = VerticalAlignment.Top;
            head.Children.Add(title); head.Children.Add(sw);
            _tweaksBox.Children.Add(head);
            _tweaksBox.Children.Add(Ui.Separator());
        }
        _loading = false;
    }

    private async void RestoreAll()
    {
        _tweakStatus.Text = "Restaurando… (a senha de administrador pode ser pedida)";
        var r = await Task.Run(() => LinuxTweaks.RestoreAll(_tweaks, S.Settings));
        S.Save();
        _tweakStatus.Text = r.Ok ? "Valores originais restaurados." : "Não consegui restaurar tudo: " + r.Message;
        RebuildTweaks();
    }
}
