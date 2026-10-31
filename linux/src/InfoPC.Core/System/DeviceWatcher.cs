namespace InfoPC;

public enum DeviceChangeKind { Storage, Maker, Usb }

public sealed record DeviceChange(bool Added, DeviceChangeKind Kind, string Title, string Detail)
{
    public string Headline => (Added ? "Conectado" : "Removido") + " · " + Kind switch
    {
        DeviceChangeKind.Storage => "armazenamento", DeviceChangeKind.Maker => "placa Maker", _ => "dispositivo USB"
    };
}

/// <summary>
/// Avisa quando um pendrive, HD externo, placa Arduino/ESP32 ou outro dispositivo USB entra ou sai. No Windows o aviso vem do WM_DEVICECHANGE;
/// no Linux basta comparar a lista do sysfs a cada poucos segundos (barato: dezenas de arquivos pequenos). A primeira leitura só guarda a lista.
/// </summary>
public sealed class DeviceWatcher
{
    private Dictionary<string, DeviceChange>? _known;

    public static Dictionary<string, DeviceChange> Snapshot()
    {
        var d = new Dictionary<string, DeviceChange>();

        // dispositivos USB (as interfaces têm ":" no nome e os hubs são classe 09)
        foreach (var n in Sys.List("/sys/bus/usb/devices"))
        {
            if (n.Contains(':') || n.StartsWith("usb", StringComparison.Ordinal)) continue;
            string p = "/sys/bus/usb/devices/" + n;
            if (Sys.Read(p + "/idVendor") is not { } vid) continue;
            if (Sys.Read(p + "/bDeviceClass") == "09") continue;
            string pid = Sys.Read(p + "/idProduct") ?? "";
            string name = Sys.Read(p + "/product") ?? $"USB {vid}:{pid}";
            string maker = Sys.Read(p + "/manufacturer") ?? "";
            var kind = Maker.ChipByVid(vid) != null ? DeviceChangeKind.Maker : DeviceChangeKind.Usb;
            d["usb:" + n + ":" + vid + pid] = new DeviceChange(true, kind, name, string.Join(" · ", new[] { maker, $"VID {vid.ToUpperInvariant()} PID {pid.ToUpperInvariant()}" }.Where(s => s.Length > 0)));
        }

        // discos removíveis (pendrive, cartão, HD externo)
        foreach (var n in Sys.List("/sys/block"))
        {
            if (!(n.StartsWith("sd", StringComparison.Ordinal) || n.StartsWith("mmcblk", StringComparison.Ordinal) || n.StartsWith("sr", StringComparison.Ordinal))) continue;
            bool removable = Sys.Read($"/sys/block/{n}/removable") == "1";
            string real = Sys.RealPath($"/sys/block/{n}") ?? "";
            if (!removable && !real.Contains("/usb")) continue;
            long sectors = Sys.Long($"/sys/block/{n}/size") ?? 0;
            string model = ((Sys.Read($"/sys/block/{n}/device/vendor") ?? "") + " " + (Sys.Read($"/sys/block/{n}/device/model") ?? "")).Trim();
            if (model.Length == 0) model = n;
            d["blk:" + n] = new DeviceChange(true, DeviceChangeKind.Storage, model, sectors > 0 ? $"/dev/{n} · {Fmt.Capacity(sectors * 512.0)}" : $"/dev/{n} · sem mídia");
        }
        return d;
    }

    /// <summary>As mudanças desde a leitura anterior (vazio na primeira chamada).</summary>
    public List<DeviceChange> Poll()
    {
        var now = Snapshot();
        var changes = new List<DeviceChange>();
        if (_known != null)
        {
            foreach (var (k, v) in now) if (!_known.ContainsKey(k)) changes.Add(v with { Added = true });
            foreach (var (k, v) in _known) if (!now.ContainsKey(k)) changes.Add(v with { Added = false });
        }
        _known = now;
        return changes;
    }
}
