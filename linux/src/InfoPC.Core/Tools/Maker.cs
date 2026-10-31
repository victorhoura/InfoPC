using System.Text.RegularExpressions;

namespace InfoPC;

/// <summary>Família de chip USB-serial: como reconhecer, qual módulo do kernel o atende e onde achar o driver do fabricante.</summary>
public sealed record SerialChip(string Name, string[] Vids, string Module, string OfficialUrl);

/// <summary>Placa ou conversor USB-serial conectado, com o diagnóstico.</summary>
public sealed class SerialDevice
{
    public string UsbPath { get; init; } = "";
    public string Vid { get; init; } = "";
    public string Pid { get; init; } = "";
    public string Manufacturer { get; init; } = "";
    public string Product { get; init; } = "";
    public SerialChip? Chip { get; init; }
    public string? Tty { get; init; }              // ex.: ttyUSB0
    public string? Driver { get; init; }           // módulo ligado à interface
    public string? ById { get; init; }
    public string Group { get; init; } = "";
    public bool Access { get; init; }
    public bool Ok { get; set; }
    public string Verdict { get; set; } = "";
    public List<string> Advice { get; } = new();
    public string Name => string.IsNullOrWhiteSpace(Product) ? (Chip?.Name ?? $"USB {Vid}:{Pid}") : Product;
    public string Node => Tty != null ? "/dev/" + Tty : "sem porta";
    public string Detail => string.Join(" · ", new[] { Node, Chip?.Name ?? Manufacturer, $"VID {Vid} PID {Pid}" }.Where(s => !string.IsNullOrEmpty(s)));
}

/// <summary>
/// Maker no Linux: os drivers dos chips USB-serial vêm no kernel (ch341, cp210x, ftdi_sio, pl2303, cdc_acm). O que dá problema é outra coisa:
/// módulo não carregado, porta sem permissão (grupo dialout/uucp), o brltty roubando o CH340 e o ModemManager abrindo a porta no upload.
/// </summary>
public static class Maker
{
    public const string RuleFile = "/etc/udev/rules.d/99-infopc-serial.rules";

    public static readonly SerialChip[] Chips =
    {
        new("CH340/CH341", new[] { "1A86" }, "ch341", "https://www.wch-ic.com/downloads/CH341SER_LINUX_ZIP.html"),
        new("CP210x", new[] { "10C4" }, "cp210x", "https://www.silabs.com/developer-tools/usb-to-uart-bridge-vcp-drivers"),
        new("FTDI", new[] { "0403" }, "ftdi_sio", "https://ftdichip.com/drivers/vcp-drivers/"),
        new("Prolific PL2303", new[] { "067B" }, "pl2303", "https://www.prolific.com.tw/"),
        new("Arduino (CDC)", new[] { "2341", "2A03" }, "cdc_acm", "https://www.arduino.cc/en/software"),
        new("ESP32 / RP2040 / Teensy (USB nativo)", new[] { "303A", "2E8A", "16C0", "239A" }, "cdc_acm", "https://docs.espressif.com/projects/esp-idf/"),
    };

    public static SerialChip? ChipByVid(string vid) => Chips.FirstOrDefault(c => c.Vids.Contains(vid, StringComparer.OrdinalIgnoreCase));

    // ---------------- dispositivos ----------------

