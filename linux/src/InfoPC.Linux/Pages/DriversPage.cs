using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace InfoPC.Linux;

/// <summary>Drivers: o driver de cada placa de vídeo, dispositivos sem driver ou sem firmware, atualizações de firmware (fwupd) e a lista de módulos.</summary>
public sealed class DriversPage : PageBase
{
    public override string Title => "Drivers";
    public override string Subtitle => "No Linux os drivers vêm no kernel. Aqui você vê qual driver cada peça usa, o que ficou sem driver ou sem firmware, e as atualizações de firmware do fabricante.";

    private readonly StackPanel _gpus = new() { Spacing = 12 };
    private readonly StackPanel _devices = new() { Spacing = 4 };
    private readonly StackPanel _firmware = new() { Spacing = 6 };
    private readonly StackPanel _fwupd = new() { Spacing = 8 };
    private readonly StatusText _status = new();
    private readonly StatusText _fwStatus = new();

    public DriversPage()
    {
        Body.Children.Add(Ui.Wrap(Ui.Primary("ATUALIZAR", Load).WithMargin(0, 0, 8, 8), Ui.Outline("EXPORTAR LISTA DE DRIVERS", Export).WithMargin(0, 0, 8, 8)));
        Body.Children.Add(_status);
        Body.Children.Add(Ui.Card(_gpus, "Placas de vídeo"));
        Body.Children.Add(Ui.Card(Ui.VStack(10, _firmware), "Firmware que o kernel não achou"));
        var fwCmd = Ui.Selectable(DriverInfo.FirmwareUpdateCommand, "mono"); fwCmd.FontSize = 12.5;
        Body.Children.Add(Ui.Card(Ui.VStack(10,
            Ui.Note("O LVFS (Linux Vendor Firmware Service) distribui atualizações de BIOS, SSD, dock e periféricos de vários fabricantes. Verificar consulta a internet e não muda nada."),
            Ui.HStack(10, Ui.Outline("VERIFICAR ATUALIZAÇÕES", ScanFirmware)), _fwStatus, _fwupd,
            Ui.Note("O InfoPC não instala firmware: muitos dispositivos não aceitam voltar à versão anterior, então não haveria como desfazer. Se quiser instalar, rode no terminal o comando abaixo. Faça backup, mantenha o notebook na tomada e não desligue durante o processo; alguns firmwares só terminam no próximo boot."),
            fwCmd), "Atualizações de firmware (fwupd)"));
        Body.Children.Add(Ui.Card(_devices, "Dispositivos e seus drivers"));
    }

    private const string RecoveredNote = "Esta lista foi concluída depois da instalação: pode incluir pacotes que você mudou por fora nesse meio-tempo. Confira antes de desfazer.";

    public override void OnShown() { S.Loaded -= Load; S.Loaded += Load; Load(); }
    public override void OnHidden() => S.Loaded -= Load;

