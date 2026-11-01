using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace InfoPC.Linux;

/// <summary>Discos físicos: unidades montadas com uso, saúde pelo S.M.A.R.T. (lido sob demanda, com senha) e temperatura.</summary>
public sealed class DisksPage : PageBase
{
    public override string Title => "Discos";
    public override string Subtitle => "Unidades, uso e saúde. O S.M.A.R.T. só o administrador lê, então o programa pede a senha uma única vez, ao clicar em LER S.M.A.R.T.";

    private readonly StackPanel _list = new() { Spacing = 14 };
    private readonly StatusText _status = new();
    private readonly Button _smartBtn;
    private List<MountInfo> _orphans = new();

    public DisksPage()
    {
        var toasts = new CheckBox { Content = "Avisar no canto da tela quando um pendrive, HD externo ou dispositivo USB for conectado ou removido", IsChecked = S.Settings.DeviceToasts };
        toasts.IsCheckedChanged += (_, _) => { S.Settings.DeviceToasts = toasts.IsChecked == true; S.Save(); };
        Body.Children.Add(toasts);
        _smartBtn = Ui.Primary("LER S.M.A.R.T. (pede senha)", ReadSmart, "Roda o smartctl como administrador, uma vez, para todos os discos. Nada é gravado nos discos.");
        Body.Children.Add(Ui.Wrap(_smartBtn.WithMargin(0, 0, 8, 8), Ui.Outline("ATUALIZAR", Reload).WithMargin(0, 0, 8, 8)));
        Body.Children.Add(_status);
        Body.Children.Add(_list);
    }

    public override void OnShown() { S.Loaded -= Reload; S.Loaded += Reload; S.DeviceChanged -= OnDevice; S.DeviceChanged += OnDevice; Reload(); }
    public override void OnHidden() { S.Loaded -= Reload; S.DeviceChanged -= OnDevice; }

    /// <summary>O pendrive foi plugado ou removido: relê a lista (dá um instante para o sistema montar a unidade).</summary>
    private async void OnDevice(DeviceChange c) { await Task.Delay(1500); Reload(); }

    private async void Reload()
    {
        if (!S.Ready) { _list.Children.Clear(); _list.Children.Add(Ui.Muted("Detectando o hardware…")); return; }
        try
        {
            await S.RefreshDisksAsync();
            // statfs de uma unidade de rede caída espera indefinidamente: nunca na thread da janela
            _orphans = await Task.Run(() => DiskCollector.Mounts().Where(m => m.Disk == null && m.TotalBytes > 0).ToList());
        }
        catch (Exception ex)
        {
            CrashLog.Write("Discos", ex);
            _status.Text = "Não consegui reler as unidades: " + ex.Message;
        }
        Rebuild();
    }

    private void Rebuild()
    {
        _list.Children.Clear();
        var cards = DiskHealthEvaluator.Cards(S.Disks, S.Smart);
        for (int i = 0; i < cards.Count; i++)
        {
            var d = i < S.Disks.Count ? S.Disks[i] : null;
            var extras = new List<Control>();
            if (d != null)
                foreach (var m in d.Mounts.Where(m => m.TotalBytes > 0))
                {
                    var head = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
                    var name = new TextBlock { Text = $"{m.MountPoint}  ·  {m.FsType}", FontSize = 13 };
                    var use = Ui.Muted($"{Fmt.Bytes(m.FreeBytes)} livres de {Fmt.Bytes(m.TotalBytes)} · {m.UsedPct:0}% usado"); use.FontSize = 12;
                    Grid.SetColumn(use, 1);
                    head.Children.Add(name); head.Children.Add(use);
                    extras.Add(Ui.VStack(5, head, Ui.Bar(m.UsedPct)));
                }
            _list.Children.Add(HealthCardView.Create(cards[i], extras.ToArray()));
        }
        // montagens sem disco físico (rede, LVM sem mapa, sistemas virtuais de arquivos reais)
        if (_orphans.Count > 0)
        {
            var rows = new StackPanel { Spacing = 10 };
            foreach (var m in _orphans)
            {
                var head = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
                var name = new TextBlock { Text = $"{m.MountPoint}  ·  {m.FsType}  ·  {m.Device}", FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis };
                var use = Ui.Muted($"{Fmt.Bytes(m.FreeBytes)} livres de {Fmt.Bytes(m.TotalBytes)}"); use.FontSize = 12;
                Grid.SetColumn(use, 1);
                head.Children.Add(name); head.Children.Add(use);
                rows.Children.Add(Ui.VStack(5, head, Ui.Bar(m.UsedPct)));
            }
            _list.Children.Add(Ui.Card(rows, "Outras unidades (rede, volumes lógicos)"));
        }
    }

    private async void ReadSmart()
    {
        _smartBtn.IsEnabled = false;
        _status.Text = "Aguardando a senha de administrador…";
        var (infos, res) = await Task.Run(() => SmartReader.ReadAll(S.Disks));
        _smartBtn.IsEnabled = true;
        if (!res.Ok) { _status.Text = "S.M.A.R.T.: " + res.Message; return; }
        S.Smart = infos;
        _status.Text = infos.Count == 0 ? "O smartctl não devolveu dados." : $"S.M.A.R.T. lido de {infos.Count} disco(s) às {DateTime.Now:HH:mm:ss}.";
        Rebuild();
    }
}
