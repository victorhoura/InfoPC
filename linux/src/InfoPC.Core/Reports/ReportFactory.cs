using System.Reflection;

namespace InfoPC;

public static class AppInfo
{
    /// <summary>Versão do produto ("1.1.0"), sem o sufixo de commit que o SDK acrescenta.</summary>
    public static string Version { get; } = ReadVersion();

    private static string ReadVersion()
    {
        var a = typeof(AppInfo).Assembly;
        string? v = a.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (string.IsNullOrWhiteSpace(v)) v = a.GetName().Version?.ToString(3);
        int plus = v?.IndexOf('+') ?? -1;
        return (plus >= 0 ? v![..plus] : v) ?? "1.1.1";
    }

    public const string FooterNote = "Dados lidos de /proc, /sys, do driver da placa de vídeo e dos sensores do kernel; podem variar conforme o hardware.";
}

/// <summary>Reúne o que cada coletor leu no formato do relatório HTML / JSON (o mesmo da edição Windows).</summary>
public static class ReportFactory
{
    public static ReportData Build(
        IReadOnlyList<InfoNode> system, IReadOnlyList<GpuDevice> gpus, IReadOnlyList<DiskHealthCard> disks, IReadOnlyList<DiskHealthCard> power,
        IReadOnlyList<DiskHealthCard>? network, SensorHub? hub, AppSettings? settings, string? logoBase64 = null)
    {
        var data = new ReportData { AppVersion = AppInfo.Version, FooterNote = AppInfo.FooterNote, LogoBase64 = logoBase64 };

        foreach (var n in system)
            if (ReportBuilder.ToNode(n) is { } rn) data.System.Add(rn);

        foreach (var g in gpus)
        {
            var info = GpuInfoBuilder.Build(g);
            data.Gpus.Add(new ReportGpu(g.Name, info.Subtitle, info.Sections.Select(ReportBuilder.ToNode).Where(x => x != null).Cast<ReportNode>().ToList()));
        }

        data.Disks.AddRange(disks.Select(ToDisk));
        data.Power.AddRange(power.Select(ToDisk));
        if (network != null) data.Network.AddRange(network.Select(ToDisk));

        if (hub != null)
            foreach (var r in hub.Rows.Where(r => r.RawValue != null).OrderBy(r => r.HwKind).ThenBy(r => r.Hardware, StringComparer.Ordinal).ThenBy(r => r.Order))
                data.Sensors.Add(new ReportSensor(r.Hardware, r.Category, r.Name, r.Value, r.Min, r.Max));

        if (settings != null)
        {
            foreach (var b in settings.BenchRecords.OrderByDescending(b => b.Date).Take(15)) data.Benchmarks.Add(new ReportBench(b.Date, b.Test, b.Summary));
            foreach (var b in settings.BenchRuns.OrderByDescending(b => b.Date).Take(10))
                data.Benchmarks.Add(new ReportBench(b.Date, "Benchmark de CPU", $"{(b.Multi != null ? $"multi {b.Multi:0}" : "")}{(b.Single != null ? $" · single {b.Single:0}" : "")} · {b.Cpu}".Trim(' ', '·')));
        }
        return data;
    }

    private static ReportDisk ToDisk(DiskHealthCard c) =>
        new(c.Title, c.Meta, c.VerdictText, c.Reasons, c.Metrics.Where(m => !m.Key.Contains("série", StringComparison.OrdinalIgnoreCase)).ToList());
}
