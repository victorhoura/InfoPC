using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace InfoPC.Linux;

/// <summary>Inicialização: programas que abrem com a sua sessão (autostart do desktop e serviços systemd da sua conta). Desativar e remover têm desfazer.</summary>
public sealed class StartupPage : PageBase
{
    public override string Title => "Inicialização";
    public override string Subtitle => "O que abre sozinho quando você entra no computador. Desativar não apaga nada; remover guarda uma cópia para restaurar.";

    private readonly StackPanel _list = new() { Spacing = 6 };
    private readonly StatusText _status = new();
    private readonly Button _restore;
    private bool _loading;

    public StartupPage()
    {
        _restore = Ui.Outline("RESTAURAR REMOVIDOS", Restore);
        Body.Children.Add(Ui.Wrap(Ui.Primary("ATUALIZAR", Load).WithMargin(0, 0, 8, 8), _restore.WithMargin(0, 0, 8, 8),
                                  Ui.Outline("ABRIR PASTA DO AUTOSTART", () => Shell.Open(Sys.Join(Sys.Home, ".config/autostart"))).WithMargin(0, 0, 8, 8)));
        Body.Children.Add(_status);
        Body.Children.Add(_list);
    }

    public override void OnShown() => Load();

    private async void Load()
    {
        var entries = await Task.Run(() => StartupManager.List(S.Settings));
        _loading = true;
        _list.Children.Clear();
        _restore.Content = S.Settings.StartupRemoved.Count > 0 ? $"RESTAURAR REMOVIDOS ({S.Settings.StartupRemoved.Count})" : "RESTAURAR REMOVIDOS";
        _restore.IsEnabled = S.Settings.StartupRemoved.Count > 0;
        if (entries.Count == 0) _list.Children.Add(Ui.Card(Ui.Muted("Nenhum programa de inicialização encontrado em ~/.config/autostart nem /etc/xdg/autostart.")));
        foreach (var e in entries)
        {
            var sw = new ToggleSwitch { IsChecked = e.Enabled, OnContent = "", OffContent = "", VerticalAlignment = VerticalAlignment.Center };
            var entry = e;
            sw.IsCheckedChanged += (_, _) =>
            {
                if (_loading) return;
                bool on = sw.IsChecked == true;
                string? err = StartupManager.SetEnabled(entry, on, S.Settings, S.Settings.Save); // Settings.Save avisa a falha; S.Save a engoliria
                _status.Text = err ?? $"{entry.Name}: {(on ? "vai abrir com a sessão" : "não abre mais com a sessão")}.";
                if (err != null) { _loading = true; sw.IsChecked = !on; _loading = false; }
            };
            var info = Ui.VStack(1, new TextBlock { Text = e.Name, FontSize = 14, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis },
                                 Ui.Muted(e.Comment.Length > 0 ? e.Comment : e.Command).Cls("label"), Ui.Muted(e.Source + " · " + e.File).Cls("label"));
            var remove = Ui.Outline("REMOVER", () => RemoveEntry(entry)).Small();
            remove.VerticalAlignment = VerticalAlignment.Center;
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("64,*,Auto"), ColumnSpacing = 8 };
            Grid.SetColumn(info, 1); Grid.SetColumn(remove, 2);
            row.Children.Add(sw); row.Children.Add(info); row.Children.Add(remove);
            _list.Children.Add(Ui.Card(row, null, 12));
        }
        _loading = false;
    }

    private async void RemoveEntry(StartupEntry e)
    {
        string msg = e.System ? $"\"{e.Name}\" é do sistema (/etc/xdg/autostart). O InfoPC não mexe no arquivo do sistema: cria uma cópia na sua conta que o desativa. Continuar?"
                              : $"Remover \"{e.Name}\" da inicialização? Uma cópia fica guardada para você restaurar.";
        if (!await Dialogs.ConfirmAsync(this, "Remover da inicialização", msg, "REMOVER")) return;
        string? err = StartupManager.Remove(e, S.Settings, S.Settings.Save);
        S.Save();
        _status.Text = err ?? $"{e.Name} removido da inicialização.";
        Load();
    }

    private void Restore()
    {
        string? err = StartupManager.RestoreRemoved(S.Settings);
        string saveError = "";
        try { S.Settings.Save(); } // se falhar, restaurar de novo só regrava o que já voltou
        catch (Exception ex) { saveError = $" Não consegui gravar as configurações ({ex.Message})."; }
        _status.Text = (err ?? "Programas restaurados.") + saveError;
        Load();
    }
}
