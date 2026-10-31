using System.Text.Json;
using System.Text.RegularExpressions;

namespace InfoPC;

public sealed record SmartAttr(int Id, string Name, int Value, int Worst, int Thresh, long Raw);

public sealed class SmartInfo
{
    public string Device { get; init; } = "";
    public string Protocol { get; init; } = "";      // "ATA" | "NVMe" | "SCSI"
    public string Model { get; init; } = "";
    public string Serial { get; init; } = "";
    public string Firmware { get; init; } = "";
    public long CapacityBytes { get; init; }
    public bool? Passed { get; init; }
    public double? TempC { get; set; }
    public long? PowerOnHours { get; init; }
    public long? PowerCycles { get; init; }
    public int? RotationRate { get; init; }
    public List<SmartAttr> Attrs { get; } = new();
    // NVMe
    public int? CriticalWarning { get; init; }
    public int? PercentUsed { get; init; }
    public int? AvailableSpare { get; init; }
    public int? SpareThreshold { get; init; }
    public long? MediaErrors { get; init; }
    public long? ErrorLogEntries { get; init; }
    public long? UnsafeShutdowns { get; init; }
    public double? WrittenBytes { get; init; }
    public double? ReadBytes { get; init; }
    public string? Error { get; init; }

    public bool IsNvme => Protocol.Equals("NVMe", StringComparison.OrdinalIgnoreCase);
    public SmartAttr? Attr(int id) => Attrs.FirstOrDefault(a => a.Id == id);
}

/// <summary>
/// S.M.A.R.T. pelo smartctl (smartmontools) em JSON. Ler o disco exige root, então o botão da tela faz um único pedido de senha
/// para todos os discos (D-019). Nada é escrito no disco.
/// </summary>
public static class SmartReader
{
    private const string Marker = "@@INFOPC ";

    /// <summary>Lê todos os discos de uma vez. Roteiro fixo; os nomes dos dispositivos vão como argumentos, nunca dentro do texto do roteiro.</summary>
    public static (List<SmartInfo> Infos, RootResult Result) ReadAll(IEnumerable<PhysicalDisk> disks)
    {
        var devs = disks.Select(d => d.DevPath).Where(p => Regex.IsMatch(p, @"^/dev/[a-z0-9]+$")).ToList();
        if (devs.Count == 0) return (new List<SmartInfo>(), new RootResult(true, "", ""));
        if (Shell.Which("smartctl") == null)
            return (new List<SmartInfo>(), new RootResult(false, "", "O smartmontools não está instalado. Instale o pacote 'smartmontools' (página Instalar apps) e tente de novo."));
        const string script = "for d in \"$@\"; do echo \"" + Marker + "$d\"; smartctl -a -j \"$d\"; done; exit 0";
        var args = new List<string> { "-c", script, "infopc" };
        args.AddRange(devs);
        var res = Elevate.Run("sh", args, 120000);
        return (res.Ok ? ParseBatch(res.Output) : new List<SmartInfo>(), res);
    }

    public static List<SmartInfo> ParseBatch(string output)
    {
        var list = new List<SmartInfo>();
        foreach (var part in output.Split(Marker, StringSplitOptions.RemoveEmptyEntries))
        {
            int nl = part.IndexOf('\n');
            if (nl < 0) continue;
            string dev = part[..nl].Trim(), json = part[(nl + 1)..].Trim();
            if (json.Length == 0) continue;
            var info = Parse(json, dev);
            if (info != null) list.Add(info);
        }
        return list;
    }

    public static SmartInfo? Parse(string json, string deviceName = "")
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var r = doc.RootElement;
            string device = Str(r, "device", "name") is { Length: > 0 } dn ? dn : deviceName;

            // erro do smartctl (sem permissão, dispositivo desconhecido): vem em smartctl.messages
            if (r.TryGetProperty("smartctl", out var sc) && sc.TryGetProperty("messages", out var msgs) && msgs.ValueKind == JsonValueKind.Array
                && !r.TryGetProperty("smart_status", out _) && !r.TryGetProperty("ata_smart_attributes", out _) && !r.TryGetProperty("nvme_smart_health_information_log", out _))
            {
                string text = string.Join(" ", msgs.EnumerateArray().Select(m => Str(m, "string")).Where(s => s.Length > 0));
                return new SmartInfo { Device = device, Error = text.Length > 0 ? text : "o smartctl não conseguiu ler este disco" };
            }

