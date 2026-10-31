using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace InfoPC;

/// <summary>Adaptadores de rede: tipo, velocidade do link, endereços, driver e, no Wi-Fi, a qualidade do sinal. MAC e nomes de rede ficam de fora.</summary>
public static class NetInfo
{
    public static List<InfoNode> Adapters()
    {
        var list = new List<InfoNode>();
        NetworkInterface[] nics;
        try { nics = NetworkInterface.GetAllNetworkInterfaces(); }
        catch (Exception ex) { return new List<InfoNode> { new InfoNode { Header = "Erro ao ler", Value = ex.Message } }; }

        foreach (var n in nics.Where(x => x.NetworkInterfaceType != NetworkInterfaceType.Loopback).OrderByDescending(x => x.OperationalStatus == OperationalStatus.Up))
        {
            bool wifi = Sys.DirExists($"/sys/class/net/{n.Name}/wireless");
            bool physical = Sys.DirExists($"/sys/class/net/{n.Name}/device");
            string kind = wifi ? "Wi-Fi" : physical ? "Ethernet (cabo)" : n.NetworkInterfaceType == NetworkInterfaceType.Tunnel ? "Túnel / VPN" : "Virtual";
            var node = new InfoNode { Header = n.Name, Value = $"{kind} · {(n.OperationalStatus == OperationalStatus.Up ? "conectado" : "desconectado")}" };
            long? speed = Sys.Long($"/sys/class/net/{n.Name}/speed");
            if (speed is > 0) node.Add("Velocidade do link", $"{speed} Mbps");
            if (Sys.LinkName($"/sys/class/net/{n.Name}/device/driver") is { } drv) node.Add("Driver", drv);
            try
            {
                var props = n.GetIPProperties();
                var v4 = props.UnicastAddresses.Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork).Select(a => a.Address.ToString()).ToList();
                var v6 = props.UnicastAddresses.Where(a => a.Address.AddressFamily == AddressFamily.InterNetworkV6 && !a.Address.IsIPv6LinkLocal).Select(a => a.Address.ToString()).ToList();
                if (v4.Count > 0) node.Add("IPv4", string.Join(", ", v4));
                if (v6.Count > 0) node.Add("IPv6", string.Join(", ", v6));
                var gw = props.GatewayAddresses.Select(g => g.Address.ToString()).Where(a => a != "0.0.0.0").ToList();
                if (gw.Count > 0) node.Add("Gateway", string.Join(", ", gw));
                var dns = props.DnsAddresses.Select(d => d.ToString()).ToList();
                if (dns.Count > 0) node.Add("DNS", string.Join(", ", dns));
            }
            catch { }
            if (wifi && WifiQuality(n.Name) is { } q) node.Add("Qualidade do sinal", q);
            list.Add(node);
        }
        if (list.Count == 0) list.Add(new InfoNode { Header = "Nenhum adaptador encontrado" });
        return list;
    }

    /// <summary>/proc/net/wireless: "wlp2s0: 0000   70.  -40.  -256 ..." → qualidade do link (de 70) e nível em dBm.</summary>
    public static string? WifiQuality(string iface)
    {
        foreach (var line in Sys.Lines("/proc/net/wireless"))
        {
            int i = line.IndexOf(':');
            if (i < 0 || line[..i].Trim() != iface) continue;
            var f = line[(i + 1)..].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (f.Length >= 3 && Num.D(f[1].TrimEnd('.')) is { } link && Num.D(f[2].TrimEnd('.')) is { } level)
                return $"{Math.Clamp(link / 70 * 100, 0, 100):0}% ({level:0} dBm)";
        }
        return null;
    }
}
