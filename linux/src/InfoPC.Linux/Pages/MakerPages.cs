using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace InfoPC.Linux;

/// <summary>Portas seriais: placas Arduino, ESP32 e conversores USB-serial conectados, com o diagnóstico e os botões de correção.</summary>
public sealed class SerialPortsPage : PageBase
{
    public override string Title => "Portas seriais";
    public override string Subtitle => "Placas Arduino, ESP32 e conversores USB-serial. Mostra a porta de cada uma e resolve o que costuma travar: módulo, permissão, brltty e ModemManager.";

    private readonly StackPanel _list = new() { Spacing = 12 };
    private readonly StatusText _status = new();
    private readonly StackPanel _env = new() { Spacing = 8 };

    public SerialPortsPage()
    {
        Body.Children.Add(Ui.Wrap(Ui.Primary("ATUALIZAR", Load).WithMargin(0, 0, 8, 8)));
        Body.Children.Add(_status);
        Body.Children.Add(_list);
        Body.Children.Add(Ui.Card(_env, "Ajustes do sistema para placas Maker"));
    }

    public override void OnShown() => Load();

    private InstallerState _installer;

    private async void Load()
    {
        _installer = await Task.Run(() => { Packages.CompletePending(S.Settings, S.Settings.Save); return Packages.InstallerActivity(); });
        var devs = await Task.Run(Maker.Devices);
        _list.Children.Clear();
        if (devs.Count == 0)
            _list.Children.Add(Ui.Card(Ui.VStack(6, new TextBlock { Text = "Nenhuma placa USB-serial conhecida conectada", FontSize = 16, FontWeight = FontWeight.SemiBold },
                Ui.Note("Conecte a placa por um cabo USB de dados (cabos só de carga não servem) e clique em ATUALIZAR. Reconheço CH340/CH341, CP210x, FTDI, Prolific, Arduino e as placas de USB nativo (ESP32-S2/S3, RP2040, Teensy)."))));
        foreach (var d in devs) _list.Children.Add(Card(d));
        BuildEnv();
    }

    private Control Card(SerialDevice d)
    {
        var head = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        var title = Ui.VStack(2, new TextBlock { Text = d.Name, FontSize = 16, FontWeight = FontWeight.SemiBold }, Ui.Muted(d.Detail).Cls("mono"));
        var badge = Ui.Badge(d.Verdict, !d.Ok);
        Grid.SetColumn(badge, 1);
        head.Children.Add(title); head.Children.Add(badge);
        var body = Ui.VStack(10, head);
        var facts = new List<KeyValuePair<string, string>>
        {
            new("Driver do kernel", d.Driver ?? "nenhum"), new("Módulo esperado", d.Chip?.Module ?? "—"),
        };
        if (d.Tty != null) { facts.Add(new("Porta", d.Node)); facts.Add(new("Grupo da porta", d.Group.Length > 0 ? d.Group : "—")); if (d.ById != null) facts.Add(new("Caminho estável", "/dev/serial/by-id/" + d.ById)); }
        body.Children.Add(Ui.KeyValues(facts, 200));
        foreach (var a in d.Advice) body.Children.Add(Ui.Text("• " + a).Cls("sub"));

        var actions = new WrapPanel();
        if (d.Chip != null && d.Driver == null && Maker.ModuleAvailable(d.Chip.Module) && !Maker.ModuleLoaded(d.Chip.Module))
            actions.Children.Add(Act("CARREGAR MÓDULO " + d.Chip.Module, () => Run(() => Maker.LoadModule(d.Chip.Module), "módulo carregado")));
        if (d.Tty != null && !d.Access && Maker.CanAddToGroup(d.Group, Environment.UserName))
            actions.Children.Add(Act($"ADICIONAR AO GRUPO {d.Group.ToUpperInvariant()}",
                () => Run(() => Maker.AddUserToGroup(d.Group, S.Settings, S.Settings.Save), $"adicionado ao grupo {d.Group}: saia da sessão e entre de novo (para desfazer, use TIRAR DO GRUPO, abaixo)")));
        if (d.Vid == "1A86" && d.Tty == null && Maker.BrlttyPresent)
            actions.Children.Add(Act("REMOVER BRLTTY", async () =>
            {
                if (await Dialogs.ConfirmAsync(this, "Remover o brltty", "O brltty é o leitor de tela em braille. Remova só se você não usa braille. Para desfazer, o botão REINSTALAR O BRLTTY (abaixo, precisa de internet) devolve o que sair. Continuar?", "REMOVER"))
                    Run(() => Maker.RemoveBrltty(S.Settings, S.Settings.Save), "brltty removido: desconecte e conecte a placa de novo");
            }));
        if (actions.Children.Count > 0) body.Children.Add(actions);
        return Ui.Card(body);
    }

