# Reproducoes da revisao R14-C / v1.1.3. Nao executa o InfoPC.exe.
# So grava arquivos descartaveis em obj/review do worktree atual.
# Saida 0 confirma as observacoes registradas; R14-D/E agora conferem a correcao (claude). Nao aprova o produto.
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..'))
$reviewDir = Join-Path $repo 'obj\review\r14-data'
$previousData = [Environment]::GetEnvironmentVariable('INFOPC_DADOS')
New-Item -ItemType Directory -Force -Path $reviewDir | Out-Null
$projectReference = [Security.SecurityElement]::Escape((Join-Path $repo 'linux\src\InfoPC.Core\InfoPC.Core.csproj'))
$project = '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup><ItemGroup><ProjectReference Include="{0}" /></ItemGroup></Project>' -f $projectReference
$source = @'
using InfoPC;
using System.Text.Json;

var root = Path.GetFullPath(Path.Combine("obj", "review", "cases-" + Guid.NewGuid().ToString("N")));
Directory.CreateDirectory(root);
int checks = 0;
void Check(bool ok, string what) { checks++; if (!ok) throw new Exception(what); }
void Log(string name, object result) => Console.WriteLine(name + " " + JsonSerializer.Serialize(result));
var t = new DateTime(2026, 10, 30, 12, 0, 0, DateTimeKind.Utc);

// R14-C: ida e volta usando Save/Load reais com datas identicas.
{
    var c = new Case(root, "same-date");
    c.Save(c.F, new[] { "dialout" }, t);
    Check(c.Resolve(out _) == c.P, "first migration");
    c.Save(c.P, new[] { "dialout", "uucp" }, t);
    using (c.Block(c.P))
    {
        string chosen = c.Resolve(out var notice);
        Check(chosen == c.F && c.Groups(chosen) == "dialout,uucp" && notice != null, "same-date fallback");
        c.Save(c.F, new[] { "dialout", "uucp", "plugdev" }, t);
    }
    string back = c.Resolve(out var last);
    Check(back == c.P && c.Groups(back) == "dialout,uucp,plugdev", "same-date return");
    c.Resolve(out var again);
    Check(again == null, "same-date repeated notice");
    Log("same-date", new { groups = c.Groups(back), last, again });
}
{
    var c = new Case(root, "rounded");
    c.Save(c.F, new[] { "dialout" }, t.AddMilliseconds(800));
    c.Resolve(out _);
    c.Save(c.P, new[] { "dialout", "uucp" }, t);
    string chosen = c.Resolve(out var notice);
    Check(chosen == c.P && c.Groups(chosen) == "dialout,uucp" && notice == null, "rounded chronology");
    Log("rounded", new { groups = c.Groups(chosen), notice });
}
{
    var c = new Case(root, "restore-no-union");
    c.Save(c.F, new[] { "dialout", "uucp" }, t);
    c.Resolve(out _);
    c.Save(c.P, Array.Empty<string>(), t);
    using (c.Block(c.P))
    {
        var chosen = c.Resolve(out var notice);
        Check(c.Groups(chosen) == "", "restored records resurrected");
        Log("restore-no-union", new { groups = c.Groups(chosen), notice });
    }
}

