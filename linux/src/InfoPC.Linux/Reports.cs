using Avalonia.Platform;

namespace InfoPC.Linux;

/// <summary>Gera o relatório HTML/JSON com o que o programa já leu e grava na pasta Documentos (sem janela de diálogo, que depende do portal do desktop).</summary>
public static class Reports
{
    public static string DocumentsDir()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        try
        {
            string cfg = Path.Combine(Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is { Length: > 0 } x ? x : Path.Combine(home, ".config"), "user-dirs.dirs");
            foreach (var line in File.ReadLines(cfg))
                if (line.StartsWith("XDG_DOCUMENTS_DIR=", StringComparison.Ordinal))
                {
                    string v = line["XDG_DOCUMENTS_DIR=".Length..].Trim().Trim('"').Replace("$HOME", home);
                    if (Directory.Exists(v)) return v;
                }
        }
        catch { }
        string docs = Path.Combine(home, "Documents");
        return Directory.Exists(docs) ? docs : home;
    }

    public static string? LogoBase64()
    {
        try
        {
            using var s = AssetLoader.Open(new Uri("avares://InfoPC/Assets/logo-dark.png"));
            using var ms = new MemoryStream();
            s.CopyTo(ms);
            return Convert.ToBase64String(ms.ToArray());
        }
        catch { return null; }
    }

    /// <summary>Monta e grava o relatório; devolve o caminho do arquivo.</summary>
    public static string Save(bool json, IReadOnlyList<DiskHealthCard>? network)
    {
        var s = Services.Current;
        var data = ReportFactory.Build(
            SystemInfoCollector.Collect(s.Ram, s.Gpus, s.Disks), s.Gpus, DiskHealthEvaluator.Cards(s.Disks, s.Smart),
            EnergyCards.Build(s.Hub), network, s.Hub, s.Settings, LogoBase64());
        string dir = DocumentsDir();
        string file = Path.Combine(dir, $"InfoPC-relatorio-{data.Machine}-{DateTime.Now:yyyyMMdd-HHmm}.{(json ? "json" : "html")}");
        File.WriteAllText(file, json ? ReportBuilder.Json(data) : ReportBuilder.Html(data), new System.Text.UTF8Encoding(false));
        return file;
    }
}
