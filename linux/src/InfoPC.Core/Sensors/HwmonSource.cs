using System.Diagnostics;

namespace InfoPC;

/// <summary>
/// Sensores do kernel em /sys/class/hwmon: temperatura (coretemp, k10temp, nvme, amdgpu, placa-mãe...), ventoinhas, tensões,
/// consumo e clocks. Unidades do hwmon: m°C, RPM, mV, mA, µW, Hz, µJ. Chips que pertencem a uma GPU conhecida
/// ganham o nome da placa e os papéis "gpuN.*".
/// </summary>
public sealed class HwmonSource : ISensorSource
{
    private const string Dir = "/sys/class/hwmon";
    private readonly IReadOnlyList<GpuDevice> _gpus;
    private readonly Dictionary<string, (long Energy, long Ticks)> _energy = new();

    public HwmonSource(IReadOnlyList<GpuDevice>? gpus = null) => _gpus = gpus ?? Array.Empty<GpuDevice>();

    public IEnumerable<SensorSample> Read()
    {
        var chips = new List<(string Dir, string Name, string Title, HwKind Kind, GpuDevice? Gpu)>();
        foreach (var h in Sys.ListNatural(Dir, "hwmon"))
        {
            string d = $"{Dir}/{h}";
            string name = Sys.Read(d + "/name") ?? h;
            var gpu = GpuOf(d);
            var (title, kind) = gpu != null ? (gpu.Title, HwKind.Gpu) : Describe(name, d);
            chips.Add((d, name, title, kind, gpu));
        }

        // Dois chips com o mesmo título (dois NVMe...) ganham um número para não se misturarem
        var dup = chips.GroupBy(c => c.Title).Where(g => g.Count() > 1).ToDictionary(g => g.Key, _ => 0);
        foreach (var c in chips)
        {
            string title = c.Title;
            if (dup.ContainsKey(title)) { dup[title]++; title += " #" + dup[title]; }
            foreach (var s in ReadChip(c.Dir, c.Name, title, c.Kind, c.Gpu)) yield return s;
        }
    }

    /// <summary>A GPU dona deste chip hwmon (pelo endereço PCI do dispositivo), se houver.</summary>
    private GpuDevice? GpuOf(string hwmonDir)
    {
        if (_gpus.Count == 0) return null;
        string? slot = (Sys.RealPath(hwmonDir + "/device") ?? "").Split('/').LastOrDefault();
        return string.IsNullOrEmpty(slot) ? null : _gpus.FirstOrDefault(g => g.PciSlot.Equals(slot, StringComparison.OrdinalIgnoreCase));
    }

    private IEnumerable<SensorSample> ReadChip(string dir, string chip, string title, HwKind kind, GpuDevice? gpu)
    {
        var files = Sys.List(dir);
        string Id(string f) => $"hwmon/{title}/{f}";
        string Label(string prefix, string fallback) => Sys.Read($"{dir}/{prefix}_label") is { Length: > 0 } l ? l : fallback;
        string g = gpu != null ? $"gpu{gpu.Index}" : "";
        HwKind boardKind = kind == HwKind.Other ? HwKind.Board : kind;

        foreach (var f in files)
        {
            // pwmN (0-255) é o único tipo sem sufixo: o duty cycle da ventoinha
            if (f.Length > 3 && f.StartsWith("pwm", StringComparison.Ordinal) && f[3..].All(char.IsDigit))
            {
                if (Sys.Double($"{dir}/{f}") is { } duty)
                    yield return new SensorSample(Id(f), title, boardKind, SensorKind.Control, "Controle " + f[3..], (float)(duty / 255.0 * 100));
                continue;
            }
            if (!f.EndsWith("_input", StringComparison.Ordinal) && !f.EndsWith("_average", StringComparison.Ordinal)) continue;
            string prefix = f[..f.LastIndexOf('_')];
            string type = new string(prefix.TakeWhile(char.IsLetter).ToArray());
            string num = prefix[type.Length..];
            // power1_average e power1_input são o mesmo sensor: fica só um
            if (f.EndsWith("_average", StringComparison.Ordinal) && files.Contains(prefix + "_input")) continue;
            double? raw = Sys.Double($"{dir}/{f}");
            if (raw == null) continue;

            switch (type)
            {
                case "temp":
                {
                    if (raw <= -100000 || raw > 300000) break; // sensor sem leitura (valores absurdos)
                    string label = Label(prefix, "Temperatura " + num);
                    var (role, prio) = TempRole(chip, label, kind, g);
                    yield return new SensorSample(Id(f), title, kind, SensorKind.Temperature, label, (float)(raw / 1000), role, prio);
                    break;
                }
                case "fan":
                    yield return new SensorSample(Id(f), title, boardKind, SensorKind.Fan, Label(prefix, "Ventoinha " + num), (float)raw.Value,
                                                  g.Length > 0 && num == "1" ? g + ".fan" : null, 2);
                    break;
                case "in":
                    yield return new SensorSample(Id(f), title, boardKind, SensorKind.Voltage, Label(prefix, "Tensão " + num), (float)(raw.Value / 1000),
                                                  g.Length > 0 && num == "0" ? g + ".volt" : null, 2);
                    break;
                case "curr":
                    yield return new SensorSample(Id(f), title, kind, SensorKind.Current, Label(prefix, "Corrente " + num), (float)(raw.Value / 1000));
                    break;
                case "power":
                {
                    string? role = kind == HwKind.Cpu ? "cpu.power" : g.Length > 0 && num == "1" ? g + ".power" : null;
                    yield return new SensorSample(Id(f), title, kind, SensorKind.Power, Label(prefix, "Consumo " + num), (float)(raw.Value / 1e6), role, 2);
                    break;
                }
                case "freq":
                {
                    string label = Label(prefix, "Clock " + num);
                    string? role = g.Length > 0 ? (label.Equals("sclk", StringComparison.OrdinalIgnoreCase) ? g + ".clock"
                                                  : label.Equals("mclk", StringComparison.OrdinalIgnoreCase) ? g + ".memclock" : null) : null;
                    yield return new SensorSample(Id(f), title, kind, SensorKind.Clock, label, (float)(raw.Value / 1e6), role, 2);
                    break;
                }
                case "energy":
                {
                    // contador acumulado (µJ): a potência é a variação sobre o tempo
                    string key = Id(f);
                    long now = Stopwatch.GetTimestamp(), e = (long)raw.Value;
                    float? watts = null;
                    if (_energy.TryGetValue(key, out var prev))
                    {
                        double secs = (now - prev.Ticks) / (double)Stopwatch.Frequency;
                        if (secs > 0.2)
                        {
                            if (e >= prev.Energy) watts = (float)((e - prev.Energy) / 1e6 / secs);
                            _energy[key] = (e, now);
                        }
                    }
                    else _energy[key] = (e, now);
                    yield return new SensorSample(Id(f), title, kind, SensorKind.Power, Label(prefix, "Energia " + num) + " (potência)", watts);
                    break;
                }
            }
        }
    }

