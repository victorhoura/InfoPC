using System.Text.Json;
using System.Text.RegularExpressions;

namespace InfoPC;

public sealed record PciDevice(string Slot, string Category, string Name, string Vendor, string? Driver, bool Missing);

public sealed record KernelModule(string Name, long SizeBytes, int UsedBy);

public sealed record GpuDriverAdvice(bool Ok, string Status, string Text, string? Command, bool CanAutoInstall);

public sealed record FirmwareUpdate(string Device, string Vendor, string Current, string Available, string Summary);

/// <summary>
/// Drivers no Linux: o kernel já traz quase tudo, então "driver" aqui é: qual módulo cada dispositivo usa, o que ficou sem driver ou sem firmware,
/// se a GPU está com o driver certo e o que o LVFS (fwupd) oferece de atualização de firmware.
/// </summary>
public static class DriverInfo
{
    private const string PciDir = "/sys/bus/pci/devices";

    private static string? Category(string cls) => cls.Length < 6 ? null : cls[..6] switch
    {
        "0x0200" => "Ethernet", "0x0280" => "Rede sem fio / outro", "0x0300" => "Vídeo", "0x0302" => "Vídeo 3D", "0x0380" => "Vídeo",
        "0x0401" => "Áudio", "0x0403" => "Áudio (HD Audio)", "0x0c03" => "USB", "0x0c05" => "SMBus", "0x0c00" => "FireWire", "0x0c80" => "Barramento serial",
        "0x0106" => "Armazenamento SATA", "0x0108" => "Armazenamento NVMe", "0x0104" => "RAID", "0x0107" => "Armazenamento SAS", "0x0805" => "Leitor de cartão SD",
        "0x0780" => "Comunicação", "0x1180" => "Sensores / térmico", "0x0d11" => "Bluetooth", "0x0b40" => "Coprocessador", "0x0700" => "Porta serial", _ => null
    };

    /// <summary>Dispositivos PCI relevantes e o driver (módulo do kernel) que cada um usa. Pontes e controladores internos ficam de fora.</summary>
    public static List<PciDevice> PciDevices()
    {
        var raw = new List<(string Slot, string Cls, string V, string D, string? Drv)>();
        foreach (var slot in Sys.List(PciDir))
        {
            string d = $"{PciDir}/{slot}";
            string cls = Sys.Read(d + "/class") ?? "";
            if (cls.StartsWith("0x0604") || cls.StartsWith("0x0600") || cls.StartsWith("0x0601") || cls.StartsWith("0x0680") || cls.StartsWith("0x0880")) continue;   // pontes e afins
            string v = (Sys.Read(d + "/vendor") ?? "").Replace("0x", ""), dev = (Sys.Read(d + "/device") ?? "").Replace("0x", "");
            raw.Add((slot, cls, v, dev, Sys.LinkName(d + "/driver")));
        }
        var names = PciIds.LookupMany(raw.Select(r => (r.V, r.D)));
        var list = new List<PciDevice>();
        foreach (var r in raw)
        {
            string key = $"{r.V}:{r.D}".ToLowerInvariant();
            var (vendor, device) = names.TryGetValue(key, out var n) ? n : (null, null);
            string cat = Category(r.Cls) ?? "Outro";
            list.Add(new PciDevice(r.Slot, cat, device ?? $"Dispositivo {r.V.ToUpperInvariant()}:{r.D.ToUpperInvariant()}", Short(vendor), r.Drv, r.Drv == null && cat != "Outro"));
        }
        return list.OrderBy(x => x.Missing ? 0 : 1).ThenBy(x => x.Category, StringComparer.Ordinal).ThenBy(x => x.Name, StringComparer.Ordinal).ToList();
    }

    private static string Short(string? v) => (v ?? "").Replace("Corporation", "").Replace("Corp.", "").Replace("Inc.", "").Replace("[AMD/ATI]", "").Replace(",", "").Trim();

    public static List<KernelModule> Modules() =>
        Sys.Lines("/proc/modules").Select(l => l.Split(' ')).Where(f => f.Length >= 3)
           .Select(f => new KernelModule(f[0], Num.L(f[1]) ?? 0, (int)(Num.L(f[2]) ?? 0))).OrderBy(m => m.Name, StringComparer.Ordinal).ToList();

    /// <summary>Arquivos de firmware que o kernel pediu e não achou: "Direct firmware load for iwlwifi-ty-a0-gf-a0-77.ucode failed with error -2".</summary>
    public static List<string> MissingFirmware(string kernelLog) =>
        Regex.Matches(kernelLog, @"(?:Direct firmware load for|firmware: failed to load|Failed to load firmware)\s+(\S+)", RegexOptions.IgnoreCase)
             .Select(m => m.Groups[1].Value.TrimEnd(',', ':')).Distinct().OrderBy(x => x, StringComparer.Ordinal).ToList();

    public static List<string> MissingFirmware()
    {
        var r = Shell.Run("journalctl", new[] { "-k", "--no-pager", "-q", "-b" }, 15000);
        if (!r.Ok || r.Out.Trim().Length == 0) r = Shell.Run("dmesg", null, 8000);
        return r.Ok ? MissingFirmware(r.Out) : new List<string>();
    }

