using System.Text;

namespace InfoPC;

/// <summary>
/// No Linux o FPS dentro do jogo é medido pelo MangoHud (uma camada Vulkan/OpenGL), não por ETW como no Windows.
/// Este módulo traduz os itens do overlay do InfoPC para a configuração do MangoHud.
/// </summary>
public static class MangoHud
{
    /// <summary>Item do overlay (OverlayMetrics.Key) → linhas de configuração do MangoHud. null = o MangoHud não tem equivalente.</summary>
    private static readonly Dictionary<string, string[]?> Map = new()
    {
        ["fps"] = new[] { "fps" },
        ["frametime"] = new[] { "frametime" },
        ["low1"] = new[] { "fps_metrics=avg,0.01" },
        ["graph"] = new[] { "frame_timing" },
        ["gpu_load"] = new[] { "gpu_stats" },
        ["gpu_temp"] = new[] { "gpu_temp" },
        ["gpu_hotspot"] = new[] { "gpu_junction_temp" },
        ["gpu_clock"] = new[] { "gpu_core_clock" },
        ["gpu_memclock"] = new[] { "gpu_mem_clock" },
        ["gpu_power"] = new[] { "gpu_power" },
        ["gpu_volt"] = new[] { "gpu_voltage" },
        ["gpu_fan"] = new[] { "gpu_fan" },
        ["gpu_perfcap"] = null,
        ["vram_used"] = new[] { "vram" },
        ["vram_pct"] = null,
        ["vram_temp"] = new[] { "gpu_mem_temp" },
        ["cpu_load"] = new[] { "cpu_stats" },
        ["cpu_maxcore"] = new[] { "core_load" },
        ["cpu_temp"] = new[] { "cpu_temp" },
        ["cpu_clock"] = new[] { "cpu_mhz" },
        ["cpu_clockmax"] = null,
        ["cpu_power"] = new[] { "cpu_power" },
        ["ram_used"] = new[] { "ram" },
        ["ram_pct"] = null,
        ["game"] = new[] { "exec_name" },
        ["clock"] = new[] { "time" },
    };

    public static bool Supports(string key) => Map.TryGetValue(key, out var v) && v != null;

    public static string ConfigPath
    {
        get
        {
            string cfg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is { Length: > 0 } x ? x : Sys.Join(Sys.Home, ".config");
            return Sys.Join(cfg, "MangoHud", "MangoHud.conf");
        }
    }

    public static string Position(string corner) => corner switch { "TR" => "top-right", "BL" => "bottom-left", "BR" => "bottom-right", _ => "top-left" };

    public static string Build(IEnumerable<string> keys, string corner, double scale)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Gerado pelo InfoPC (página Overlay). Gerar de novo sobrescreve este arquivo; o anterior vira MangoHud.conf.infopc.bak.");
        sb.AppendLine("legacy_layout=false");
        sb.AppendLine("background_alpha=0.45");
        sb.AppendLine("round_corners=8");
        sb.AppendLine($"position={Position(corner)}");
        sb.AppendLine($"font_size={Math.Round(24 * Math.Clamp(scale, 0.5, 2.5))}");
        var seen = new HashSet<string>();
        foreach (var k in keys)
            if (Map.TryGetValue(k, out var lines) && lines != null)
                foreach (var l in lines) if (seen.Add(l)) sb.AppendLine(l);
        return sb.ToString();
    }

    /// <summary>Grava o arquivo (com cópia do anterior). Devolve o caminho gravado.</summary>
    public static string Write(string content)
    {
        string path = ConfigPath;
        Directory.CreateDirectory(Path.GetDirectoryName(Sys.P(path))!);
        if (File.Exists(Sys.P(path)) && !File.ReadAllText(Sys.P(path)).StartsWith("# Gerado pelo InfoPC", StringComparison.Ordinal))
            File.Copy(Sys.P(path), Sys.P(path) + ".infopc.bak", true);
        File.WriteAllText(Sys.P(path), content, new UTF8Encoding(false));
        return path;
    }
}
