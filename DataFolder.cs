using System.IO;
using System.Text;
using System.Text.Json;

namespace InfoPC;

/// <summary>
/// Pasta de dados portátil das duas edições (requisito 3, D-028): "dados\&lt;identidade&gt;" ao lado do executável. A identidade separa cada PC e usuário:
/// o programa pode ir num pen drive, e o desfazer de um PC (registro, serviços, pacotes) não pode ser aplicado em outro.
/// </summary>
public static class DataFolder
{
    private const string Partial = ".migrando";
    private const string Previous = ".anterior-";
    private const string Probe = ".gravavel-";
    private const int TrailSize = 256;
    /// <summary>A data de gravação do FAT tem resolução de 2 segundos: dentro disso, a data não diz qual cópia veio depois.</summary>
    private static readonly TimeSpan Tolerance = TimeSpan.FromSeconds(2);

    private enum Relation { Same, FirstNewer, SecondNewer, Diverged }

    /// <summary>
    /// 16 letras hexadecimais a partir da máquina e do usuário, para não deixar o nome de ninguém visível na pasta do pen drive. É o FNV-1a de 64 bits, e não SHA-256:
    /// no Linux o SHA-256 do .NET carrega o libssl, e o programa abortaria ao abrir num sistema sem ele. Isto só escolhe um nome de pasta, não protege nada.
    /// </summary>
    public static string Identity(string machine, string user)
    {
        ulong hash = 14695981039346656037;
        foreach (byte b in Encoding.UTF8.GetBytes(machine + "\n" + user)) hash = (hash ^ b) * 1099511628211;
        return hash.ToString("x16");
    }

    private static bool IsIdentity(string name) => name.Length == 16 && name.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');

    /// <summary>
    /// A pasta a usar: ao lado do executável ou, sem poder gravar lá, <paramref name="fallback"/>. Os dados vêm de onde estiverem mais novos, entre essas duas e
    /// <paramref name="legacy"/>: a pasta em uso nunca abre vazia nem com uma cópia antiga enquanto os registros de desfazer estão em outro lugar.
    /// O que a pasta em uso tinha fica em "&lt;pasta&gt;.anterior-&lt;data&gt;", e se a cópia falhar o programa continua onde os dados estão.
    /// Mais novo é o que a trilha de gravações (<see cref="Advance"/>) prova ter vindo depois. Duas cópias que nenhuma trilha ordena nunca se sobrescrevem em silêncio.
    /// </summary>
    /// <param name="moveLegacy">Apaga <paramref name="legacy"/> depois de copiar. Serve para a antiga pasta "dados" comum, que o próximo PC não pode adotar como se fosse dele.</param>
    /// <param name="notice">O que o usuário precisa saber, se a pasta mudou, se dados foram trocados ou se nada pode ser gravado.</param>
    public static string Resolve(string exeDir, string identity, string fallback, out string? notice, string? legacy = null, bool moveLegacy = false)
    {
        notice = null;
        string? portable = string.IsNullOrEmpty(exeDir) ? null : Path.Combine(exeDir, "dados", identity);
        string? usable = portable != null && Writable(portable) ? portable : Writable(fallback) ? fallback : null;

        // a pasta em uso vem primeiro: num empate, ela fica
        var found = new List<string>();
        foreach (var d in new[] { usable, portable, fallback, legacy }.OfType<string>())
            if (HasData(d) && !found.Any(f => Same(f, d))) found.Add(d);

        string? newest = null;
        foreach (var d in found)
        {
            if (newest == null) { newest = d; continue; }
            var relation = Compare(newest, d);
            if (relation == Relation.SecondNewer || (relation == Relation.Diverged && DataTime(d) - DataTime(newest) > Tolerance)) newest = d;
        }
        string? other = newest == null ? null : found.FirstOrDefault(d => !Same(d, newest) && Compare(newest, d) == Relation.Diverged);

        if (usable == null)
        {
            notice = "Não consigo gravar na pasta do programa nem em " + fallback + ". O InfoPC abre, mas as alterações no sistema ficam bloqueadas porque não haveria como guardar o desfazer.";
            return newest ?? fallback;
        }
        if (newest == null) return usable;
        if (Same(newest, usable))
        {
            if (other != null) notice = DivergedNotice(usable, legacy != null && Same(other, legacy) ? other : SetAside(other));
            return usable;
        }

        bool replaces = HasData(usable);
        if (!Migrate(newest, usable, moveLegacy && legacy != null && Same(newest, legacy)))
        {
            notice = $"Não consegui copiar os dados para {usable}. O InfoPC continua usando {newest}, onde estão os registros de desfazer.";
            return newest;
        }
        if (other != null) notice = DivergedNotice(newest, Same(other, usable) ? "uma pasta \".anterior\" ao lado de " + usable : legacy != null && Same(other, legacy) ? other : SetAside(other));
        else if (replaces) notice = $"Os dados mais recentes estavam em {newest} e foram copiados para {usable}. O que havia lá ficou guardado em uma pasta \".anterior\" ao lado.";
        else if (Same(usable, fallback) && portable != null) notice = $"Não consigo mais gravar na pasta do programa. Os dados foram copiados para {usable} e continuam a partir de lá.";
        return usable;
    }

