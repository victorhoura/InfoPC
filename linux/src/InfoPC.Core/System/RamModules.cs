namespace InfoPC;

public sealed class RamModule
{
    public string Locator = "", BankLocator = "", Type = "", FormFactor = "", Manufacturer = "", PartNumber = "", Serial = "";
    public long SizeMb;
    public int SpeedMts, ConfiguredMts, TotalWidth, DataWidth, Rank;
    public double? MinV, MaxV, CfgV;
    public bool Populated => SizeMb > 0;
}

public sealed record RamArray(long MaxCapacityMb, int Slots, string Ecc);
public sealed record RamLayout(List<RamModule> Modules, RamArray? Array);

/// <summary>
/// Módulos de RAM (tipo, velocidade, fabricante, part number): só o SMBIOS tem, e ele exige root.
/// O dmidecode é chamado sob demanda, pelo botão da tela (D-019).
/// </summary>
public static class RamModules
{
    public static RamLayout? ReadWithPrompt(out RootResult result)
    {
        result = Elevate.Run("dmidecode", new[] { "-t", "memory" }, 30000);
        return result.Ok ? Parse(result.Output) : null;
    }

    public static RamLayout Parse(string dmidecode)
    {
        var modules = new List<RamModule>();
        RamArray? arr = null;
        foreach (var rec in Records(dmidecode))
        {
            if (rec.Title == "Memory Device")
            {
                var f = rec.Fields;
                string G(string k) => f.GetValueOrDefault(k, "");
                var m = new RamModule
                {
                    Locator = G("Locator"), BankLocator = G("Bank Locator"), Type = Clean(G("Type")), FormFactor = Clean(G("Form Factor")),
                    Manufacturer = Clean(G("Manufacturer")), PartNumber = Clean(G("Part Number")), Serial = Clean(G("Serial Number")),
                    SizeMb = SizeMb(G("Size")), SpeedMts = (int)(Num.Leading(G("Speed")) ?? 0),
                    ConfiguredMts = (int)(Num.Leading(G("Configured Memory Speed")) ?? Num.Leading(G("Configured Clock Speed")) ?? 0),
                    TotalWidth = (int)(Num.Leading(G("Total Width")) ?? 0), DataWidth = (int)(Num.Leading(G("Data Width")) ?? 0),
                    Rank = (int)(Num.Leading(G("Rank")) ?? 0),
                    MinV = Num.Leading(G("Minimum Voltage")), MaxV = Num.Leading(G("Maximum Voltage")), CfgV = Num.Leading(G("Configured Voltage")),
                };
                modules.Add(m);
            }
            else if (rec.Title == "Physical Memory Array" && arr == null)
            {
                var f = rec.Fields;
                long cap = SizeMb(f.GetValueOrDefault("Maximum Capacity", ""));
                arr = new RamArray(cap, (int)(Num.L(f.GetValueOrDefault("Number Of Devices", "")) ?? 0), f.GetValueOrDefault("Error Correction Type", "None"));
            }
        }
        return new RamLayout(modules, arr);
    }

    private static string Clean(string s)
    {
        s = s.Trim();
        return s is "Unknown" or "Not Specified" or "None" or "Undefined" or "[Empty]" or "0000" or "00000000" or "NO DIMM" ? "" : s;
    }

    /// <summary>"16 GB" / "16384 MB" / "No Module Installed" → MB.</summary>
    public static long SizeMb(string s)
    {
        double? n = Num.Leading(s);
        if (n == null) return 0;
        s = s.ToUpperInvariant();
        return (long)(s.Contains("TB") ? n * 1024 * 1024 : s.Contains("GB") ? n * 1024 : s.Contains("KB") ? n / 1024 : n);
    }

    private sealed record Rec(string Title, Dictionary<string, string> Fields);

    private static IEnumerable<Rec> Records(string text)
    {
        foreach (var block in text.Replace("\r", "").Split("\n\n", StringSplitOptions.RemoveEmptyEntries))
        {
            var lines = block.Split('\n');
            int h = Array.FindIndex(lines, l => l.StartsWith("Handle ", StringComparison.Ordinal));
            if (h < 0 || h + 1 >= lines.Length) continue;
            var fields = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var l in lines.Skip(h + 2))
            {
                if (!l.StartsWith('\t') && !l.StartsWith(' ')) continue;
                int i = l.IndexOf(':');
                if (i <= 0) continue;
                fields.TryAdd(l[..i].Trim(), l[(i + 1)..].Trim());
            }
            yield return new Rec(lines[h + 1].Trim(), fields);
        }
    }
}
