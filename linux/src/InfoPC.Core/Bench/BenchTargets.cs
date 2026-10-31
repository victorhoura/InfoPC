using System.Text.RegularExpressions;

namespace InfoPC;

/// <summary>Uma unidade onde o benchmark de disco pode gravar o arquivo de teste (precisa de pasta gravável e espaço livre).</summary>
public sealed record BenchTarget(string Label, string Dir, string MountPoint, string FsType, long FreeBytes);

public static class BenchTargets
{
    /// <summary>Unidades montadas (sem rede) com pelo menos 1,5 GB livres e uma pasta onde este usuário consegue gravar.</summary>
    public static List<BenchTarget> List()
    {
        var all = new List<(string Mount, string Fs)>();
        foreach (var line in Sys.Lines("/proc/self/mountinfo"))
        {
            int dash = line.IndexOf(" - ", StringComparison.Ordinal);
            if (dash < 0) continue;
            var pre = line[..dash].Split(' '); var post = line[(dash + 3)..].Split(' ');
            if (pre.Length >= 5 && post.Length >= 1) all.Add((Unescape(pre[4]), post[0]));
        }
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string? homeOwner = Owner(home, all);

        var result = new List<BenchTarget>();
        foreach (var m in DiskCollector.Mounts())
        {
            if (m.IsNetwork || m.FreeBytes < 1536L << 20) continue;
            string? dir = null;
            foreach (var cand in Candidates(m.MountPoint, m.MountPoint == homeOwner))
                if (Writable(cand)) { dir = cand; break; }
            if (dir == null) continue;
            string disk = m.Disk != null ? $" ({m.Disk})" : "";
            result.Add(new BenchTarget($"{m.MountPoint}{disk} · {m.FsType} · {Fmt.Bytes(m.FreeBytes)} livres", dir, m.MountPoint, m.FsType, m.FreeBytes));
        }
        return result;
    }

    private static IEnumerable<string> Candidates(string mount, bool homeHere)
    {
        yield return Path.Combine(mount, "InfoPCBench");
        if (homeHere)
        {
            string cache = Environment.GetEnvironmentVariable("XDG_CACHE_HOME") is { Length: > 0 } x
                ? x : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache");
            yield return Path.Combine(cache, "InfoPCBench");
        }
        yield return Path.Combine(mount, "tmp", "InfoPCBench");
    }

    /// <summary>Ponto de montagem mais específico que contém o caminho.</summary>
    public static string? Owner(string path, IEnumerable<(string Mount, string Fs)> mounts)
    {
        string? best = null;
        foreach (var (m, _) in mounts)
        {
            bool inside = m == "/" || path == m || path.StartsWith(m.TrimEnd('/') + "/", StringComparison.Ordinal);
            if (inside && (best == null || m.Length > best.Length)) best = m;
        }
        return best;
    }

    private static bool Writable(string dir)
    {
        try
        {
            Directory.CreateDirectory(dir);
            string probe = Path.Combine(dir, ".teste-" + Environment.ProcessId);
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return true;
        }
        catch { return false; }
    }

    private static string Unescape(string s) =>
        s.Contains('\\') ? Regex.Replace(s, @"\\([0-7]{3})", m => ((char)Convert.ToInt32(m.Groups[1].Value, 8)).ToString()) : s;

    /// <summary>Pasta para o "carregamento de mapa": a do disco do sistema (nunca tmpfs, que seria RAM).</summary>
    public static BenchTarget? Default() => List().OrderByDescending(t => t.MountPoint == "/").FirstOrDefault();
}