    private static Button Act(string text, Action a) { var b = Ui.Outline(text, a).Small(); b.Margin = new Thickness(0, 0, 8, 8); return b; }

    private void Run(Func<RootResult> fix, string ok) => Run(() => (fix(), (string?)null), ok);

    private async void Run(Func<(RootResult Result, string? Warning)> fix, string ok)
    {
        _status.Text = "Aguardando a senha de administrador…";
        var (r, warning) = await Task.Run(fix);
        _status.Text = (r.Ok ? "Pronto: " + ok + "." : "Não consegui: " + r.Message) + (warning != null ? " " + warning : "");
        Load();
    }

    private void BuildEnv()
    {
        _env.Children.Clear();
        bool rule = Maker.RuleInstalled, mm = Maker.ModemManagerRunning, br = Maker.BrlttyPresent;
        _env.Children.Add(Ui.Note("Regra udev do InfoPC: manda o ModemManager ignorar estas placas e libera a porta para quem está logado (sem precisar do grupo dialout). Fica em " + Maker.RuleFile + " e pode ser removida a qualquer hora."));
        _env.Children.Add(Ui.KeyValues(new[]
        {
            new KeyValuePair<string, string>("Regra udev do InfoPC", rule ? "instalada" : "não instalada"),
            new KeyValuePair<string, string>("ModemManager", mm ? "ativo (pode atrapalhar o upload)" : "inativo ou ausente"),
            new KeyValuePair<string, string>("brltty (braille)", br ? "presente (conflita com o CH340)" : "ausente"),
            new KeyValuePair<string, string>("Seus grupos", string.Join(", ", Maker.UserGroups().OrderBy(g => g, StringComparer.Ordinal))),
        }, 200));
        _env.Children.Add(Ui.Wrap(
            rule ? Act("REMOVER A REGRA UDEV", () => Run(Maker.RemoveRule, "regra removida")) : Act("INSTALAR A REGRA UDEV (pede senha)", () => Run(Maker.InstallRule, "regra instalada: desconecte e conecte a placa")),
            Act("ABRIR /dev/serial/by-id", () => Shell.Open("/dev/serial/by-id"))));

        // desfazer do que o InfoPC mudou aqui (requisito 4)
        var undo = new List<Control>();
        foreach (var g in S.Settings.GroupsAdded.ToList())
            undo.Add(Act($"TIRAR DO GRUPO {g.ToUpperInvariant()} (pede senha)", () => Run(() => Maker.RemoveUserFromGroup(g, S.Settings, S.Settings.Save), $"você saiu do grupo {g}: saia da sessão e entre de novo")));
        foreach (var c in S.Settings.PackageChanges.Where(c => c.Kind == "brltty").ToList())
        {
            if (c.Pending)
            {
                _env.Children.Add(Ui.Note("A remoção do brltty ainda não tem a lista final de pacotes, e o REINSTALAR aparece quando ela existir. " + Packages.PendingHint(_installer)));
                if (_installer == InstallerState.Unknown)
                    undo.Add(Act("O INSTALADOR JÁ TERMINOU", async () =>
                    {
                        if (!await Dialogs.ConfirmAsync(this, "Concluir a lista de pacotes", "Confirme só se a remoção já terminou. Se ela ainda estiver rodando, o desfazer fica incompleto. Continuar?", "CONFIRMAR")) return;
                        await Task.Run(() => Packages.ConfirmFinished(S.Settings, S.Settings.Save));
                        Load();
                    }));
                continue;
            }
            if (c.Recovered) _env.Children.Add(Ui.Text("A lista da remoção do brltty foi concluída depois: pode incluir pacotes que você mudou por fora nesse meio-tempo. Confira antes de reinstalar. " + Packages.Describe(c)).Cls("warn"));
            undo.Add(Act("REINSTALAR O BRLTTY (pede senha e internet)", () => Run(() => Packages.Undo(c, S.Settings, S.Settings.Save), "brltty reinstalado")));
        }
        if (undo.Count > 0)
        {
            _env.Children.Add(Ui.Note("Desfazer o que o InfoPC mudou nesta página:"));
            _env.Children.Add(Ui.Wrap(undo.ToArray()));
        }
    }
}

