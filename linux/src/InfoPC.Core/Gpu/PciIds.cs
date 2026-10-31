namespace InfoPC;

/// <summary>
/// Nomes de fabricante e dispositivo PCI a partir do banco pci.ids (pacote hwdata / pciutils). Lê o arquivo em fluxo e para
/// assim que acha o dispositivo; sem o arquivo, devolve null e o chamador usa os códigos.
/// </summary>
public static class PciIds
{
    private static readonly string[] Paths =
    {
        "/usr/share/hwdata/pci.ids", "/usr/share/misc/pci.ids", "/usr/share/pci.ids", "/var/lib/pciutils/pci.ids", "/usr/share/hwdata/pci.ids.gz"
    };

    public static string? Find()
    {
        foreach (var p in Paths)
            if (!p.EndsWith(".gz") && Sys.FileExists(p)) return p;
        return null;
    }

    /// <summary>Nome do dispositivo ("AD104 [GeForce RTX 4070]") e do fabricante ("NVIDIA Corporation").</summary>
    public static (string? Vendor, string? Device) Lookup(string vendorHex, string deviceHex, string? file = null)
    {
        file ??= Find();
        if (file == null) return (null, null);
        vendorHex = vendorHex.ToLowerInvariant(); deviceHex = deviceHex.ToLowerInvariant();
        string? vendor = null;
        bool inVendor = false;
        try
        {
            using var r = new StreamReader(Sys.P(file));
            string? line;
            while ((line = r.ReadLine()) != null)
            {
                if (line.Length == 0 || line[0] == '#') continue;
                if (line[0] != '\t')
                {
                    if (inVendor) return (vendor, null); // saiu do bloco do fabricante sem achar o dispositivo
                    if (line.Length > 6 && line.StartsWith(vendorHex, StringComparison.OrdinalIgnoreCase) && line[4] == ' ')
                    {
                        vendor = line[4..].Trim();
                        inVendor = true;
                    }
                    else if (line.StartsWith("C ")) break; // classes: acabou a parte de fabricantes
                    continue;
                }
                if (inVendor && line.Length > 6 && line[1] != '\t' && MemoryExtensions.Equals(line.AsSpan(1, 4), deviceHex, StringComparison.OrdinalIgnoreCase))
                    return (vendor, line[5..].Trim());
            }
        }
        catch { }
        return (vendor, null);
    }

    /// <summary>Vários dispositivos de uma vez, lendo o arquivo uma só vez (a lista de drivers tem dezenas). Chave: "vvvv:dddd" em minúsculas.</summary>
    public static Dictionary<string, (string? Vendor, string? Device)> LookupMany(IEnumerable<(string Vendor, string Device)> wanted, string? file = null)
    {
        var want = wanted.Select(w => (V: w.Vendor.ToLowerInvariant(), D: w.Device.ToLowerInvariant())).Distinct().ToList();
        var result = want.ToDictionary(w => $"{w.V}:{w.D}", _ => ((string?)null, (string?)null));
        file ??= Find();
        if (file == null || want.Count == 0) return result;
        var vendors = want.Select(w => w.V).ToHashSet();
        var byVendor = want.GroupBy(w => w.V).ToDictionary(g => g.Key, g => g.Select(x => x.D).ToHashSet());
        string? curVendor = null, curName = null;
        try
        {
            foreach (var line in File.ReadLines(Sys.P(file)))
            {
                if (line.Length == 0 || line[0] == '#') continue;
                if (line[0] != '	')
                {
                    if (line.StartsWith("C ")) break;
                    curVendor = line.Length > 6 && line[4] == ' ' ? line[..4].ToLowerInvariant() : null;
                    curName = curVendor != null && vendors.Contains(curVendor) ? line[4..].Trim() : null;
                    if (curName != null)
                        foreach (var d in byVendor[curVendor!]) result[$"{curVendor}:{d}"] = (curName, result[$"{curVendor}:{d}"].Item2);
                    continue;
                }
                if (curName == null || line.Length < 7 || line[1] == '	') continue;
                string dev = line.Substring(1, 4).ToLowerInvariant();
                if (byVendor[curVendor!].Contains(dev)) result[$"{curVendor}:{dev}"] = (curName, line[5..].Trim());
            }
        }
        catch { }
        return result;
    }

    /// <summary>Nome comercial da GPU: o texto entre colchetes do pci.ids, com o fabricante na frente.</summary>
    public static string GpuName(string vendorHex, string deviceHex, string? file = null)
    {
        var (vendor, device) = Lookup(vendorHex, deviceHex, file);
        return MarketingName(vendorHex, vendor, device, deviceHex);
    }

    public static string MarketingName(string vendorHex, string? vendor, string? device, string deviceHex)
    {
        string brand = vendorHex.ToUpperInvariant() switch { "10DE" => "NVIDIA", "1002" => "AMD", "8086" => "Intel", _ => ShortVendor(vendor) };
        if (device == null) return $"{(brand.Length > 0 ? brand + " " : "")}GPU {vendorHex.ToUpperInvariant()}:{deviceHex.ToUpperInvariant()}";
        int a = device.IndexOf('['), b = device.LastIndexOf(']');
        string inner = a >= 0 && b > a ? device[(a + 1)..b] : device;
        // AMD lista várias placas no mesmo código ("Radeon RX 7600/7600 XT/7700S"): fica a primeira
        string first = inner.Split('/')[0].Trim();
        if (a >= 0 && first.Length > 0 && !first.Contains("Radeon") && vendorHex.Equals("1002", StringComparison.OrdinalIgnoreCase) && char.IsDigit(first[0]))
            first = "Radeon RX " + first;
        if (a < 0 && vendorHex.Equals("1002", StringComparison.OrdinalIgnoreCase)) first = $"Radeon Graphics ({first})"; // APU: só o código do chip
        return $"{brand} {first}".Trim();
    }

    private static string ShortVendor(string? v) =>
        string.IsNullOrWhiteSpace(v) ? "" : v.Replace("Corporation", "").Replace("Corp.", "").Replace("Inc.", "").Replace(",", "").Trim();
}