    /// <summary>Quais leituras alimentam "cpu.temp" e "gpuN.temp", com prioridade.</summary>
    private static (string? Role, int Prio) TempRole(string chip, string label, HwKind kind, string gpu)
    {
        string l = label.ToLowerInvariant();
        if (kind == HwKind.Gpu && gpu.Length > 0)
        {
            if (l == "edge") return (gpu + ".temp", 3);
            if (l == "junction") return (gpu + ".hotspot", 3);
            if (l == "mem") return (gpu + ".memtemp", 3);
            return (gpu + ".temp", 1); // nouveau/i915: "temp1"
        }
        if (kind != HwKind.Cpu) return (null, 1);
        if (chip == "coretemp") return l.StartsWith("package", StringComparison.Ordinal) ? ("cpu.temp", 3) : (null, 1);
        if (chip is "k10temp" or "zenpower")
            return l.Contains("tdie") ? ("cpu.temp", 3) : l.Contains("tctl") ? ("cpu.temp", 2) : (null, 1);
        return ("cpu.temp", 1); // outros drivers (ARM: cpu_thermal, soc_thermal...)
    }

    /// <summary>Nome amigável e tipo de hardware a partir do nome do driver hwmon.</summary>
    public static (string Title, HwKind Kind) Describe(string name, string dir)
    {
        string Model()
        {
            foreach (var p in new[] { "/device/model", "/device/device/model", "/device/block/model" })
                if (Sys.Read(dir + p) is { Length: > 0 } m) return m.Trim();
            return "";
        }

        switch (name)
        {
            case "coretemp": return ("Processador (Intel)", HwKind.Cpu);
            case "k10temp": return ("Processador (AMD)", HwKind.Cpu);
            case "zenpower": return ("Processador (AMD, zenpower)", HwKind.Cpu);
            case "amd_energy": return ("Processador (AMD, energia)", HwKind.Cpu);
            case "cpu_thermal": case "soc_thermal": case "cpu-thermal": return ("Processador (SoC)", HwKind.Cpu);
            case "amdgpu": return ("Placa de vídeo (AMD)", HwKind.Gpu);
            case "radeon": return ("Placa de vídeo (AMD, radeon)", HwKind.Gpu);
            case "nouveau": return ("Placa de vídeo (NVIDIA, nouveau)", HwKind.Gpu);
            case "i915": case "xe": return ("Placa de vídeo (Intel)", HwKind.Gpu);
            case "nvme": { string m = Model(); return (m.Length > 0 ? $"NVMe · {m}" : "NVMe", HwKind.Storage); }
            case "drivetemp": { string m = Model(); return (m.Length > 0 ? $"Disco · {m}" : "Disco", HwKind.Storage); }
            case "acpitz": return ("ACPI (zona térmica)", HwKind.Board);
            case "pch_cannonlake": case "pch_skylake": case "pch_haswell": case "pch_lewisburg": return ("Chipset (PCH)", HwKind.Board);
            case "iwlwifi": case "iwlwifi_1": return ("Wi-Fi (Intel)", HwKind.Network);
            case "mt7921_phy0": return ("Wi-Fi (MediaTek)", HwKind.Network);
            case "r8169": return ("Ethernet (Realtek)", HwKind.Network);
            case "BAT0": case "BAT1": case "battery": return ("Bateria", HwKind.Battery);
            case "ADP1": case "AC": case "ACAD": return ("Adaptador de energia", HwKind.Battery);
        }
        if (name.StartsWith("nct") || name.StartsWith("it8") || name.StartsWith("w83") || name.StartsWith("f718") || name.StartsWith("asus")
            || name.StartsWith("gigabyte") || name.StartsWith("dell") || name.StartsWith("thinkpad") || name.StartsWith("applesmc")
            || name.StartsWith("corsair") || name.StartsWith("nzxt") || name.StartsWith("hp") || name.StartsWith("msi"))
            return ($"Placa-mãe / controlador ({name})", HwKind.Board);
        return (name, HwKind.Other);
    }
}