/// <summary>Drivers seriais: o módulo do kernel de cada família de chip (carregado, disponível, versão) e as placas que dependem dele.</summary>
public sealed class SerialDriversPage : PageBase
{
    public override string Title => "Drivers seriais";
    public override string Subtitle => "CH340/CH341, CP210x, FTDI, Prolific e Arduino/ESP32 (CDC). No Linux os drivers vêm no kernel; aqui você confere se o módulo de cada um está disponível.";

    private readonly StackPanel _list = new() { Spacing = 12 };
    private readonly StatusText _status = new();

    public SerialDriversPage()
    {
        Body.Children.Add(Ui.Wrap(Ui.Primary("ATUALIZAR", Load).WithMargin(0, 0, 8, 8)));
        Body.Children.Add(_status);
        Body.Children.Add(_list);
    }

    public override void OnShown() => Load();

    private async void Load()
    {
        var statuses = await Task.Run(() => Maker.ChipStatuses(Maker.Devices()));
        _list.Children.Clear();
        foreach (var s in statuses)
        {
            string state = s.Loaded ? "CARREGADO" : s.Available ? "DISPONÍVEL" : "AUSENTE";
            var head = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            var title = Ui.VStack(2, new TextBlock { Text = s.Chip.Name, FontSize = 16, FontWeight = FontWeight.SemiBold },
                                  Ui.Muted($"módulo {s.Chip.Module} · VID {string.Join(", ", s.Chip.Vids)}" + (s.Version != null ? $" · versão {s.Version}" : "")).Cls("mono"));
            var badge = Ui.Badge(state, !s.Loaded && s.Devices.Count > 0);
            Grid.SetColumn(badge, 1);
            head.Children.Add(title); head.Children.Add(badge);
            var body = Ui.VStack(8, head);
            body.Children.Add(Ui.Text(s.Devices.Count > 0 ? "Conectado agora: " + string.Join(", ", s.Devices.Select(d => $"{d.Name} ({d.Node})")) : "Nenhuma placa desta família conectada.").Cls("sub"));
            if (!s.Available) body.Children.Add(Ui.Text($"Este kernel não traz o módulo {s.Chip.Module}. Instale os módulos extras do kernel (Ubuntu: linux-modules-extra; Fedora: kernel-modules-extra) ou o driver do fabricante.").Cls("sub"));
            var actions = new WrapPanel();
            if (s.Available && !s.Loaded) actions.Children.Add(Small("CARREGAR MÓDULO (pede senha)", async () =>
            {
                _status.Text = "Aguardando a senha de administrador…";
                var r = await Task.Run(() => Maker.LoadModule(s.Chip.Module));
                _status.Text = r.Ok ? $"Módulo {s.Chip.Module} carregado até reiniciar." : "Não consegui: " + r.Message;
                Load();
            }));
            actions.Children.Add(Small("PÁGINA DO FABRICANTE", () => Shell.Open(s.Chip.OfficialUrl)));
            body.Children.Add(actions);
            _list.Children.Add(Ui.Card(body));
        }
    }

    private static Button Small(string text, Action a) { var b = Ui.Outline(text, a).Small(); b.Margin = new Thickness(0, 0, 8, 8); return b; }
}
