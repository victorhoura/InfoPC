using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;

namespace InfoPC.Linux;

/// <summary>Instalar apps: jogos, ferramentas de diagnóstico e Maker. Usa o gerenciador da sua distribuição (uma senha só) ou o Flatpak (sem senha).</summary>
public sealed class AppsPage : PageBase
{
    public override string Title => "Instalar apps";
    public override string Subtitle => _sub;
    private string _sub = "Marque o que quer e clique em INSTALAR. Os pacotes vêm dos repositórios oficiais da sua distribuição ou do Flathub.";

    private readonly StackPanel _list = new() { Spacing = 6 };
    private readonly StatusText _status = new();
    private readonly TextBlock _log = new() { FontSize = 11.5, TextWrapping = TextWrapping.Wrap };
    private readonly Border _logCard = new() { IsVisible = false };
    private readonly Button _install;
    private List<InstallableApp> _apps = new();
    private PackageManager _pm;
    private bool _busy;

    public AppsPage()
    {
        _install = Ui.Primary("INSTALAR SELECIONADOS", Install);
        _log.Classes.Add("mono");
        _logCard.Classes.Add("card");
        _logCard.Child = Ui.VStack(6, Ui.Label("Registro"), _log);
        Body.Children.Add(Ui.Wrap(_install.WithMargin(0, 0, 8, 8), Ui.Outline("VERIFICAR DE NOVO", Load).WithMargin(0, 0, 8, 8)));
        Body.Children.Add(_status);
        Body.Children.Add(_list);
        Body.Children.Add(_logCard);
    }

    public override void OnShown() { if (!_busy) Load(); }

    private async void Load()
    {
        _pm = AppInstaller.Detect();
        if (_apps.Count == 0) _apps = AppInstaller.Catalog();
        await Task.Run(() => AppInstaller.Resolve(_apps, _pm));
        _sub = _pm == PackageManager.None
            ? "Não reconheci o gerenciador de pacotes desta distribuição: só o Flatpak está disponível."
            : $"Gerenciador de pacotes: {_pm.ToString().ToLowerInvariant()}" + (AppInstaller.FlatpakAvailable ? " · Flatpak disponível" : " · Flatpak ausente (instale o pacote flatpak para ter mais opções)") + ". Os pacotes vêm dos repositórios oficiais.";
        RaiseHeaderChanged();
        Rebuild();
    }

    private void Rebuild()
    {
        _list.Children.Clear();
        foreach (var a in _apps)
        {
            var cb = new CheckBox { DataContext = a, IsChecked = a.Selected || (a.PreSelect && a.CanSelect && !a.Selected && !_touched.Contains(a.Name)) };
            if (cb.IsChecked == true) a.Selected = true;
            cb.IsCheckedChanged += (_, _) => { _touched.Add(a.Name); a.Selected = cb.IsChecked == true; };
            cb.IsEnabled = a.CanSelect && !_busy;

            var name = new TextBlock { Text = a.Name, FontSize = 14, FontWeight = FontWeight.SemiBold };
            var desc = Ui.Text(a.Description).Cls("sub");
            var src = Ui.Muted(a.Source.Length > 0 ? "de: " + a.Source : "").Cls("label");
            var status = new TextBlock { FontSize = 12, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Right, DataContext = a };
            status[!TextBlock.TextProperty] = new Binding(nameof(InstallableApp.Status));
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("34,*,200"), ColumnSpacing = 8 };
            var text = Ui.VStack(1, name, desc, src);
            Grid.SetColumn(text, 1); Grid.SetColumn(status, 2);
            row.Children.Add(cb); row.Children.Add(text); row.Children.Add(status);
            _list.Children.Add(Ui.Card(row, null, 12));
        }
    }

    private readonly HashSet<string> _touched = new();

    private async void Install()
    {
        var chosen = _apps.Where(a => a.Selected && a.CanSelect).ToList();
        if (chosen.Count == 0) { _status.Text = "Marque pelo menos um app que ainda não esteja instalado."; return; }
        _busy = true; _install.IsEnabled = false;
        _logCard.IsVisible = true; _log.Text = "";
        void Log(string t) => Post(() =>
        {
            string all = _log.Text + t.TrimEnd() + "\n";
            _log.Text = all.Length > 4000 ? all[^4000..] : all;   // só o final do registro
        });
        var native = chosen.Where(a => a.NativePackage(_pm) != null).ToList();
        var flat = chosen.Except(native).Where(a => a.Flatpak != null).ToList();
        int ok = 0, fail = 0;

        if (native.Count > 0)
        {
            _status.Text = "Instalando pacotes do sistema… (a senha de administrador é pedida uma vez)";
            foreach (var a in native) a.Status = "Instalando…";
            var res = await AppInstaller.InstallNativeAsync(native, _pm, Log);
            foreach (var (a, (good, text)) in res) { a.Status = text; if (good) ok++; else fail++; }
        }
        foreach (var a in flat)
        {
            _status.Text = $"Instalando {a.Name} (Flatpak)…";
            a.Status = "Instalando…";
            var (good, text) = await AppInstaller.InstallFlatpakAsync(a, CancellationToken.None, Log);
            a.Status = text;
            if (good) ok++; else fail++;
        }
        _status.Text = $"Concluído: {ok} instalado(s)" + (fail > 0 ? $", {fail} com falha (veja o registro)." : ".");
        _busy = false; _install.IsEnabled = true;
        Load();
    }
}
