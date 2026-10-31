using System.Diagnostics;

namespace InfoPC;

/// <summary>
/// Motor de efeitos de iluminação (cor fixa, respiração, arco-íris, temperatura/carga). Calcula os quadros e os envia às pontes (OpenRGB).
/// A matemática dos efeitos é a da edição Windows. Só reenvia quando a cor muda (ou a cada 2 s, para o fabricante não retomar as luzes).
/// </summary>
public sealed class LightEngine
{
    public string Effect { get; set; } = "static";
    public (byte R, byte G, byte B) Color { get; set; } = (255, 255, 255);
    public double Brightness { get; set; } = 100;
    public double Speed { get; set; } = 5;
    public Func<string, float?> LiveValue { get; set; } = _ => null;
    public Action<string>? Diag { get; set; }

    private readonly Dictionary<string, ((byte, byte, byte)[] Colors, long At)> _last = new();
    private readonly Dictionary<string, int> _errByDevice = new();
    private float _smooth = float.NaN;

    public void Reset() { _last.Clear(); _smooth = float.NaN; }

    public static (byte, byte, byte) Hsv(double h, double s, double v)
    {
        h = ((h % 360) + 360) % 360;
        double c = v * s, x = c * (1 - Math.Abs(h / 60 % 2 - 1)), m = v - c;
        (double r, double g, double b) = h switch
        {
            < 60 => (c, x, 0.0), < 120 => (x, c, 0.0), < 180 => (0.0, c, x),
            < 240 => (0.0, x, c), < 300 => (x, 0.0, c), _ => (c, 0.0, x)
        };
        return ((byte)Math.Round((r + m) * 255), (byte)Math.Round((g + m) * 255), (byte)Math.Round((b + m) * 255));
    }

    /// <summary>Cor de um LED neste instante (antes do brilho). <paramref name="ledX"/> vai de 0 a 1 ao longo da peça.</summary>
    public static (byte R, byte G, byte B) Compute(string effect, double ledX, double t, double speed, (byte R, byte G, byte B) color, (byte, byte, byte)? live)
    {
        switch (effect)
        {
            case "breath":
                double k = 0.5 - 0.5 * Math.Cos(2 * Math.PI * (0.1 * speed) * t);
                k = 0.04 + 0.96 * k;
                return ((byte)(color.R * k), (byte)(color.G * k), (byte)(color.B * k));
            case "rainbow": return Hsv(ledX * 360 + t * speed * 36, 1, 1);
            case "cpu" or "gpu" or "load": return live ?? (0, 0, 0);
            case "off": return (0, 0, 0);
            default: return color;
        }
    }

    /// <summary>Temperatura ou carga vira matiz: temperatura de azul (35 °C) a vermelho (85 °C); carga de verde (0%) a vermelho (100%).</summary>
    public static (byte, byte, byte) LiveColor(string effect, float value) =>
        effect == "load" ? Hsv(120 * (1 - Math.Clamp(value / 100.0, 0, 1)), 1, 1) : Hsv(240 * (1 - Math.Clamp((value - 35) / 50.0, 0, 1)), 1, 1);

