using System.Text.RegularExpressions;

namespace InfoPC;

/// <summary>
/// Leituras ao vivo de uma GPU: NVML (NVIDIA proprietária), sysfs do amdgpu e do Intel. Temperatura, ventoinha e consumo da AMD
/// e da Intel vêm do hwmon (<see cref="HwmonSource"/>); aqui entram carga, VRAM e clocks.
/// </summary>
public sealed class GpuSource : ISensorSource
{
    private readonly GpuDevice _g;
    public GpuSource(GpuDevice g) => _g = g;

    public IEnumerable<SensorSample> Read()
    {
        // notebook híbrido com a GPU dormindo: ler acordaria a placa e gastaria bateria
        if (_g.Suspended) return Array.Empty<SensorSample>();
        var list = new List<SensorSample>();
        string hw = _g.Title, r = $"gpu{_g.Index}";
        void Add(string id, SensorKind k, string name, double? v, string? role = null, int prio = 2) =>
            list.Add(new SensorSample($"gpu{_g.Index}/{id}", hw, HwKind.Gpu, k, name, v == null ? null : (float)v.Value, role, prio));

        if (_g.HasNvml)
        {
            var h = _g.Nvml;
            if (NvmlLib.Util(h) is { } u)
            {
                Add("load", SensorKind.Load, "GPU Core", u.Gpu, r + ".load", 3);
                Add("memctl", SensorKind.Load, "Controlador de memória", u.Memory);
            }
            if (NvmlLib.Temperature(h) is { } t) Add("temp", SensorKind.Temperature, "GPU Core", t, r + ".temp", 3);
            if (NvmlLib.FanPercent(h) is { } fan) Add("fan", SensorKind.Control, "Ventoinha", fan);
            if (NvmlLib.PowerUsageMw(h) is { } pw) Add("power", SensorKind.Power, "GPU Power", pw / 1000.0, r + ".power", 3);
            if (NvmlLib.Clock(h, NvmlLib.ClockGraphics) is { } gc) Add("clock", SensorKind.Clock, "GPU Core", gc, r + ".clock", 3);
            if (NvmlLib.Clock(h, NvmlLib.ClockMem) is { } mc) Add("memclock", SensorKind.Clock, "GPU Memory", mc, r + ".memclock", 3);
            if (NvmlLib.MemoryInfo(h) is { } m && m.Total > 0)
            {
                Add("vram.used", SensorKind.Data, "Memória de vídeo usada", m.Used / 1073741824.0, r + ".vram.used", 3);
                Add("vram.total", SensorKind.Data, "Memória de vídeo total", m.Total / 1073741824.0);
                Add("vram.pct", SensorKind.Load, "Memória de vídeo", 100.0 * m.Used / m.Total, r + ".vram.pct", 3);
            }
            if (NvmlLib.EncoderUtil(h) is { } enc) Add("enc", SensorKind.Load, "Encoder (NVENC)", enc);
            if (NvmlLib.DecoderUtil(h) is { } dec) Add("dec", SensorKind.Load, "Decoder (NVDEC)", dec);
            if (NvmlLib.ThrottleReasons(h) is { } reasons) Add("thermalcap", SensorKind.Level, "Limitador térmico ativo", NvmlLib.IsThermalThrottle(reasons) ? 100 : 0, r + ".thermalcap", 3);
        }
        else if (_g.Vendor == GpuVendor.Amd)
        {
            string d = _g.SysDir;
            if (Sys.Double(d + "/gpu_busy_percent") is { } busy) Add("load", SensorKind.Load, "GPU Core", busy, r + ".load", 3);
            if (Sys.Double(d + "/mem_busy_percent") is { } mb) Add("memctl", SensorKind.Load, "Controlador de memória", mb);
            long? total = Sys.Long(d + "/mem_info_vram_total"), used = Sys.Long(d + "/mem_info_vram_used");
            if (total is > 0 && used != null)
            {
                Add("vram.used", SensorKind.Data, "Memória de vídeo usada", used.Value / 1073741824.0, r + ".vram.used", 3);
                Add("vram.total", SensorKind.Data, "Memória de vídeo total", total.Value / 1073741824.0);
                Add("vram.pct", SensorKind.Load, "Memória de vídeo", 100.0 * used.Value / total.Value, r + ".vram.pct", 3);
            }
            // sem hwmon de frequência (kernels antigos): usa os estados de clock da DPM
            bool hwmonFreq = Sys.List(d + "/hwmon").Any(h => Sys.FileExists($"{d}/hwmon/{h}/freq1_input"));
            if (!hwmonFreq && DpmCurrent(d + "/pp_dpm_sclk") is { } sc) Add("clock", SensorKind.Clock, "sclk", sc, r + ".clock", 1);
        }
        else if (_g.Vendor == GpuVendor.Intel)
        {
            double? cur = Sys.Double($"/sys/class/drm/{_g.Card}/gt_cur_freq_mhz") ?? Sys.Double($"/sys/class/drm/{_g.Card}/gt/gt0/rps_cur_freq_mhz")
                          ?? Sys.Double(_g.SysDir + "/tile0/gt0/freq0/cur_freq");
            if (cur != null) Add("clock", SensorKind.Clock, "GPU Core", cur, r + ".clock", 3);
            double? act = Sys.Double($"/sys/class/drm/{_g.Card}/gt_act_freq_mhz") ?? Sys.Double($"/sys/class/drm/{_g.Card}/gt/gt0/rps_act_freq_mhz");
            if (act != null) Add("actclock", SensorKind.Clock, "GPU Core (efetivo)", act);
        }
        return list;
    }

    /// <summary>Clock atual de um arquivo pp_dpm_* (a linha marcada com *): "1: 1500Mhz *" → 1500.</summary>
    public static double? DpmCurrent(string path)
    {
        foreach (var line in Sys.Lines(path))
        {
            if (!line.Contains('*')) continue;
            var m = Regex.Match(line, @"(\d+)\s*[Mm][Hh][Zz]");
            if (m.Success) return Num.D(m.Groups[1].Value);
        }
        return null;
    }

    /// <summary>Maior clock listado em um pp_dpm_*.</summary>
    public static double? DpmMax(string path)
    {
        double? max = null;
        foreach (var line in Sys.Lines(path))
        {
            var m = Regex.Match(line, @"(\d+)\s*[Mm][Hh][Zz]");
            if (m.Success && Num.D(m.Groups[1].Value) is { } v) max = Math.Max(max ?? 0, v);
        }
        return max;
    }
}
