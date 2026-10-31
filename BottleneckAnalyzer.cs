using LibreHardwareMonitor.Hardware;

namespace InfoPC;

public static class BottleneckAnalyzer
{
    /// <summary>Lê os sensores atuais (LibreHardwareMonitor + NVML) da GPU principal e da CPU.</summary>
    public static LoadSample Sample(SensorMonitor m, GpuInfo? gpu, FrameStats? frames)
    {
        var rows = m.Rows;
        float? gpuLoad = null, gpuTemp = null;
        double? vramPct = null;
        bool thermal = false;
        if (gpu != null)
        {
            gpuLoad = rows.FirstOrDefault(r => r.Hardware == gpu.Name && r.Type == SensorType.Load && r.Name == "GPU Core")?.RawValue;
            var temps = rows.Where(r => r.Hardware == gpu.Name && r.Type == SensorType.Temperature).ToList();
            gpuTemp = (temps.FirstOrDefault(r => r.Name == "GPU Core") ?? temps.FirstOrDefault())?.RawValue;

            if (gpu.NvmlHandle != IntPtr.Zero && Nvml.MemoryInfo(gpu.NvmlHandle) is Nvml.Memory mem && mem.Total > 0)
                vramPct = 100.0 * mem.Used / mem.Total;
            else
            {
                var used = rows.FirstOrDefault(r => r.Hardware == gpu.Name && r.Name == "GPU Memory Used")?.RawValue;
                var total = rows.FirstOrDefault(r => r.Hardware == gpu.Name && r.Name == "GPU Memory Total")?.RawValue;
                if (used != null && total > 0) vramPct = 100.0 * used.Value / total.Value;
            }
            if (gpu.NvmlHandle != IntPtr.Zero && Nvml.ThrottleReasons(gpu.NvmlHandle) is ulong r)
                thermal = (r & 0x60) != 0; // SW/HW thermal slowdown
        }

        var cores = rows.Where(r => r.HardwareType == HardwareType.Cpu && r.Type == SensorType.Load &&
                                    r.Name.StartsWith("CPU Core #") && !r.Name.Contains("Thread") && r.RawValue != null).ToList();
        var busiest = cores.OrderByDescending(r => r.RawValue).FirstOrDefault();
        float? ram = rows.FirstOrDefault(r => r.HardwareType == HardwareType.Memory && r.Type == SensorType.Load && r.Name == "Memory")?.RawValue;

        return new LoadSample(gpuLoad, m.CpuLoad, busiest?.RawValue, busiest?.Name.Replace("CPU Core #", "núcleo "),
                              vramPct, ram, gpuTemp, m.CpuTemperature, thermal, frames?.Fps);
    }

    public static string Title(Limiter l) => l switch
    {
        Limiter.Gpu => "GPU no limite",
        Limiter.Cpu => "Gargalo de CPU",
        Limiter.Vram => "Memória de vídeo cheia",
        Limiter.Ram => "Memória RAM cheia",
        Limiter.Thermal => "Limitado por temperatura",
        _ => "Nada no limite"
    };

    public static string Advice(Limiter l) => l switch
    {
        Limiter.Gpu => "Normal em jogos pesados: a placa de vídeo está sendo bem aproveitada. Para mais FPS, reduza " +
                       "resolução ou qualidade gráfica, ou ative DLSS/FSR.",
        Limiter.Cpu => "A CPU (ou um núcleo dela) não acompanha a GPU. Subir resolução/gráficos quase não custa FPS. " +
                       "Para ganhar FPS: reduza opções que pesam na CPU (distância de visão, multidões, física), feche " +
                       "programas em segundo plano e confira se o XMP/EXPO da RAM está ativo na BIOS.",
        Limiter.Vram => "A memória da placa de vídeo encheu, o que causa travadas. Reduza a qualidade das texturas " +
                        "ou a resolução.",
        Limiter.Ram => "Pouca RAM livre: o Windows passa a usar o disco e o jogo engasga. Feche navegadores e outros programas.",
        Limiter.Thermal => "Temperatura alta está derrubando o clock. Verifique ventilação do gabinete, poeira e pasta térmica.",
        _ => "Nem CPU nem GPU estão no limite: provavelmente há trava de FPS, V-Sync/G-Sync limitando, ou o jogo " +
             "está em menu/pausa. Normal se o FPS já é o desejado."
    };

    /// <summary>Equilíbrio teórico pelo desempenho relativo (GPU: RTX 4070 = 100; CPU: Ryzen 7 7700X = 100).</summary>
    public static (double Ratio, string Text) Balance(double gpuIdx, double cpuIdx) => BottleneckBalance.Compute(gpuIdx, cpuIdx);
}
