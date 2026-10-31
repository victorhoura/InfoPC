using System.Globalization;

namespace InfoPC;

/// <summary>
/// Formatação pt-BR sem depender do ICU (distros mínimas não trazem libicu e o .NET não abriria).
/// O processo roda em modo invariante; aqui trocamos só os separadores de número, que é o que a tela usa.
/// ATENÇÃO: ao LER números de /proc, /sys e saída de comandos, use sempre <see cref="Num"/> (cultura invariante).
/// </summary>
public static class Locale
{
    public static readonly CultureInfo Br = Create();

    private static CultureInfo Create()
    {
        var c = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        var n = c.NumberFormat;
        n.NumberDecimalSeparator = ","; n.NumberGroupSeparator = ".";
        n.PercentDecimalSeparator = ","; n.PercentGroupSeparator = ".";
        n.CurrencyDecimalSeparator = ","; n.CurrencyGroupSeparator = ".";
        return c;
    }

    public static void Init()
    {
        CultureInfo.DefaultThreadCurrentCulture = Br;
        CultureInfo.DefaultThreadCurrentUICulture = Br;
        CultureInfo.CurrentCulture = Br;
        CultureInfo.CurrentUICulture = Br;
    }
}

/// <summary>Leitura numérica tolerante e SEMPRE invariante (o texto do kernel usa ponto decimal).</summary>
public static class Num
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static long? L(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        s = s.Trim();
        return long.TryParse(s, NumberStyles.Integer, Inv, out long v) ? v : null;
    }

    public static double? D(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        return double.TryParse(s.Trim(), NumberStyles.Float, Inv, out double v) ? v : null;
    }

    /// <summary>Primeiro número do texto ("3200 MT/s" → 3200, "1.2 V" → 1.2, "Size: 16 GB" não serve: use depois do ':').</summary>
    public static double? Leading(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        s = s.Trim();
        int i = 0;
        while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '.' || (i == 0 && (s[i] == '-' || s[i] == '+')))) i++;
        return i == 0 ? null : D(s[..i]);
    }

    public static long? Hex(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        s = s.Trim();
        if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) s = s[2..];
        return long.TryParse(s, NumberStyles.HexNumber, Inv, out long v) ? v : null;
    }
}

/// <summary>Formatação de valores para a tela e para o relatório.</summary>
public static class Fmt
{
    public static string Bytes(double b)
    {
        string[] u = { "B", "KB", "MB", "GB", "TB", "PB" };
        int i = 0;
        while (b >= 1024 && i < u.Length - 1) { b /= 1024; i++; }
        return $"{b:0.##} {u[i]}";
    }

    /// <summary>Capacidade como o fabricante anuncia (base 1000): 500 GB, 1 TB.</summary>
    public static string Capacity(double bytes)
    {
        string[] u = { "B", "KB", "MB", "GB", "TB", "PB" };
        int i = 0;
        while (bytes >= 1000 && i < u.Length - 1) { bytes /= 1000; i++; }
        return bytes >= 100 || i == 0 ? $"{bytes:0} {u[i]}" : $"{bytes:0.#} {u[i]}";
    }

    public static string Uptime(TimeSpan t) =>
        t.TotalDays >= 1 ? $"{(int)t.TotalDays} d {t.Hours} h {t.Minutes} min"
        : t.TotalHours >= 1 ? $"{t.Hours} h {t.Minutes} min"
        : $"{t.Minutes} min {t.Seconds} s";

    public static string Mhz(double mhz) => mhz >= 1000 ? $"{mhz / 1000:0.00} GHz" : $"{mhz:0} MHz";

    public static string Dash(string? s) => string.IsNullOrWhiteSpace(s) ? "—" : s;
}