    // ---------------- GPU ----------------

    public static GpuDriverAdvice Advice(GpuDevice g, OsDetails os, string? ubuntuRecommended = null)
    {
        string drv = g.Driver ?? "";
        if (g.IsVirtual) return new(true, "VIRTUAL", "GPU emulada pelo hipervisor da máquina virtual: o driver é o do convidado, não há o que instalar.", null, false);
        switch (g.Vendor)
        {
            case GpuVendor.Nvidia:
                if (drv == "nvidia") return new(true, "DRIVER OFICIAL", NvidiaVersion(g) is { } v ? $"Driver proprietário da NVIDIA {v} em uso: tem CUDA, NVENC, sensores completos e os ajustes da página Overclock." : "Driver proprietário da NVIDIA em uso.", null, false);
                string how = Install(os, "NVIDIA", ubuntuRecommended);
                return new(false, drv == "nouveau" ? "DRIVER LIVRE" : "SEM DRIVER",
                           (drv == "nouveau" ? "O driver livre nouveau está em uso: funciona para o desktop, mas rende bem menos em jogos e não tem CUDA, NVENC nem os ajustes de energia. " : "Esta placa NVIDIA está sem driver. ")
                           + how.Split('\n')[0], how.Contains('\n') ? how.Split('\n')[1] : null, ubuntuRecommended != null);
            case GpuVendor.Amd:
                if (drv == "amdgpu") return new(true, "DRIVER OK", "Driver amdgpu (código aberto, parte do kernel e do Mesa) em uso: é o recomendado para placas AMD.", null, false);
                if (drv == "radeon") return new(false, "DRIVER ANTIGO", "O driver radeon está em uso: serve para placas antigas (GCN 1.0 e 1.1 podem usar o amdgpu com os parâmetros de kernel amdgpu.si_support=1 e amdgpu.cik_support=1). Em placas recentes, o certo é o amdgpu.", null, false);
                return new(false, "SEM DRIVER", "Esta placa AMD está sem driver de kernel. Atualize o kernel e o pacote linux-firmware.", null, false);
            case GpuVendor.Intel:
                return drv is "i915" or "xe" ? new(true, "DRIVER OK", $"Driver {drv} (código aberto, parte do kernel e do Mesa) em uso.", null, false)
                                             : new(false, "SEM DRIVER", "Esta GPU Intel está sem driver de kernel (i915 ou xe). Atualize o kernel e o linux-firmware.", null, false);
            default:
                return new(drv.Length > 0, drv.Length > 0 ? "EM USO" : "SEM DRIVER", drv.Length > 0 ? $"Driver {drv} em uso." : "Sem driver de kernel.", null, false);
        }
    }

    private static string? NvidiaVersion(GpuDevice g) => g.HasNvml && !g.Suspended ? NvmlLib.DriverVersion() : null;

    /// <summary>Texto e comando para instalar o driver da NVIDIA conforme a distribuição (linha 1: o que fazer; linha 2: o comando).</summary>
    public static string Install(OsDetails os, string vendor, string? ubuntuRecommended)
    {
        string like = (os.Id + " " + os.IdLike).ToLowerInvariant();
        if (like.Contains("ubuntu") || like.Contains("debian") || like.Contains("mint") || like.Contains("pop"))
            return like.Contains("ubuntu") || like.Contains("pop") || like.Contains("mint")
                ? ubuntuRecommended != null ? $"O Ubuntu recomenda o pacote {ubuntuRecommended}. Use o botão para instalar e reinicie o PC.\nsudo ubuntu-drivers autoinstall"
                                            : "Instale o driver recomendado com o ubuntu-drivers (pacote ubuntu-drivers-common) e reinicie o PC.\nsudo ubuntu-drivers autoinstall"
                : "No Debian, ative o repositório non-free e instale o driver, depois reinicie o PC.\nsudo apt install nvidia-driver";
        if (like.Contains("fedora") || like.Contains("rhel"))
            return "No Fedora o driver vem do RPM Fusion: ative o repositório \"nonfree\" e instale o akmod-nvidia; espere uns 5 minutos e reinicie.\nsudo dnf install akmod-nvidia";
        if (like.Contains("arch") || like.Contains("manjaro") || like.Contains("endeavour") || like.Contains("cachy"))
            return "No Arch o driver é o pacote nvidia (ou nvidia-open nas placas RTX/GTX 16 e mais novas); reinicie depois.\nsudo pacman -S nvidia nvidia-utils";
        if (like.Contains("suse"))
            return "No openSUSE, adicione o repositório da NVIDIA e instale o driver G06; reinicie depois.\nsudo zypper install nvidia-driver-G06-kmp-default";
        return "Instale o driver proprietário pelo gerenciador de pacotes da sua distribuição e reinicie o PC.";
    }

