namespace InfoPC;

public sealed record BatteryDetails(
    string Name, string Status, int? Percent, double? EnergyNowWh, double? EnergyFullWh, double? EnergyDesignWh, double? HealthPct,
    int? Cycles, double? VoltageV, double? PowerW, string Technology, string Manufacturer, string Model, TimeSpan? TimeRemaining)
{
    public bool Charging => Status.Equals("Charging", StringComparison.OrdinalIgnoreCase);
    public bool Discharging => Status.Equals("Discharging", StringComparison.OrdinalIgnoreCase);
}

/// <summary>Baterias e fonte de energia (/sys/class/power_supply).</summary>
public static class BatteryReader
{
    private const string Dir = "/sys/class/power_supply";

    public static List<BatteryDetails> Read()
    {
        var list = new List<BatteryDetails>();
        foreach (var n in Sys.List(Dir))
        {
            string d = $"{Dir}/{n}";
            if (!string.Equals(Sys.Read(d + "/type"), "Battery", StringComparison.OrdinalIgnoreCase)) continue;
            // baterias de periféricos (mouse sem fio, fone) aparecem como "Battery" com scope=Device: não são da máquina
            if (string.Equals(Sys.Read(d + "/scope"), "Device", StringComparison.OrdinalIgnoreCase)) continue;
            if (Sys.Read(d + "/present") == "0") continue;

            double? volt = Sys.Double(d + "/voltage_now") is { } v ? v / 1e6 : null;
            double? vDesign = Sys.Double(d + "/voltage_min_design") is { } vm ? vm / 1e6 : volt;
            // a bateria informa energia (µWh) ou carga (µAh); com carga, a energia é carga × tensão
            double? Wh(string energyKey, string chargeKey)
            {
                if (Sys.Double($"{d}/{energyKey}") is { } e) return e / 1e6;
                if (Sys.Double($"{d}/{chargeKey}") is { } c && vDesign != null) return c / 1e6 * vDesign;
                return null;
            }
            double? now = Wh("energy_now", "charge_now"), full = Wh("energy_full", "charge_full"), design = Wh("energy_full_design", "charge_full_design");
            double? power = Sys.Double(d + "/power_now") is { } p ? p / 1e6 : null;
            if (power == null && Sys.Double(d + "/current_now") is { } cur && volt != null) power = Math.Abs(cur) / 1e6 * volt;
            int? pct = (int?)Sys.Long(d + "/capacity");
            if (pct == null && now != null && full is > 0) pct = (int)Math.Round(100 * now.Value / full.Value);
            double? health = full != null && design is > 0 ? Math.Min(100.0, 100.0 * full.Value / design.Value) : null;
            string status = Sys.Read(d + "/status") ?? "Unknown";

            TimeSpan? left = null;
            if (power is > 0.5 && now != null && full != null)
            {
                if (status.Equals("Discharging", StringComparison.OrdinalIgnoreCase)) left = TimeSpan.FromHours(now.Value / power.Value);
                else if (status.Equals("Charging", StringComparison.OrdinalIgnoreCase)) left = TimeSpan.FromHours(Math.Max(0, full.Value - now.Value) / power.Value);
            }
            list.Add(new BatteryDetails(n, status, pct, now, full, design, health, (int?)Sys.Long(d + "/cycle_count") is > 0 and var cyc ? cyc : null, volt, power,
                                        Sys.Read(d + "/technology") ?? "", Sys.Read(d + "/manufacturer") ?? "", Sys.Read(d + "/model_name") ?? "", left));
        }
        return list;
    }

    /// <summary>true = na tomada, false = na bateria, null = não há informação (PC de mesa).</summary>
    public static bool? AcOnline()
    {
        bool any = false;
        foreach (var n in Sys.List(Dir))
        {
            string t = Sys.Read($"{Dir}/{n}/type") ?? "";
            if (t is "Mains" or "USB" or "USB_C" or "USB_PD")
            {
                any = true;
                if (Sys.Read($"{Dir}/{n}/online") == "1") return true;
            }
        }
        return any ? false : null;
    }

    /// <summary>Perfil de energia ativo: power-profiles-daemon, perfil da plataforma ACPI ou o governador da CPU.</summary>
    public static string PowerProfile()
    {
        var r = Shell.Run("powerprofilesctl", new[] { "get" }, 3000);
        if (r.Ok && r.Out.Trim().Length > 0) return ProfileName(r.Out.Trim());
        if (Sys.Read("/sys/firmware/acpi/platform_profile") is { Length: > 0 } pp) return ProfileName(pp);
        if (Sys.Read("/sys/devices/system/cpu/cpu0/cpufreq/scaling_governor") is { Length: > 0 } g) return $"governador {g}";
        return "—";
    }

    private static string ProfileName(string p) => p switch
    {
        "performance" => "Desempenho", "balanced" or "balanced-performance" => "Equilibrado", "power-saver" or "low-power" or "quiet" => "Economia de energia", _ => p
    };
}

/// <summary>Carga e consumo das baterias como sensores.</summary>
public sealed class BatterySource : ISensorSource
{
    public IEnumerable<SensorSample> Read()
    {
        foreach (var b in BatteryReader.Read())
        {
            string hw = "Bateria " + b.Name;
            yield return new SensorSample($"bat/{b.Name}/pct", hw, HwKind.Battery, SensorKind.Level, "Carga", b.Percent);
            if (b.PowerW != null)
                yield return new SensorSample($"bat/{b.Name}/power", hw, HwKind.Battery, SensorKind.Power, b.Discharging ? "Consumo" : "Carregando", (float)b.PowerW.Value);
            if (b.VoltageV != null)
                yield return new SensorSample($"bat/{b.Name}/volt", hw, HwKind.Battery, SensorKind.Voltage, "Tensão", (float)b.VoltageV.Value);
        }
    }
}
