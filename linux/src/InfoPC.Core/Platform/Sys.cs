using System.Text;

namespace InfoPC;

/// <summary>
/// Acesso a /proc, /sys e /etc. <see cref="Root"/> permite apontar para uma pasta com a mesma estrutura
/// (os testes usam isso, já que o desenvolvimento acontece fora do Linux). Toda leitura que falha devolve null.
/// </summary>
public static class Sys
{
    public static string Root { get; set; } = "/";

    /// <summary>Pasta pessoal do usuário (os testes apontam para uma pasta de fixtures).</summary>
    public static string Home { get; set; } = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    /// <summary>Junta partes de caminho com "/" (o código da edição Linux nunca usa "\").</summary>
    public static string Join(params string[] parts) =>
        string.Join("/", parts.Select((p, i) => i == 0 ? p.TrimEnd('/') : p.Trim('/')));

    public static string P(string path) =>
        Root == "/" ? path : System.IO.Path.Combine(Root, path.TrimStart('/'));

    public static string? Read(string path)
    {
        try { return File.ReadAllText(P(path), Encoding.UTF8).Trim(); }
        catch { return null; }
    }

    public static string[] Lines(string path)
    {
        try { return File.ReadAllLines(P(path), Encoding.UTF8); }
        catch { return Array.Empty<string>(); }
    }

    public static long? Long(string path) => Num.L(Read(path));
    public static double? Double(string path) => Num.D(Read(path));

    public static bool FileExists(string path) { try { return File.Exists(P(path)); } catch { return false; } }
    public static bool DirExists(string path) { try { return Directory.Exists(P(path)); } catch { return false; } }
    public static bool Exists(string path) => FileExists(path) || DirExists(path);

    /// <summary>Nomes (não caminhos) dos itens de uma pasta, em ordem alfabética; vazio se não existir.</summary>
    public static List<string> List(string dir)
    {
        try
        {
            var names = Directory.EnumerateFileSystemEntries(P(dir)).Select(System.IO.Path.GetFileName).Where(n => n != null).Cast<string>().ToList();
            names.Sort(StringComparer.Ordinal);
            return names;
        }
        catch { return new List<string>(); }
    }

    /// <summary>Itens numerados em ordem natural (cpu2 antes de cpu10).</summary>
    public static List<string> ListNatural(string dir, string prefix)
    {
        var l = List(dir).Where(n => n.StartsWith(prefix, StringComparison.Ordinal) && n.Length > prefix.Length && n[prefix.Length..].All(char.IsDigit)).ToList();
        l.Sort((a, b) => (Num.L(a[prefix.Length..]) ?? 0).CompareTo(Num.L(b[prefix.Length..]) ?? 0));
        return l;
    }

    /// <summary>Caminho real de um link simbólico (já com os "..", sem o prefixo de teste). Null se não for link ou não existir.</summary>
    public static string? RealPath(string path)
    {
        try
        {
            var info = new DirectoryInfo(P(path));
            var target = info.ResolveLinkTarget(true);
            if (target == null)
            {
                var f = new FileInfo(P(path));
                target = f.ResolveLinkTarget(true);
            }
            if (target == null) return null;
            string full = target.FullName;
            if (Root != "/" && full.StartsWith(Root, StringComparison.Ordinal)) full = full[Root.TrimEnd('/', '\\').Length..].Replace('\\', '/');
            return full;
        }
        catch { return null; }
    }

    /// <summary>Último trecho do destino do link (ex.: .../drivers/nvidia → "nvidia").</summary>
    public static string? LinkName(string path)
    {
        try
        {
            string? t = new FileInfo(P(path)).LinkTarget ?? new DirectoryInfo(P(path)).LinkTarget;
            if (t == null) return null;
            return System.IO.Path.GetFileName(t.TrimEnd('/', '\\'));
        }
        catch { return null; }
    }

    /// <summary>Pares "chave: valor" de arquivos como /proc/meminfo e /etc/os-release (chave=valor).</summary>
    public static Dictionary<string, string> KeyValues(string path, char sep = ':')
    {
        var d = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in Lines(path))
        {
            int i = line.IndexOf(sep);
            if (i <= 0) continue;
            d[line[..i].Trim()] = line[(i + 1)..].Trim().Trim('"');
        }
        return d;
    }
}