    /// <summary>
    /// A próxima trilha de gravações: cada Save acrescenta uma marca, e uma cópia leva as marcas junto. Se a última marca de uma cópia está na trilha da outra, a outra veio depois,
    /// sem depender do relógio nem da precisão do sistema de arquivos. Não é número de segurança, só distingue gravações.
    /// </summary>
    public static List<string> Advance(IEnumerable<string>? trail)
    {
        var next = trail?.ToList() ?? new List<string>();
        next.Add(Random.Shared.NextInt64().ToString("x16"));
        if (next.Count > TrailSize) next.RemoveRange(0, next.Count - TrailSize);
        return next;
    }

    private static string DivergedNotice(string used, string kept) =>
        $"Há duas cópias dos dados com alterações diferentes, e não dá para saber qual veio depois. O InfoPC está usando {used}; a outra cópia está em {kept}. Nada foi apagado, mas os registros de desfazer que só existem nela não aparecem aqui.";

    /// <summary>Tira do caminho a cópia que não foi escolhida, para o aviso não repetir a cada abertura. Sem conseguir, ela fica onde está, e o aviso volta.</summary>
    private static string SetAside(string dir)
    {
        try
        {
            string kept = $"{dir}{Previous}{DateTime.Now:yyyyMMdd-HHmmss}";
            Directory.Move(dir, kept);
            return kept;
        }
        catch { return dir; }
    }

    private static Relation Compare(string a, string b)
    {
        var (first, second) = (Trail(a), Trail(b));
        if (first != null && second != null)
        {
            if (first[^1] == second[^1]) return Relation.Same;
            if (second.Contains(first[^1])) return Relation.SecondNewer;
            return first.Contains(second[^1]) ? Relation.FirstNewer : Relation.Diverged;
        }
        // sem trilha, a cópia é de uma versão anterior: quem já gravou com trilha continuou a partir dela
        if (first != null) return Relation.FirstNewer;
        if (second != null) return Relation.SecondNewer;
        var gap = DataTime(a) - DataTime(b);
        if (gap.Duration() > Tolerance) return gap > TimeSpan.Zero ? Relation.FirstNewer : Relation.SecondNewer;
        return SameSettings(a, b) ? Relation.Same : Relation.Diverged;
    }

