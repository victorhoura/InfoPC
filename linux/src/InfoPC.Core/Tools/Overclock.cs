using System.Text.RegularExpressions;

namespace InfoPC;

/// <summary>Ajustes da GPU NVIDIA pelo nvidia-smi (a NVML só grava como root). Valem até reiniciar o PC; nada é gravado em disco.</summary>
public static class GpuTuning
{
    public static bool Available => Shell.Has("nvidia-smi");

    private static bool ValidBus(string? id) => id != null && Regex.IsMatch(id, @"^[0-9a-fA-F]{4,8}:[0-9a-fA-F]{2}:[0-9a-fA-F]{2}\.[0-9a-fA-F]$");

    private static RootResult Run(GpuDevice g, params string[] args)
    {
        if (!ValidBus(g.NvmlBusId)) return new RootResult(false, "", "Não identifiquei o endereço PCI desta placa na NVML.");
        var all = new List<string> { "-i", g.NvmlBusId! };
        all.AddRange(args);
        return Elevate.Run("nvidia-smi", all, 60000);
    }

    public static RootResult SetPowerLimit(GpuDevice g, int watts) =>
        watts is < 50 or > 1000 ? new RootResult(false, "", "Limite fora da faixa aceita.") : Run(g, "-pl", watts.ToString());

    public static RootResult LockClocks(GpuDevice g, int maxMhz) =>
        maxMhz is < 300 or > 4000 ? new RootResult(false, "", "Clock fora da faixa aceita.") : Run(g, "-lgc", $"{Math.Min(210, maxMhz)},{maxMhz}");

    public static RootResult ResetClocks(GpuDevice g) => Run(g, "-rgc");
}

/// <summary>Passo do guia de overclock na BIOS (o texto é o mesmo da edição Windows: a BIOS não depende do sistema).</summary>
public sealed record GuideStep(string Number, string Title, string Where, string Text);

public static class OcGuide
{
    public static (string Title, string Intro, List<GuideStep> Steps) Build(string boardVendor, string boardModel, string cpuName)
    {
        bool gigabyte = boardVendor.Contains("Gigabyte", StringComparison.OrdinalIgnoreCase);
        bool asus = boardVendor.Contains("ASUS", StringComparison.OrdinalIgnoreCase);
        bool msi = boardVendor.Contains("Micro-Star", StringComparison.OrdinalIgnoreCase) || boardVendor.Contains("MSI", StringComparison.OrdinalIgnoreCase);
        string cpu = GameCatalog.ShortCpuName(cpuName);
        bool amd = cpuName.Contains("Ryzen", StringComparison.OrdinalIgnoreCase);
        string board = ((gigabyte ? "Gigabyte" : (boardVendor.Split(' ').FirstOrDefault() ?? "")) + " " + boardModel).Trim();

        string pbo = gigabyte ? "Tweaker → Advanced CPU Settings → Precision Boost Overdrive  (ou Settings → AMD Overclocking)"
                   : asus ? "Advanced → AMD Overclocking → Precision Boost Overdrive  (ou AI Tweaker → PBO)"
                   : msi ? "OC → Advanced CPU Configuration → AMD Overclocking → Precision Boost Overdrive"
                   : "Advanced → AMD Overclocking → Precision Boost Overdrive";
        string ram = gigabyte ? "Tweaker → Extreme Memory Profile (X.M.P.) → EXPO1"
                   : asus ? "AI Tweaker → Ai Overclock Tuner → EXPO I"
                   : msi ? "OC → A-XMP/EXPO → Profile 1" : "procure EXPO / XMP no menu de memória";

        var steps = new List<GuideStep>
        {
            new("0", "Antes de começar", gigabyte ? "Save & Exit → Save Profiles" : "Save & Exit → Save Profile",
                "Salve o perfil atual da BIOS. Se o PC não ligar depois de um ajuste, espere 3 tentativas (muitas placas voltam sozinhas) ou limpe a CMOS (jumper CLR_CMOS na placa ou tirando a bateria por 1 minuto)."),
            new("1", "Ative o perfil da memória (EXPO/XMP)", ram,
                "É o maior ganho em jogos para a maioria dos PCs: a RAM sai do padrão lento e vai para a velocidade da etiqueta. Teste: verificador \"CPU + RAM\" por 15 min (o teste de RAM acusa erro se o perfil não for estável)."),
        };
        if (amd)
        {
            steps.Add(new("2", "Precision Boost Overdrive (PBO)", pbo, "Precision Boost Overdrive → Advanced. PBO Limits → Motherboard. Isso libera os limites de energia para o boost durar mais. Teste com o verificador."));
            steps.Add(new("3", "Curve Optimizer (undervolt da CPU)", pbo + " → Curve Optimizer",
                $"All Cores → Negative → 10. Teste. Se passar, tente 15, 20, 25… até falhar; volte 5 a partir de onde falhou. No {cpu} costuma ficar entre -15 e -30. Instabilidade de Curve Optimizer aparece muito em uso leve: use o PC normalmente por um dia e veja \"Erros de hardware recentes\" aqui; erro de CPU (MCE) = volte um passo."));
            steps.Add(new("4", "Max CPU Boost Clock Override (opcional)", pbo, "Depois do Curve Optimizer estável: +100 MHz, teste; depois +200 MHz. Ganho pequeno; pode exigir reduzir o Curve Optimizer."));
            steps.Add(new("5", "Alternativa fria: Eco Mode", pbo + " → Eco Mode", "Em vez de mais desempenho: limita a CPU a 65 W (ou 105 W). Perde ~5–10% em tarefas pesadas multi-core, quase nada em jogos, e roda bem mais frio e silencioso."));
        }
        else
            steps.Add(new("2", "Processador Intel", "Tweaker / Ai Tweaker / OC → CPU", "Para Intel, prefira undervolt (offset negativo de tensão) e limites de energia (PL1/PL2) em vez de aumentar multiplicadores. Teste cada passo com o verificador."));

        return ("GUIA: OVERCLOCK NA BIOS · " + board.ToUpperInvariant() + " · " + cpu.ToUpperInvariant(),
                "Faça uma mudança por vez e rode o verificador depois de cada uma. Os menus podem mudar um pouco com a versão da BIOS. Valores são pontos de partida seguros, não garantia: cada chip é diferente.",
                steps);
    }
}