    private async void Load()
    {
        var os = S.Os ?? OsInfo.Read();
        var installer = await Task.Run(() => { Packages.CompletePending(S.Settings, S.Settings.Save); return Packages.InstallerActivity(); });
        string? ubuntu = await Task.Run(() => Shell.Has("ubuntu-drivers") ? DriverInfo.UbuntuRecommended() : null);
        _gpus.Children.Clear();
        if (S.Gpus.Count == 0) _gpus.Children.Add(Ui.Note(S.Ready ? "Nenhuma GPU PCI encontrada." : "Detectando o hardware…"));
        foreach (var g in S.Gpus)
        {
            var adv = DriverInfo.Advice(g, os, ubuntu);
            var head = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            var title = Ui.VStack(2, new TextBlock { Text = g.Name, FontSize = 15, FontWeight = FontWeight.SemiBold }, Ui.Muted($"driver do kernel: {g.Driver ?? "nenhum"} · {g.PciId}").Cls("mono"));
            var badge = Ui.Badge(adv.Status, !adv.Ok);
            Grid.SetColumn(badge, 1);
            head.Children.Add(title); head.Children.Add(badge);
            var box = Ui.VStack(8, head, Ui.Text(adv.Text).Cls("sub"));
            if (adv.Command != null)
            {
                box.Children.Add(Ui.Muted("Comando:"));
                var cmd = Ui.Selectable(adv.Command, "mono"); cmd.FontSize = 12.5;
                box.Children.Add(cmd);
            }
            if (adv.CanAutoInstall)
                box.Children.Add(Ui.Primary("INSTALAR O DRIVER RECOMENDADO (pede senha)", async () =>
                {
                    if (!await Dialogs.ConfirmAsync(this, "Instalar driver", "O ubuntu-drivers vai baixar e instalar o driver da NVIDIA (precisa de internet e leva alguns minutos). Reinicie o PC depois. O InfoPC guarda a lista do que entrou: o botão DESFAZER remove o driver e devolve o que havia antes. Continuar?", "INSTALAR")) return;
                    _status.Text = "Instalando o driver… (pode levar vários minutos)";
                    var (r, warning) = await Task.Run(() => DriverInfo.InstallUbuntuDrivers(S.Settings, S.Settings.Save));
                    _status.Text = (r.Ok ? "Driver instalado. Reinicie o PC para usá-lo." : "Falhou: " + r.Message) + (warning != null ? " " + warning : "");
                    Load();
                }).WithMargin(0, 4, 0, 0));
            _gpus.Children.Add(Ui.Card(box, null, 14));
        }
        // desfazer das instalações de driver feitas pelo InfoPC (requisito 4)
        foreach (var c in S.Settings.PackageChanges.Where(c => c.Kind == "gpu-driver").ToList())
        {
            if (c.Pending)
            {
                var pending = Ui.VStack(8, Ui.Note($"Instalação de {c.What} em {c.When:dd/MM/yyyy HH:mm}: ainda sem a lista final de pacotes, e o DESFAZER aparece quando ela existir. {Packages.PendingHint(installer)}"));
                if (installer == InstallerState.Unknown)
                    pending.Children.Add(Ui.Outline("O INSTALADOR JÁ TERMINOU", async () =>
                    {
                        if (!await Dialogs.ConfirmAsync(this, "Concluir a lista de pacotes", "Confirme só se a instalação já terminou. Se ela ainda estiver rodando, o desfazer fica incompleto. Continuar?", "CONFIRMAR")) return;
                        await Task.Run(() => Packages.ConfirmFinished(S.Settings, S.Settings.Save));
                        Load();
                    }));
                _gpus.Children.Add(Ui.Card(pending, null, 14));
                continue;
            }
            var change = Ui.VStack(8, Ui.Text($"Instalação de {c.What} em {c.When:dd/MM/yyyy HH:mm}").Cls("sub"), Ui.Muted(Packages.Describe(c)).Cls("mono"));
            if (c.Recovered) change.Children.Add(Ui.Text(RecoveredNote).Cls("warn"));
            change.Children.Add(
                Ui.Outline("DESFAZER (pede senha; reinstalar o que saiu precisa de internet)", async () =>
                {
                    if (!await Dialogs.ConfirmAsync(this, "Desfazer a instalação do driver", "O InfoPC vai remover os pacotes que entraram e reinstalar os que saíram. Reinicie o PC depois. Continuar?", "DESFAZER")) return;
                    _status.Text = "Desfazendo… (pode levar vários minutos)";
                    var r = await Task.Run(() => Packages.Undo(c, S.Settings, S.Settings.Save));
                    _status.Text = r.Ok ? "Instalação desfeita. Reinicie o PC." : "Não consegui desfazer: " + r.Message;
                    Load();
                }));
            _gpus.Children.Add(Ui.Card(change, null, 14));
        }
        if (S.Cpu != null) _gpus.Children.Add(Ui.Muted("CPU: " + DriverInfo.Microcode(S.Cpu)));

        var devs = await Task.Run(DriverInfo.PciDevices);
        _devices.Children.Clear();
        if (devs.Count == 0) _devices.Children.Add(Ui.Muted("O kernel não lista dispositivos PCI (comum em ARM e contêiner)."));
        int missing = devs.Count(d => d.Missing);
        if (missing > 0) _devices.Children.Add(Ui.Text($"{missing} dispositivo(s) sem driver:").Cls("warn"));
        foreach (var d in devs)
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("150,*,170"), ColumnSpacing = 10 };
            var cat = Ui.Muted(d.Category); cat.FontSize = 12;
            var name = new TextBlock { Text = d.Name, FontSize = 12.5, TextWrapping = TextWrapping.Wrap };
            var drv = new TextBlock { Text = d.Driver ?? "SEM DRIVER", FontSize = 12, FontFamily = new FontFamily("DejaVu Sans Mono, monospace"), TextAlignment = TextAlignment.Right, FontWeight = d.Missing ? FontWeight.Bold : FontWeight.Normal };
            if (d.Missing) drv.Classes.Add("warn");
            Grid.SetColumn(name, 1); Grid.SetColumn(drv, 2);
            row.Children.Add(cat); row.Children.Add(name); row.Children.Add(drv);
            _devices.Children.Add(row);
        }

        var fw = await Task.Run(DriverInfo.MissingFirmware);
        _firmware.Children.Clear();
        _firmware.Children.Add(fw.Count == 0
            ? Ui.Note("Nenhum pedido de firmware falhou neste boot. Está tudo certo.")
            : Ui.VStack(6, Ui.Text($"{fw.Count} arquivo(s) de firmware faltando neste boot:").Cls("warn"), Ui.Selectable(string.Join("\n", fw), "mono"),
                        Ui.Note("Instale o pacote linux-firmware (Ubuntu/Debian: linux-firmware ou firmware-misc-nonfree; Fedora e Arch: linux-firmware) e reinicie. Peças de Wi-Fi, vídeo e Bluetooth costumam depender dele.")));
    }

    private async void ScanFirmware()
    {
        _fwStatus.Text = "Consultando o LVFS…";
        var (list, msg) = await Task.Run(() => DriverInfo.FirmwareScan(refresh: true));
        _fwStatus.Text = msg;
        _fwupd.Children.Clear();
        foreach (var u in list)
            _fwupd.Children.Add(Ui.VStack(0, new TextBlock { Text = $"{u.Device} ({u.Vendor})", FontWeight = FontWeight.SemiBold, FontSize = 13 },
                                          Ui.Muted($"{u.Current} → {u.Available}  {u.Summary}").Cls("label")));
    }

    private async void Export()
    {
        var os = S.Os ?? OsInfo.Read();
        try
        {
            string text = await Task.Run(() => DriverInfo.ExportText(os));
            string file = Path.Combine(Reports.DocumentsDir(), $"InfoPC-drivers-{Environment.MachineName}-{DateTime.Now:yyyyMMdd-HHmm}.txt");
            File.WriteAllText(file, text, new System.Text.UTF8Encoding(false));
            _status.Text = "Lista salva em " + file;
        }
        catch (Exception ex) { _status.Text = "Não consegui salvar: " + ex.Message; }
    }
}