    private static List<string>? Trail(string dir)
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(dir, "settings.json")));
            if (!doc.RootElement.TryGetProperty("DataTrail", out var trail) || trail.ValueKind != JsonValueKind.Array) return null;
            var marks = trail.EnumerateArray().Where(m => m.ValueKind == JsonValueKind.String).Select(m => m.GetString()!).ToList();
            return marks.Count > 0 ? marks : null;
        }
        catch { return null; }
    }

    private static bool SameSettings(string a, string b)
    {
        try
        {
            string first = Path.Combine(a, "settings.json"), second = Path.Combine(b, "settings.json");
            if (!File.Exists(first) || !File.Exists(second)) return File.Exists(first) == File.Exists(second);
            return File.ReadAllBytes(first).AsSpan().SequenceEqual(File.ReadAllBytes(second));
        }
        catch { return false; }
    }

    public static bool Writable(string dir)
    {
        try
        {
            Directory.CreateDirectory(dir);
            string probe = Path.Combine(dir, Probe + Environment.ProcessId);
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return true;
        }
        catch { return false; }
    }

    private static bool Same(string a, string b) =>
        string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)), Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)),
                      OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static IEnumerable<string> Entries(string dir) =>
        Directory.EnumerateFileSystemEntries(dir).Where(e =>
        {
            string name = Path.GetFileName(e);
            return !IsIdentity(name) && !name.EndsWith(Partial, StringComparison.Ordinal) && !name.Contains(Previous, StringComparison.Ordinal) && !name.StartsWith(Probe, StringComparison.Ordinal);
        });

    private static bool HasData(string dir)
    {
        try { return Directory.Exists(dir) && Entries(dir).Any(); }
        catch { return false; }
    }

    /// <summary>Quando os dados mudaram por último. O Save sempre grava o settings.json; sem ele, vale o arquivo mais novo.</summary>
    private static DateTime DataTime(string dir)
    {
        try
        {
            string settings = Path.Combine(dir, "settings.json");
            if (File.Exists(settings)) return File.GetLastWriteTimeUtc(settings);
            return Entries(dir).SelectMany(e => Directory.Exists(e) ? Directory.EnumerateFiles(e, "*", SearchOption.AllDirectories) : new[] { e })
                               .Select(File.GetLastWriteTimeUtc).DefaultIfEmpty(DateTime.MinValue).Max();
        }
        catch { return DateTime.MinValue; }
    }

    /// <summary>Copia para uma pasta temporária e só então troca o nome: uma cópia interrompida nunca parece a pasta de dados. File.Copy mantém a data, então abrir de novo não copia outra vez.</summary>
    private static bool Migrate(string source, string target, bool moveSource)
    {
        string partial = target + Partial;
        try
        {
            if (Directory.Exists(partial)) Directory.Delete(partial, true);
            Directory.CreateDirectory(partial);
            var entries = Entries(source).ToList();
            foreach (var e in entries)
            {
                string to = Path.Combine(partial, Path.GetFileName(e));
                if (Directory.Exists(e)) CopyTree(e, to);
                else File.Copy(e, to);
            }
            if (HasData(target)) Directory.Move(target, $"{target}{Previous}{DateTime.Now:yyyyMMdd-HHmmss}");
            else Directory.Delete(target, true);
            Directory.Move(partial, target);
            if (moveSource)
                foreach (var e in entries)
                    try { if (Directory.Exists(e)) Directory.Delete(e, true); else File.Delete(e); } catch { }
            return true;
        }
        catch
        {
            try { Directory.Delete(partial, true); } catch { }
            return false;
        }
    }

    private static void CopyTree(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var f in Directory.GetFiles(from)) File.Copy(f, Path.Combine(to, Path.GetFileName(f)));
        foreach (var d in Directory.GetDirectories(from)) CopyTree(d, Path.Combine(to, Path.GetFileName(d)));
    }

    /// <summary>Guarda um settings.json que não deu para ler. O próximo Save o sobrescreveria, com todos os registros de desfazer dele.</summary>
    public static void KeepUnreadable(string settingsFile)
    {
        try { File.Copy(settingsFile, settingsFile + ".corrompido", true); } catch { }
    }
}