/// <summary>Resultado do verificador. <see cref="Inconclusive"/>: nada falhou, mas faltou carga ou leitura para dizer ESTÁVEL.</summary>
public sealed record StabilityResult(bool Stable, string Reason, float MaxCpuTemp, float MaxGpuTemp, bool Inconclusive = false)
{
    public string Verdict => Stable ? "ESTÁVEL" : Inconclusive ? "INCONCLUSIVO" : "INSTÁVEL";
}

/// <summary>
/// Regras do verificador de estabilidade: para o teste se a temperatura passar do limite de segurança ou se a RAM errar, e no fim compara
/// os erros de hardware do log do kernel com os de antes do teste. Os limites protegem o hardware: não afrouxe.
/// </summary>
public sealed class StabilityCheck
{
    public const float CpuLimit = 95f, GpuLimit = 88f;

    private readonly bool _cpu, _gpu;
    private readonly KernelErrors _baseline;
    public float MaxCpuTemp { get; private set; }
    public float MaxGpuTemp { get; private set; }

    public StabilityCheck(bool cpu, bool gpu, KernelErrors baseline) { _cpu = cpu; _gpu = gpu; _baseline = baseline; }

    /// <summary>Uma leitura por segundo. Devolve o motivo para parar o teste, ou null para continuar.</summary>
    public string? Sample(float? cpuTemp, float? gpuTemp, long memoryErrors)
    {
        if (cpuTemp is float c) MaxCpuTemp = Math.Max(MaxCpuTemp, c);
        if (gpuTemp is float g) MaxGpuTemp = Math.Max(MaxGpuTemp, g);
        if (_cpu && cpuTemp >= CpuLimit) return $"a CPU chegou a {cpuTemp:0} °C (limite de segurança {CpuLimit:0} °C)";
        if (_gpu && gpuTemp >= GpuLimit) return $"a GPU chegou a {gpuTemp:0} °C (limite de segurança {GpuLimit:0} °C)";
        if (memoryErrors > 0) return $"{memoryErrors} erros de memória (RAM/EXPO instável)";
        return null;
    }

    /// <param name="noLoad">A carga parou antes do fim (o programa de GPU fechou sozinho, por exemplo): sem falha, o resultado é INCONCLUSIVO.</param>
    public StabilityResult Finish(KernelErrors after, string? failure, TimeSpan duration, string? noLoad = null)
    {
        // sem ler o log antes e depois não dá para comparar: erro antigo pareceria novo, e "nenhum erro" não teria sido conferido
        bool logRead = _baseline.Error == null && after.Error == null;
        int newErrors = logRead ? Math.Max(0, after.Total - _baseline.Total) : 0;
        if (failure == null && newErrors > 0)
            failure = $"o kernel registrou {newErrors} novo(s) erro(s) de hardware durante o teste ({after.Summary})";
        if (failure != null)
            return new StabilityResult(false, "Motivo: " + failure + ". Volte um passo no ajuste e teste de novo.", MaxCpuTemp, MaxGpuTemp);
        if (noLoad != null)
            return new StabilityResult(false, $"Motivo: {noLoad}, então o teste não teve a carga pedida até o fim. Se a GPU estava com ajuste, isso também pode ser instabilidade: confira os erros recentes e teste de novo.", MaxCpuTemp, MaxGpuTemp, Inconclusive: true);
        if (!logRead)
            return new StabilityResult(false, $"O stress rodou {duration.TotalMinutes:0} min sem falhar, mas não consegui ler o log do kernel ({_baseline.Error ?? after.Error}), então os erros de hardware não foram conferidos. Rode o InfoPC com permissão para ler o journalctl ou o dmesg e teste de novo.", MaxCpuTemp, MaxGpuTemp, Inconclusive: true);
        return new StabilityResult(true, $"Nenhum erro em {duration.TotalMinutes:0} min de stress. Para Curve Optimizer, use o PC normalmente por um dia e confira os erros recentes.", MaxCpuTemp, MaxGpuTemp);
    }
}