            string protocol = Str(r, "device", "protocol");
            var nv = r.TryGetProperty("nvme_smart_health_information_log", out var n) ? n : default;
            bool isNvme = protocol.Equals("NVMe", StringComparison.OrdinalIgnoreCase) || nv.ValueKind == JsonValueKind.Object;

            var info = new SmartInfo
            {
                Device = device, Protocol = isNvme ? "NVMe" : protocol,
                Model = Str(r, "model_name"), Serial = Str(r, "serial_number"), Firmware = Str(r, "firmware_version"),
                CapacityBytes = Lng(r, "user_capacity", "bytes") ?? 0,
                Passed = r.TryGetProperty("smart_status", out var ss) && ss.TryGetProperty("passed", out var pv) && pv.ValueKind is JsonValueKind.True or JsonValueKind.False
                         ? pv.GetBoolean() : null,
                TempC = Lng(r, "temperature", "current"),
                PowerOnHours = Lng(r, "power_on_time", "hours") ?? (nv.ValueKind == JsonValueKind.Object ? Lng(nv, "power_on_hours") : null),
                PowerCycles = Lng(r, "power_cycle_count") ?? (nv.ValueKind == JsonValueKind.Object ? Lng(nv, "power_cycles") : null),
                RotationRate = (int?)Lng(r, "rotation_rate"),
                CriticalWarning = nv.ValueKind == JsonValueKind.Object ? (int?)Lng(nv, "critical_warning") : null,
                PercentUsed = nv.ValueKind == JsonValueKind.Object ? (int?)Lng(nv, "percentage_used") : null,
                AvailableSpare = nv.ValueKind == JsonValueKind.Object ? (int?)Lng(nv, "available_spare") : null,
                SpareThreshold = nv.ValueKind == JsonValueKind.Object ? (int?)Lng(nv, "available_spare_threshold") : null,
                MediaErrors = nv.ValueKind == JsonValueKind.Object ? Lng(nv, "media_errors") : null,
                ErrorLogEntries = nv.ValueKind == JsonValueKind.Object ? Lng(nv, "num_err_log_entries") : null,
                UnsafeShutdowns = nv.ValueKind == JsonValueKind.Object ? Lng(nv, "unsafe_shutdowns") : null,
                // a unidade de dados do NVMe é 1000 blocos de 512 bytes
                WrittenBytes = nv.ValueKind == JsonValueKind.Object && Lng(nv, "data_units_written") is { } w ? w * 512000.0 : null,
                ReadBytes = nv.ValueKind == JsonValueKind.Object && Lng(nv, "data_units_read") is { } rd ? rd * 512000.0 : null,
            };
            if (info.TempC == null && nv.ValueKind == JsonValueKind.Object) info.TempC = Lng(nv, "temperature");

            if (r.TryGetProperty("ata_smart_attributes", out var ata) && ata.TryGetProperty("table", out var table) && table.ValueKind == JsonValueKind.Array)
                foreach (var a in table.EnumerateArray())
                    info.Attrs.Add(new SmartAttr((int)(Lng(a, "id") ?? 0), Str(a, "name"), (int)(Lng(a, "value") ?? 0), (int)(Lng(a, "worst") ?? 0),
                                                 (int)(Lng(a, "thresh") ?? 0), Lng(a, "raw", "value") ?? 0));
            return info;
        }
        catch (JsonException) { return null; }
    }

    private static string Str(JsonElement e, params string[] path)
    {
        foreach (var p in path)
        {
            if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(p, out e)) return "";
        }
        return e.ValueKind == JsonValueKind.String ? e.GetString() ?? "" : "";
    }

    private static long? Lng(JsonElement e, params string[] path)
    {
        foreach (var p in path)
        {
            if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(p, out e)) return null;
        }
        return e.ValueKind == JsonValueKind.Number && e.TryGetInt64(out long v) ? v : null;
    }
}