    /// <summary>Um quadro para todos os dispositivos. Devolve o texto "ao vivo" (ex.: "CPU 62 °C").</summary>
    public string Tick(IEnumerable<LightDevice> devices, double t, long nowMs)
    {
        string text = "";
        (byte, byte, byte)? live = null;
        if (Effect is "cpu" or "gpu" or "load")
        {
            float? raw = LiveValue(Effect);
            if (raw == null) { text = "Sensor indisponível."; live = (0, 0, 40); }
            else
            {
                _smooth = float.IsNaN(_smooth) ? raw.Value : _smooth + (raw.Value - _smooth) * 0.08f;
                live = LiveColor(Effect, _smooth);
                text = Effect == "load" ? $"CPU {_smooth:0}% de carga" : $"{(Effect == "cpu" ? "CPU" : "GPU")} {_smooth:0} °C";
            }
        }

        double bright = Brightness / 100.0;
        foreach (var d in devices)
        {
            if (d.LedCount == 0) continue;
            bool have = _last.TryGetValue(d.Key, out var last);
            if (have && nowMs - last.At < d.Backend.MinIntervalMs) continue;

            var cols = new (byte, byte, byte)[d.LedCount];
            for (int i = 0; i < cols.Length; i++)
            {
                var c = Compute(Effect, d.LedX[i], t, Speed, Color, live);
                cols[i] = ((byte)(c.R * bright), (byte)(c.G * bright), (byte)(c.B * bright));
            }
            if (have && nowMs - last.At < 2000 && last.Colors.AsSpan().SequenceEqual(cols)) continue;
            int err = d.Backend.Send(d, cols);
            _last[d.Key] = (cols, nowMs);
            if (!_errByDevice.TryGetValue(d.Key, out int prev) || prev != err)
            {
                _errByDevice[d.Key] = err;
                Diag?.Invoke($"Cores enviadas a \"{d.Name}\" ({d.Backend.Name}, {cols.Length} LEDs, primeiro = {cols[0].Item1},{cols[0].Item2},{cols[0].Item3}): {d.Backend.ErrorText(err)}");
            }
        }
        return text;
    }
}

/// <summary>Acha e liga o OpenRGB: pacote da distribuição, Flatpak ou AppImage na pasta do usuário.</summary>
public static class OpenRgbLocator
{
    public sealed record Launch(string Exe, string[] Args, string Description);

    public static Launch? Find()
    {
        if (Shell.Which("openrgb") is { } exe) return new Launch(exe, new[] { "--server", "--startminimized" }, "openrgb (pacote da distribuição)");
        if (Shell.Has("flatpak") && Shell.Run("flatpak", new[] { "info", "org.openrgb.OpenRGB" }, 4000).Ok)
            return new Launch(Shell.Which("flatpak")!, new[] { "run", "org.openrgb.OpenRGB", "--server", "--startminimized" }, "OpenRGB (Flatpak)");
        string home = Sys.Home;
        foreach (var dir in new[] { Sys.Join(home, "Downloads"), Sys.Join(home, "Applications"), Sys.Join(home, ".local/bin"), Sys.Join(home, "Desktop"), Sys.Join(home, "Área de Trabalho"), Sys.Join(home, "Documents") })
        {
            try
            {
                if (!Directory.Exists(Sys.P(dir))) continue;
                var f = Directory.GetFiles(Sys.P(dir), "OpenRGB*.AppImage").OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
                if (f != null) return new Launch(f, new[] { "--server", "--startminimized" }, "OpenRGB (AppImage em " + dir + ")");
            }
            catch { }
        }
        return null;
    }

    public static bool Start(Launch l)
    {
        var p = ProcessRunner.Start(l.Exe, l.Args);
        return p != null;
    }

    /// <summary>Sem as regras udev do OpenRGB, só o root fala com os controladores. O pacote da distribuição já traz as regras.</summary>
    public static string? SetupHint()
    {
        bool rules = new[] { "/usr/lib/udev/rules.d/60-openrgb.rules", "/lib/udev/rules.d/60-openrgb.rules", "/etc/udev/rules.d/60-openrgb.rules" }.Any(Sys.FileExists);
        bool i2c = Sys.List("/dev").Any(n => n.StartsWith("i2c-", StringComparison.Ordinal));
        var hints = new List<string>();
        if (!rules) hints.Add("não achei as regras udev do OpenRGB (60-openrgb.rules): sem elas, o programa só enxerga os dispositivos como root. Instale o pacote openrgb da distribuição, que já as inclui");
        if (!i2c) hints.Add("não há /dev/i2c-*: para RAM e placas-mãe RGB, carregue o módulo i2c-dev (sudo modprobe i2c-dev) e, em placas AMD, também o i2c-piix4");
        if (hints.Count == 0) return null;
        string text = string.Join(". ", hints) + ".";
        return char.ToUpperInvariant(text[0]) + text[1..];
    }
}