    public static List<SerialDevice> Devices()
    {
        var list = new List<SerialDevice>();
        string user = Environment.UserName;
        var groups = UserGroups();
        foreach (var d in Sys.List("/sys/bus/usb/devices"))
        {
            string path = "/sys/bus/usb/devices/" + d;
            if (d.Contains(':') || Sys.Read(path + "/idVendor") is not { } vid) continue;
            vid = vid.ToUpperInvariant();
            var chip = ChipByVid(vid);
            if (chip == null) continue;
            string pid = (Sys.Read(path + "/idProduct") ?? "").ToUpperInvariant();

            // a interface com a porta serial: ttyUSBn direto dentro dela, ou tty/ttyACMn
            string? tty = null, driver = null;
            foreach (var iface in Sys.List(path).Where(n => n.StartsWith(d + ":", StringComparison.Ordinal)))
            {
                string ip = $"{path}/{iface}";
                string? t = Sys.List(ip).FirstOrDefault(n => n.StartsWith("ttyUSB", StringComparison.Ordinal) || n.StartsWith("ttyACM", StringComparison.Ordinal))
                            ?? Sys.List(ip + "/tty").FirstOrDefault(n => n.StartsWith("tty", StringComparison.Ordinal));
                string? drv = Sys.LinkName(ip + "/driver");
                if (t != null) { tty = t; driver = drv; break; }
                driver ??= drv;
            }
            string group = "", mode = "";
            bool access = false;
            if (tty != null)
            {
                var st = Shell.Run("stat", new[] { "-c", "%G|%a", "/dev/" + tty }, 3000);
                if (st.Ok) { var f = st.Out.Trim().Split('|'); group = f[0]; mode = f.Length > 1 ? f[1] : ""; }
                access = group.Length == 0 || groups.Contains(group) || Elevate.IsRoot || mode.EndsWith('6') || mode.EndsWith('7');
            }
            string? byId = null;
            if (tty != null)
                byId = Sys.List("/dev/serial/by-id").FirstOrDefault(n => Sys.LinkName("/dev/serial/by-id/" + n) == tty);

            var dev = new SerialDevice
            {
                UsbPath = d, Vid = vid, Pid = pid, Manufacturer = Sys.Read(path + "/manufacturer") ?? "", Product = Sys.Read(path + "/product") ?? "",
                Chip = chip, Tty = tty, Driver = driver, ById = byId, Group = group, Access = access,
            };
            Diagnose(dev, user);
            list.Add(dev);
        }
        return list;
    }

