using Avalonia.Controls;

namespace InfoPC.Linux;

/// <summary>Sistema, placa-mãe, processador, memória, vídeo e armazenamento, com relatório exportável.</summary>
public sealed class SystemPage : PageBase
{
    public override string Title => "Sistema";
    public override string Subtitle => "Tudo o que o Linux informa sobre este computador. Números de série não entram no relatório.";

    private List<InfoNode> _roots = new();
    private int _selected;
    private readonly Border _treeHost = new();
    private readonly WrapPanel _chips = new();
    private readonly StatusText _status = new();
    private readonly Button _ramBtn;
    private string? _lastReport;

    public SystemPage()
    {
        _ramBtn = Ui.Outline("LER MÓDULOS DE RAM (pede senha)", ReadRam, "O SMBIOS só o administrador lê. Usa o dmidecode, pelo pkexec.");
        var buttons = Ui.Wrap(
            Ui.Primary("ATUALIZAR", Reload).WithMargin(0, 0, 8, 8),
            _ramBtn.WithMargin(0, 0, 8, 8),
            Ui.Outline("SALVAR RELATÓRIO (HTML)", () => Export(false)).WithMargin(0, 0, 8, 8),
            Ui.Outline("SALVAR JSON", () => Export(true)).WithMargin(0, 0, 8, 8),
            Ui.Outline("ABRIR RELATÓRIO", OpenLast).WithMargin(0, 0, 8, 8));
        Body.Children.Add(buttons);
        Body.Children.Add(_status);
        Body.Children.Add(_chips);
        _treeHost.Classes.Add("card");
        Body.Children.Add(_treeHost);
        _treeHost.Child = Ui.Muted("Lendo o hardware…");
    }

    public override void OnShown()
    {
        S.Loaded -= Reload; S.Loaded += Reload;
        if (_roots.Count == 0) Reload();
    }

    public override void OnHidden() => S.Loaded -= Reload;

    private async void Reload()
    {
        _status.Text = "";
        var roots = await Task.Run(() => SystemInfoCollector.Collect(S.Ram, S.Gpus, S.Disks));
        _roots = roots;
        _selected = Math.Clamp(_selected, 0, Math.Max(0, roots.Count - 1));
        BuildChips();
        ShowSelected();
        if (S.Ram != null) _ramBtn.Content = "RELER MÓDULOS DE RAM (pede senha)";
    }

    private void BuildChips()
    {
        _chips.Children.Clear();
        var chips = Ui.Chips("sysgrp", _roots.Select(r => r.Header).ToList(), _selected, i => { _selected = i; ShowSelected(); });
        _chips.Children.Add(chips);
    }

    private void ShowSelected()
    {
        if (_roots.Count == 0) { _treeHost.Child = Ui.Muted("Sem dados."); return; }
        var node = _roots[_selected];
        _treeHost.Child = Ui.InfoTree(node.Children.Count > 0 ? node.Children : new List<InfoNode> { new() { Header = "Sem dados" } });
    }

    private async void ReadRam()
    {
        _ramBtn.IsEnabled = false;
        _status.Text = "Aguardando a senha de administrador…";
        var (layout, res) = await Task.Run(() => { var l = RamModules.ReadWithPrompt(out var r); return (l, r); });
        _ramBtn.IsEnabled = true;
        if (layout == null) { _status.Text = "Módulos de RAM: " + res.Message; return; }
        S.Ram = layout;
        _status.Text = $"{layout.Modules.Count(m => m.Populated)} módulo(s) lido(s).";
        _selected = Math.Max(0, _roots.FindIndex(r => r.Header == "Memória RAM"));
        Reload();
    }

    private async void Export(bool json)
    {
        _status.Text = "Gerando o relatório…";
        try
        {
            string file = await Task.Run(() => Reports.Save(json, null));
            _lastReport = file;
            _status.Text = "Relatório salvo em " + file;
        }
        catch (Exception ex) { _status.Text = "Não consegui salvar o relatório: " + ex.Message; }
    }

    private void OpenLast()
    {
        if (_lastReport == null) { _status.Text = "Salve um relatório primeiro."; return; }
        if (!Shell.Open(_lastReport)) _status.Text = "Não achei o xdg-open para abrir o arquivo. Ele está em " + _lastReport;
    }
}