// Todas as seis posicoes de tres estados da mesma linhagem, inclusive dados/ antiga.
foreach (var order in new[] { new[] { 0, 1, 2 }, new[] { 0, 2, 1 }, new[] { 1, 0, 2 }, new[] { 1, 2, 0 }, new[] { 2, 0, 1 }, new[] { 2, 1, 0 } })
{
    var c = new Case(root, "chain-" + string.Join("", order));
    var dirs = new[] { c.P, c.F, c.L };
    AppSettings? previous = null;
    for (int stage = 0; stage < 3; stage++)
    {
        string dir = dirs[Array.IndexOf(order, stage)];
        previous = c.Save(dir, Enumerable.Range(0, stage + 1).Select(i => "g" + i).ToArray(), t, previous);
    }
    string chosen = c.Resolve(out var notice, legacy: true, move: true);
    Check(chosen == c.P && c.Groups(chosen) == "g0,g1,g2", "three-chain " + string.Join("", order));
    Check(notice == null || !notice.Contains("altera\u00e7\u00f5es diferentes"), "false divergence in chain");
    Log("chain-" + string.Join("", order), new { groups = c.Groups(chosen), notice });
}
{
    var c = new Case(root, "three-diverged");
    var basis = c.Save(c.P, new[] { "base" }, t);
    c.Save(c.F, new[] { "base", "fallback" }, t, basis);
    c.Save(c.L, new[] { "base", "legacy" }, t, basis);
    c.Save(c.P, new[] { "base", "portable" }, t, basis);
    string chosen = c.Resolve(out var first, true, true);
    string chosenAgain = c.Resolve(out var second, true, true);
    Check(c.Groups(chosen) == "base,portable" && first != null && second != null && File.Exists(Path.Combine(c.L, "settings.json")), "three divergent preservation");
    Log("three-diverged", new { groups = c.Groups(chosenAgain), first, second });
}
{
    var c = new Case(root, "legacy-only");
    Directory.CreateDirectory(c.L);
    File.WriteAllText(Path.Combine(c.L, "settings.json"), "{\"GroupsAdded\":[\"legacy\"]}");
    var stranger = Path.Combine(c.L, "fedcba9876543210");
    Directory.CreateDirectory(stranger);
    File.WriteAllText(Path.Combine(stranger, "settings.json"), "{\"GroupsAdded\":[\"other-pc\"]}");
    var chosen = c.Resolve(out var notice, true, true);
    Check(c.Groups(chosen) == "legacy" && !File.Exists(Path.Combine(c.L, "settings.json")) && File.Exists(Path.Combine(stranger, "settings.json")), "legacy and unrelated identity");
    Log("legacy-only", new { groups = c.Groups(chosen), otherIdentity = File.Exists(Path.Combine(stranger, "settings.json")), notice });
}
{
    var c = new Case(root, "copy-failure");
    var basis = c.Save(c.P, new[] { "old" }, t);
    c.Save(c.F, new[] { "old", "new" }, t, basis);
    Directory.CreateDirectory(Path.Combine(c.F, "Startup"));
    string aux = Path.Combine(c.F, "Startup", "backup.lnk");
    File.WriteAllText(aux, "backup");
    using var locked = new FileStream(aux, FileMode.Open, FileAccess.Read, FileShare.None);
    string chosen = c.Resolve(out var notice);
    Check(chosen == c.F && c.Groups(chosen) == "old,new" && c.Groups(c.P) == "old" && notice!.Contains("N\u00e3o consegui copiar"), "copy failure must retain source");
    Log("copy-failure", new { source = chosen == c.F, groups = c.Groups(chosen), target = c.Groups(c.P), notice });
}
{
    var c = new Case(root, "both-unwritable");
    var basis = c.Save(c.P, new[] { "old" }, t);
    c.Save(c.F, new[] { "old", "new" }, t, basis);
    using var bp = c.Block(c.P);
    using var bf = c.Block(c.F);
    var chosen = c.Resolve(out var notice);
    Check(chosen == c.F && c.Groups(chosen) == "old,new" && notice!.Contains("bloqueadas"), "both write probes denied");
    Log("both-unwritable", new { groups = c.Groups(chosen), notice });
}

// O arquivo somente leitura e temporario: nenhum ACL ou configuracao do Windows e alterado.
{
    var c = new Case(root, "readonly-settings");
    var basis = c.Save(c.F, new[] { "old" }, t);
    c.Save(c.P, new[] { "old", "new" }, t, basis);
    string settings = Path.Combine(c.P, "settings.json");
    File.SetAttributes(settings, FileAttributes.ReadOnly);
    try
    {
        using var bp = c.Block(c.P);
        var chosen = c.Resolve(out var notice);
        Check(chosen == c.F && c.Groups(chosen) == "old,new", "readonly source copy");
        Log("readonly-settings", new { groups = c.Groups(chosen), notice });
    }
    finally { File.SetAttributes(settings, FileAttributes.Normal); }
}

// Falha de leitura da trilha nao pode parecer versao antiga. Registra o comportamento real.
{
    var c = new Case(root, "locked-settings");
    var basis = c.Save(c.P, new[] { "dialout" }, t);
    c.Save(c.F, new[] { "dialout", "uucp" }, t, basis);
    string chosen;
    string? notice;
    using (var locked = new FileStream(Path.Combine(c.F, "settings.json"), FileMode.Open, FileAccess.Read, FileShare.None))
    {
        chosen = c.Resolve(out notice);
        Check(chosen == c.P && c.Groups(chosen) == "dialout" && notice!.Contains("N\u00e3o consegui ler"), "locked settings must warn");
        Log("locked-settings", new { groups = c.Groups(chosen), notice });
    }
    Check(c.Groups(c.F) == "dialout,uucp", "locked source was modified");
}