    /// <summary>Pacote recomendado pelo ubuntu-drivers ("driver : nvidia-driver-550 - distro non-free recommended").</summary>
    public static string? UbuntuRecommended()
    {
        var r = Shell.Run("ubuntu-drivers", new[] { "devices" }, 20000);
        if (!r.Ok) return null;
        foreach (var line in r.Out.Split('\n'))
        {
            var m = Regex.Match(line, @"driver\s*:\s*(\S+)\s*-.*recommended");
            if (m.Success) return m.Groups[1].Value;
        }
        return null;
    }

    /// <summary>
    /// Instala o driver recomendado pelo ubuntu-drivers, guardando os pacotes que entraram (e os que saíram) em
    /// <see cref="AppSettings.PackageChanges"/>: o DESFAZER da página os remove e devolve os de antes (requisito 4).
    /// </summary>
    public static (RootResult Result, string? Warning) InstallUbuntuDrivers(AppSettings s, Action persist) =>
        Packages.Track("gpu-driver", "driver da GPU (ubuntu-drivers)", "apt", s, persist,
                       () => Elevate.Run("ubuntu-drivers", new[] { "autoinstall" }, 1800000));

    // ---------------- microcódigo ----------------

    public static string Microcode(CpuDetails cpu)
    {
        string pkg = cpu.Vendor == "Intel" ? "intel-microcode" : cpu.Vendor == "AMD" ? "amd-ucode / amd64-microcode" : "";
        return cpu.Microcode != null ? $"Versão do microcódigo em uso: {cpu.Microcode}" + (pkg.Length > 0 ? $" (pacote {pkg})" : "") : "O kernel não informa a versão do microcódigo (comum em ARM e máquinas virtuais).";
    }

    // ---------------- firmware (fwupd / LVFS) ----------------

    public static (List<FirmwareUpdate> Updates, string Message) FirmwareScan(bool refresh)
    {
        if (!Shell.Has("fwupdmgr")) return (new List<FirmwareUpdate>(), "O fwupd não está instalado (pacote fwupd, na página Instalar apps).");
        if (refresh) Shell.Run("fwupdmgr", new[] { "refresh", "--force" }, 60000);
        var r = Shell.Run("fwupdmgr", new[] { "get-updates", "--json" }, 60000);
        return ParseFwupd(r.Out, r.ExitCode);
    }

    public static (List<FirmwareUpdate> Updates, string Message) ParseFwupd(string json, int exitCode)
    {
        var list = new List<FirmwareUpdate>();
        try
        {
            int start = json.IndexOf('{');
            if (start < 0) return (list, exitCode == 2 ? "Nenhuma atualização de firmware disponível." : "O fwupd não devolveu dados (sem internet para consultar o LVFS, ou sem dispositivos compatíveis).");
            using var doc = JsonDocument.Parse(json[start..]);
            if (doc.RootElement.TryGetProperty("Devices", out var devs) && devs.ValueKind == JsonValueKind.Array)
                foreach (var d in devs.EnumerateArray())
                {
                    string name = Str(d, "Name"), vendor = Str(d, "Vendor"), cur = Str(d, "Version");
                    if (!d.TryGetProperty("Releases", out var rel) || rel.ValueKind != JsonValueKind.Array) continue;
                    var first = rel.EnumerateArray().FirstOrDefault();
                    if (first.ValueKind != JsonValueKind.Object) continue;
                    list.Add(new FirmwareUpdate(name, vendor, cur, Str(first, "Version"), Str(first, "Summary")));
                }
        }
        catch (JsonException) { return (list, "Não entendi a resposta do fwupd."); }
        return (list, list.Count == 0 ? "Nenhuma atualização de firmware disponível." : $"{list.Count} atualização(ões) de firmware disponível(is).");
    }

    private static string Str(JsonElement e, string k) => e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    /// <summary>
    /// O InfoPC só verifica as atualizações de firmware: instalar não tem como ser desfeito com garantia (muitos dispositivos não aceitam
    /// voltar de versão), então fica com o usuário, por este comando no terminal (D-027).
    /// </summary>
    public const string FirmwareUpdateCommand = "sudo fwupdmgr update";

    // ---------------- relatório de drivers (o "backup" do Linux) ----------------

    /// <summary>Texto com os módulos carregados e os dispositivos com seus drivers, para guardar ou mandar para suporte.</summary>
    public static string ExportText(OsDetails os)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"InfoPC {AppInfo.Version} · lista de drivers · {DateTime.Now:dd/MM/yyyy HH:mm}");
        sb.AppendLine($"{os.Name} · kernel {os.Kernel} · {os.Arch}");
        sb.AppendLine();
        sb.AppendLine("== Dispositivos PCI e seus drivers ==");
        foreach (var d in PciDevices()) sb.AppendLine($"{d.Slot,-14} {d.Category,-22} {d.Name}  ->  {d.Driver ?? "(sem driver)"}");
        sb.AppendLine();
        sb.AppendLine("== Módulos do kernel carregados ==");
        foreach (var m in Modules())
        {
            string ver = Sys.Read($"/sys/module/{m.Name}/version") ?? "";
            sb.AppendLine($"{m.Name,-28} {m.SizeBytes / 1024,8} KB  usado por {m.UsedBy}{(ver.Length > 0 ? "  versão " + ver : "")}");
        }
        return sb.ToString();
    }
}