    /// <summary>Grupos do usuário NESTA sessão (id -nG): quem entrou num grupo agora só o vê depois de sair e entrar de novo.</summary>
    public static HashSet<string> UserGroups()
    {
        var r = Shell.Run("id", new[] { "-nG" }, 3000);
        return r.Ok ? r.Out.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).ToHashSet() : new HashSet<string>();
    }

    /// <summary>O usuário está no grupo em /etc/group, que é o que vale no próximo login? Null se não deu para ler o arquivo: não é o mesmo que "não está".</summary>
    public static bool? InGroupFile(string group, string user)
    {
        var text = Sys.Read("/etc/group");
        if (string.IsNullOrEmpty(text)) return null;
        return text.Split('\n').Any(l => { var f = l.Trim().Split(':'); return f.Length >= 4 && f[0] == group && f[3].Split(',').Contains(user); });
    }

    /// <summary>O grupo já tem o usuário em /etc/group, mas a sessão atual ainda não o enxerga (falta sair e entrar de novo).</summary>
    public static bool GroupPending(string group, string user) => group.Length > 0 && InGroupFile(group, user) == true;

    /// <summary>ADICIONAR AO GRUPO só faz sentido para quem não está nele nem na sessão nem em /etc/group (nesse caso falta só entrar de novo).</summary>
    public static bool CanAddToGroup(string group, string user) => group.Length > 0 && !UserGroups().Contains(group) && !GroupPending(group, user);

    public static bool ModuleLoaded(string module) => Sys.DirExists("/sys/module/" + module);

    public static bool ModuleAvailable(string module)
    {
        if (ModuleLoaded(module)) return true;
        var r = Shell.Run("modinfo", new[] { "-n", module }, 4000);
        return r.Ok && r.Out.Trim().Length > 0;
    }

    public static bool BrlttyPresent => Shell.Has("brltty") || new[] { "/usr/lib/udev/rules.d/85-brltty.rules", "/lib/udev/rules.d/85-brltty.rules" }.Any(Sys.FileExists);

    public static bool ModemManagerRunning =>
        Shell.Has("systemctl") && Shell.Run("systemctl", new[] { "is-active", "ModemManager" }, 3000).Out.Trim() == "active";

    public static bool RuleInstalled => Sys.FileExists(RuleFile);

    private static void Diagnose(SerialDevice d, string user)
    {
        var chip = d.Chip!;
        if (d.Tty == null)
        {
            d.Verdict = "SEM PORTA";
            if (d.Driver == null && !ModuleLoaded(chip.Module))
            {
                if (ModuleAvailable(chip.Module)) d.Advice.Add($"O módulo {chip.Module} existe no kernel mas não está carregado. Use o botão CARREGAR MÓDULO (ou: sudo modprobe {chip.Module}).");
                else d.Advice.Add($"Este kernel não tem o módulo {chip.Module}. Instale o pacote de módulos extras do kernel (Ubuntu: linux-modules-extra-$(uname -r); Fedora: kernel-modules-extra) ou o driver do fabricante: {chip.OfficialUrl}");
            }
            else d.Advice.Add("O driver está carregado, mas não surgiu uma porta (/dev/ttyUSB* ou ttyACM*). Troque o cabo (muitos são só de carga) e a porta USB.");
            if (d.Vid == "1A86" && BrlttyPresent) d.Advice.Add("O brltty (leitor de tela em braille) tem uma regra que toma o chip CH340 e faz a porta sumir. Se você não usa braille, remova o pacote brltty (botão REMOVER BRLTTY).");
            return;
        }

        bool pending = !d.Access && GroupPending(d.Group, user);
        if (!d.Access)
        {
            d.Verdict = pending ? "SAIA E ENTRE" : "SEM PERMISSÃO";
            d.Advice.Add(pending
                ? $"Você já está no grupo {d.Group}, mas a sessão atual ainda não sabe. Saia da sessão e entre de novo (ou reinicie)."
                : $"A porta {d.Node} pertence ao grupo {d.Group} e você não faz parte dele. Use o botão ADICIONAR AO GRUPO (depois, saia e entre de novo) ou instale a regra udev, que libera a porta só para quem está logado.");
        }
        if (ModemManagerRunning && !RuleInstalled)
            d.Advice.Add("O ModemManager está ativo e pode abrir a porta antes do seu programa (o upload no Arduino IDE/esptool falha ou atrasa). A regra udev do InfoPC manda o ModemManager ignorar estas placas.");
        d.Ok = d.Access;
        if (d.Access) d.Verdict = d.Advice.Count == 0 ? "FUNCIONANDO" : "FUNCIONA, COM AVISO";
    }

    // ---------------- correções (sempre por botão, com senha) ----------------

    private static readonly Regex SafeId = new(@"^[a-z_][a-z0-9_-]{0,31}$", RegexOptions.Compiled);

    public static RootResult LoadModule(string module) =>
        SafeId.IsMatch(module) ? Elevate.Run("modprobe", new[] { module }) : new RootResult(false, "", "Nome de módulo inválido.");

    /// <summary>
    /// Põe o usuário no grupo. O grupo vai antes para <see cref="AppSettings.GroupsAdded"/> no disco (<paramref name="persist"/>), para
    /// <see cref="RemoveUserFromGroup"/> desfazer; se não der para gravar, nada muda. Quem já estava no grupo não é registrado: o desfazer
    /// tiraria uma associação que não foi o InfoPC que fez.
    /// </summary>
    public static RootResult AddUserToGroup(string group, AppSettings s, Action persist, string? user = null)
    {
        user ??= Environment.UserName;
        if (!SafeId.IsMatch(group) || !SafeId.IsMatch(user)) return new RootResult(false, "", "Nome de grupo ou de usuário inválido.");
        switch (InGroupFile(group, user))
        {
            case null: return new RootResult(false, "", "Nada foi alterado: não consegui ler /etc/group para saber se você já está no grupo.");
            case true: return new RootResult(false, "", $"Nada foi alterado: você já está no grupo {group}. Saia da sessão e entre de novo.");
        }
        bool added = !s.GroupsAdded.Contains(group);
        if (added)
        {
            s.GroupsAdded.Add(group);
            try { persist(); }
            catch (Exception ex)
            {
                s.GroupsAdded.Remove(group);
                return new RootResult(false, "", $"Nada foi alterado: não consegui guardar o desfazer ({ex.Message}).");
            }
        }
        var r = Elevate.Run("usermod", new[] { "-aG", group, user });
        if (!r.Ok && added) { s.GroupsAdded.Remove(group); try { persist(); } catch { } }
        return r;
    }

    /// <summary>Desfaz <see cref="AddUserToGroup"/>: tira o usuário só de um grupo em que o InfoPC o pôs.</summary>
    public static RootResult RemoveUserFromGroup(string group, AppSettings s, Action persist, string? user = null)
    {
        user ??= Environment.UserName;
        if (!s.GroupsAdded.Contains(group)) return new RootResult(false, "", "O InfoPC não pôs você neste grupo: não há o que desfazer.");
        if (!SafeId.IsMatch(group) || !SafeId.IsMatch(user)) return new RootResult(false, "", "Nome de grupo ou de usuário inválido.");
        var member = InGroupFile(group, user);
        if (member == null) return new RootResult(false, "", "Nada foi alterado: não consegui ler /etc/group para saber se você ainda está no grupo.");
        var r = member == true ? Elevate.Run("gpasswd", new[] { "-d", user, group })
                               : new RootResult(true, $"Você já não estava no grupo {group}.", "");   // saiu por fora: só esquece
        if (r.Ok) { s.GroupsAdded.Remove(group); try { persist(); } catch { } } // se não gravar, o botão continua e repetir não muda nada
        return r;
    }

    public static string RuleText()
    {
        var vids = Chips.SelectMany(c => c.Vids).Select(v => v.ToLowerInvariant()).Distinct().OrderBy(v => v, StringComparer.Ordinal);
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("# InfoPC: placas Arduino, ESP32 e conversores USB-serial.");
        sb.AppendLine("# Manda o ModemManager ignorar estas placas e libera a porta para quem está logado (uaccess) e para o grupo dialout.");
        sb.AppendLine("# Para desfazer: apague este arquivo e rode: sudo udevadm control --reload-rules");
        foreach (var v in vids)
        {
            sb.AppendLine($"SUBSYSTEM==\"usb\", ATTR{{idVendor}}==\"{v}\", ENV{{ID_MM_DEVICE_IGNORE}}=\"1\"");
            sb.AppendLine($"SUBSYSTEM==\"tty\", ATTRS{{idVendor}}==\"{v}\", ENV{{ID_MM_DEVICE_IGNORE}}=\"1\", TAG+=\"uaccess\", MODE=\"0660\", GROUP=\"dialout\"");
        }
        return sb.ToString().Replace("\r\n", "\n");   // arquivos de regra udev sempre com LF
    }

    /// <summary>Instala a regra udev. O texto vai como argumento do roteiro fixo (nunca dentro dele).</summary>
    public static RootResult InstallRule() =>
        Elevate.Run("sh", new[] { "-c", "printf '%s' \"$1\" > \"$2\" && udevadm control --reload-rules && udevadm trigger --subsystem-match=tty --subsystem-match=usb", "infopc", RuleText(), RuleFile });

    public static RootResult RemoveRule() =>
        Elevate.Run("sh", new[] { "-c", "rm -f \"$1\" && udevadm control --reload-rules", "infopc", RuleFile });

    /// <summary>Remove o brltty, guardando o que saiu (ele e o que o gerenciador tirou junto) em <see cref="AppSettings.PackageChanges"/> para reinstalar.</summary>
    public static (RootResult Result, string? Warning) RemoveBrltty(AppSettings s, Action persist, string? manager = null)
    {
        manager ??= OsInfo.PackageManager();
        Func<RootResult> remove = manager switch
        {
            "apt" => () => Elevate.Run("apt-get", new[] { "remove", "-y", "brltty" }, 300000),
            "dnf" => () => Elevate.Run("dnf", new[] { "remove", "-y", "brltty" }, 300000),
            "pacman" => () => Elevate.Run("pacman", new[] { "-R", "--noconfirm", "brltty" }, 300000),
            "zypper" => () => Elevate.Run("zypper", new[] { "--non-interactive", "remove", "brltty" }, 300000),
            _ => () => new RootResult(false, "", "Não reconheci o gerenciador de pacotes. Remova o brltty por ele.")
        };
        if (manager is not ("apt" or "dnf" or "pacman" or "zypper")) return (remove(), null);
        return Packages.Track("brltty", "remoção do brltty (Maker)", manager, s, persist, remove);
    }

    // ---------------- driver por família ----------------

    public sealed record ChipStatus(SerialChip Chip, bool Loaded, bool Available, string? Version, List<SerialDevice> Devices);

    public static List<ChipStatus> ChipStatuses(IReadOnlyList<SerialDevice> devices) =>
        Chips.Select(c => new ChipStatus(c, ModuleLoaded(c.Module), ModuleAvailable(c.Module), Sys.Read($"/sys/module/{c.Module}/version"),
                                         devices.Where(d => d.Chip == c).ToList())).ToList();
}
