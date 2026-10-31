namespace InfoPC;

/// <summary>Amostra e texto do detector de gargalo no Linux. A classificação (Limiter, LoadSample) é a mesma da edição Windows (BottleneckModel.cs).</summary>
public static class LinuxBottleneck
{
    public static LoadSample Sample(SensorHub hub, GpuDevice? gpu)
    {
        string g = gpu != null ? $"gpu{gpu.Index}." : "none.";
        // thread mais ocupada: o papel só guarda o valor; o nome vem da linha do sensor
        var busiest = hub.Rows.Where(r => r.Kind == SensorKind.Load && r.Name.StartsWith("CPU Thread #", StringComparison.Ordinal) && r.RawValue != null)
                              .OrderByDescending(r => r.RawValue).FirstOrDefault();
        return new LoadSample(hub.Role(g + "load"), hub.CpuLoad, hub.Role("cpu.maxcore"), busiest?.Name.Replace("CPU Thread #", "thread "),
                              hub.Role(g + "vram.pct"), hub.RamPercent, hub.Role(g + "temp"), hub.CpuTemperature,
                              hub.Role(g + "thermalcap") is > 0, null);
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
        Limiter.Gpu => "Normal em jogos pesados: a placa de vídeo está sendo bem aproveitada. Para mais FPS, reduza resolução ou qualidade gráfica, ou ative DLSS/FSR.",
        Limiter.Cpu => "A CPU (ou uma thread dela) não acompanha a GPU. Subir resolução/gráficos quase não custa FPS. Para ganhar FPS: reduza opções que pesam na CPU " +
                       "(distância de visão, multidões, física), feche programas em segundo plano, use o GameMode (perfil do jogo na Biblioteca) e confira se o XMP/EXPO da RAM está ativo na BIOS.",
        Limiter.Vram => "A memória da placa de vídeo encheu, o que causa travadas. Reduza a qualidade das texturas ou a resolução.",
        Limiter.Ram => "Pouca RAM livre: o sistema passa a usar swap e o jogo engasga. Feche navegadores e outros programas (página Otimização).",
        Limiter.Thermal => "Temperatura alta está derrubando o clock. Verifique ventilação do gabinete, poeira e pasta térmica.",
        _ => "Nem CPU nem GPU estão no limite: provavelmente há trava de FPS, V-Sync limitando, ou o jogo está em menu/pausa. Normal se o FPS já é o desejado."
    };
}