// Candidatas sem ordem total: uma candidata ja vencida volta a ganhar na ultima comparacao.
{
    var c = new Case(root, "locked-portable-settings");
    var basis = c.Save(c.F, new[] { "dialout" }, t);
    c.Save(c.P, new[] { "dialout", "uucp" }, t, basis);
    using var probe = c.Block(c.P);
    using var locked = new FileStream(Path.Combine(c.P, "settings.json"), FileMode.Open, FileAccess.Read, FileShare.None);
    string chosen = c.Resolve(out var notice);
    Check(chosen == c.F && c.Groups(chosen) == "dialout" && notice!.Contains("N\u00e3o consegui ler"), "locked portable must warn");
    Log("locked-portable-settings", new { groups = c.Groups(chosen), notice });
}
// Mais de 256 gravacoes perdem a prova de parentesco, mas devem avisar.
{
    var c = new Case(root, "truncated-trail");
    var basis = c.Save(c.F, new[] { "old" }, t);
    for (int i = 0; i < 257; i++) basis = c.Save(c.P, new[] { "new" }, t, basis);
    var chosen = c.Resolve(out var notice);
    Check(c.Groups(chosen) == "new" && notice!.Contains("altera\u00e7\u00f5es diferentes"), "truncated trail must warn");
    Log("truncated-trail", new { groups = c.Groups(chosen), notice });
}
{
    var c = new Case(root, "dominated-three");
    var basis = c.Save(Path.Combine(c.Root, "seed"), new[] { "base" }, t);
    var ancestor = c.Save(c.L, new[] { "dialout" }, t.AddSeconds(20), basis);
    c.Save(c.P, new[] { "dialout", "uucp" }, t, ancestor);
    c.Save(c.F, new[] { "plugdev" }, t.AddSeconds(10), basis);
    string chosen = c.Resolve(out var notice, true, true);
    var keptPortable = Directory.GetDirectories(c.L, "0123456789abcdef.anterior-*").Single();
    Check(chosen == c.P && c.Groups(chosen) == "plugdev" && c.Groups(keptPortable) == "dialout,uucp" && notice!.Contains("usando " + c.P) && notice.Contains(keptPortable), "ancestor must not win");
    Log("dominated-three", new { groups = c.Groups(chosen), notice, portableArchives = Directory.GetDirectories(c.L, "0123456789abcdef.anterior-*").Select(c.Groups).ToArray(), fallbackArchives = Directory.GetDirectories(c.Root, "fallback.anterior-*").Select(c.Groups).ToArray() });
}
Console.WriteLine($"CHECKS={checks}; ROOT={root}");

sealed class Case
{
    public string Root { get; }
    public string E => Path.Combine(Root, "exe");
    public string F => Path.Combine(Root, "fallback");
    public string L => Path.Combine(E, "dados");
    public string P => Path.Combine(L, "0123456789abcdef");
    public Case(string root, string name) { Root = Path.Combine(root, name); Directory.CreateDirectory(Root); }
    public string Resolve(out string? notice, bool legacy = false, bool move = false) => DataFolder.Resolve(E, "0123456789abcdef", F, out notice, legacy ? L : null, move);
    public AppSettings Save(string dir, string[] groups, DateTime when, AppSettings? parent = null)
    {
        AppSettings.Folder = dir;
        var state = parent == null ? AppSettings.Load() : JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(parent))!;
        state.GroupsAdded = groups.ToList();
        state.Save();
        File.SetLastWriteTimeUtc(Path.Combine(dir, "settings.json"), when);
        return state;
    }
    public string Groups(string dir) { AppSettings.Folder = dir; return string.Join(",", AppSettings.Load().GroupsAdded); }
    public FileStream Block(string dir)
    {
        Directory.CreateDirectory(dir);
        return new FileStream(Path.Combine(dir, ".gravavel-" + Environment.ProcessId), FileMode.Create, FileAccess.Write, FileShare.None);
    }
}
'@
[IO.File]::WriteAllText((Join-Path $reviewDir 'CoreReview.csproj'), $project, [Text.Encoding]::UTF8)
[IO.File]::WriteAllText((Join-Path $reviewDir 'Program.cs'), $source, [Text.Encoding]::UTF8)
Push-Location $repo
try {
    $env:INFOPC_DADOS = Join-Path $reviewDir 'initial-data'
    & dotnet run --project (Join-Path $reviewDir 'CoreReview.csproj') -c Release
    if ($LASTEXITCODE -ne 0) { throw ('Reproducao falhou, codigo ' + $LASTEXITCODE) }
}
finally {
    [Environment]::SetEnvironmentVariable('INFOPC_DADOS', $previousData)
    Pop-Location
}
