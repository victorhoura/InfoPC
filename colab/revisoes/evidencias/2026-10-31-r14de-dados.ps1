# Revisao R14-D/E e v1.1.4. Nao abre o InfoPC.exe nem altera o sistema.
# So usa o Core real e arquivos descartaveis em obj/review do worktree.
# Saida 0 confirma os resultados, inclusive BUG-*: nao significa aprovacao.
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..'))
$reviewDir = Join-Path $repo 'obj\review\r14de-data'
$previousData = [Environment]::GetEnvironmentVariable('INFOPC_DADOS')
New-Item -ItemType Directory -Force -Path $reviewDir | Out-Null
$reference = [Security.SecurityElement]::Escape((Join-Path $repo 'linux\src\InfoPC.Core\InfoPC.Core.csproj'))
$project = '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup><ItemGroup><ProjectReference Include="{0}" /></ItemGroup></Project>' -f $reference
$source = @'
using InfoPC;
using System.Text.Json;

var root = Path.GetFullPath(Path.Combine("obj", "review", "r14de-" + Guid.NewGuid().ToString("N")));
Directory.CreateDirectory(root);
int checks = 0;
void Check(bool ok, string name) { checks++; if (!ok) throw new Exception(name); }
void Log(string name, object data) => Console.WriteLine(name + " " + JsonSerializer.Serialize(data).Replace(JsonSerializer.Serialize(root)[1..^1], "<root>"));
var t = new DateTime(2026, 10, 31, 12, 0, 0, DateTimeKind.Utc);
int[][] orders = { new[] { 0, 1, 2 }, new[] { 0, 2, 1 }, new[] { 1, 0, 2 }, new[] { 1, 2, 0 }, new[] { 2, 0, 1 }, new[] { 2, 1, 0 } };

// R14-E: ancestral A, sucessora B e ramificacao C, em todas as posicoes.
// A tem a maior data, mas deve ser eliminada antes do desempate entre B e C.
foreach (var order in orders)
{
    var c = new Case(root, "branches-" + string.Join("", order));
    string[] dirs = { c.P, c.F, c.L };
    var basis = c.Save(Path.Combine(c.Root, "seed"), new[] { "root" }, t);
    var a = c.Save(dirs[order[0]], new[] { "ancestor" }, t.AddSeconds(20), basis);
    c.Save(dirs[order[1]], new[] { "successor" }, t, a);
    c.Save(dirs[order[2]], new[] { "branch" }, t.AddSeconds(10), basis);
    var chosen = c.Resolve(out var notice, true);
    Check(chosen == c.P && c.Groups(chosen) == "branch", "ancestor selected: " + string.Join("", order));
    Check(notice != null && notice.Contains("usando " + chosen), "wrong active path");
    Check(c.AllSettings().Any(p => File.ReadAllText(p).Contains("successor")), "successor lost");
}
Log("branches-six-positions", new { passed = 6 });

// Tres ramificacoes independentes: nenhuma pode sumir sem o aviso dizer onde.
{
    var c = new Case(root, "three-tips");
    var basis = c.Save(Path.Combine(c.Root, "seed"), new[] { "root" }, t);
    c.Save(c.P, new[] { "portable" }, t, basis);
    c.Save(c.F, new[] { "fallback" }, t, basis);
    c.Save(c.L, new[] { "legacy" }, t, basis);
    var chosen = c.Resolve(out var notice, true);
    var kept = Directory.GetDirectories(c.Root, "fallback.anterior-*").Single();
    Check(c.Groups(chosen) == "portable", "tied tips should keep usable");
    Check(notice!.Contains(kept) && notice.Contains(c.L), "notice omitted another tip");
    Check(c.Groups(kept) == "fallback" && c.Groups(c.L) == "legacy", "branch not preserved");
    Log("three-tips", new { groups = c.Groups(chosen), notice });
}

// Pasta em uso vazia e outra ilegivel: comportamento declarado, sem migracao.
{
    var c = new Case(root, "empty-usable-unread");
    c.Save(c.F, new[] { "undo" }, t);
    using (Case.Hold(c.F))
    {
        var chosen = c.Resolve(out var notice);
        Check(chosen == c.P && notice!.Contains("N\u00e3o consegui ler"), "empty usable must warn");
        Check(!File.Exists(Path.Combine(c.P, "settings.json")), "unexpected migration");
    }
    var back = c.Resolve(out _);
    Check(c.Groups(back) == "undo", "unlock did not recover latest");
    Log("empty-usable-unread", new { recovered = c.Groups(back) });
}

// Copia corrompida perde para trilha valida nos dois sentidos.
foreach (bool brokenPortable in new[] { false, true })
{
    var c = new Case(root, "corrupt-" + brokenPortable);
    string good = brokenPortable ? c.F : c.P, bad = brokenPortable ? c.P : c.F;
    c.Save(good, new[] { "undo" }, t);
    Directory.CreateDirectory(bad);
    File.WriteAllText(Path.Combine(bad, "settings.json"), "{broken");
    var chosen = c.Resolve(out var notice);
    Check(c.Groups(chosen) == "undo" && notice!.Contains("danificadas"), "corrupt copy silently discarded");
    Check(c.AllSettings().Any(p => File.ReadAllText(p) == "{broken"), "corrupt copy not preserved");
}
Log("corrupt-both-directions", new { passed = 2 });

// Caminho feliz que o autor testou: primeira gravacao depois de liberar o arquivo.
{
    var c = new Case(root, "save-after-unlock");
    c.Save(c.P, new[] { "undo" }, t);
    AppSettings.Folder = c.P;
    AppSettings empty;
    using (Case.Hold(c.P)) empty = AppSettings.Load();
    empty.Save();
    Check(File.ReadAllText(Path.Combine(c.P, "settings.json.corrompido")).Contains("undo"), "happy backup missing");
    Log("save-after-unlock", new { backupPreserved = true });
}

// BUG: um Save falha enquanto o mesmo arquivo continua preso; a segunda
// tentativa usa o mesmo objeto (como Services.Save), sem reabrir o programa.
{
    var c = new Case(root, "retry-save");
    c.Save(c.P, new[] { "undo-original" }, t);
    AppSettings.Folder = c.P;
    AppSettings empty;
    bool failed = false;
    using (Case.Hold(c.P))
    {
        empty = AppSettings.Load();
        try { empty.Save(); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { failed = true; }
    }
    Check(failed && empty.LoadError != null, "first save must fail under lock");
    Check(File.ReadAllText(Path.Combine(c.P, "settings.json")).Contains("undo-original"), "first save damaged original");
    empty.Save();
    bool originalRetained = c.AllSettings().Any(p => File.ReadAllText(p).Contains("undo-original"));
    Check(c.Groups(c.P) == "" && !originalRetained && !File.Exists(Path.Combine(c.P, "settings.json.tmp")), "BUG retry no longer reproduces; convert expectation");
    Log("BUG-save-retry", new { firstSaveFailed = failed, secondSaveSucceeded = true, loaded = c.Groups(c.P), originalRetained });
}

// BUG: a copia .corrompido esta presa, mas o settings.json ja foi liberado.
// O Save tambem deve recusar sobrescrever se nao consegue preservar a origem.
{
    var c = new Case(root, "backup-blocked");
    c.Save(c.P, new[] { "undo-original" }, t);
    string backup = Path.Combine(c.P, "settings.json.corrompido");
    File.WriteAllText(backup, "older-backup");
    AppSettings.Folder = c.P;
    AppSettings empty;
    using (var old = new FileStream(backup, FileMode.Open, FileAccess.Read, FileShare.None))
    {
        using (Case.Hold(c.P)) empty = AppSettings.Load();
        empty.Save();
    }
    bool originalRetained = c.AllSettings().Any(p => File.ReadAllText(p).Contains("undo-original"));
    Check(c.Groups(c.P) == "" && File.ReadAllText(backup) == "older-backup" && !originalRetained && !File.Exists(Path.Combine(c.P, "settings.json.tmp")), "BUG blocked backup no longer reproduces; convert expectation");
    Log("BUG-backup-blocked", new { saveSucceeded = true, loaded = c.Groups(c.P), backup = File.ReadAllText(backup), originalRetained });
}
// BUG: a legibilidade muda depois da prechecagem. F prende CanRead nos retries;
// P so e preso depois que a sua checagem inicial ja passou. Nenhum JSON ou
// DataTrail e adulterado. O resultado e uma troca funcional para a ancestral.
for (int iteration = 0; iteration < 3; iteration++)
{
    var c = new Case(root, "late-read-lock-" + iteration);
    var basis = c.Save(c.F, new[] { "old" }, t);
    c.Save(c.P, new[] { "old", "new" }, t, basis);
    var holdFallback = Case.Hold(c.F);
    FileStream? holdPortable = null;
    Exception? workerError = null;
    var worker = new Thread(() =>
    {
        try
        {
            Thread.Sleep(120);
            holdPortable = Case.Hold(c.P);
        }
        catch (Exception ex) { workerError = ex; }
        finally { holdFallback.Dispose(); }
    });
    worker.Start();
    string chosen;
    string? notice;
    try { chosen = c.Resolve(out notice); }
    finally { worker.Join(); holdPortable?.Dispose(); holdFallback.Dispose(); }
    Check(workerError == null, "late lock setup failed: " + workerError);
    Check(chosen == c.F && c.Groups(chosen) == "old" && c.Groups(c.P) == "old,new", "BUG late lock no longer reproduces; convert expectation");
    Check(notice!.Contains("N\u00e3o consegui copiar") && !notice.Contains("N\u00e3o consegui ler"), "late lock unexpectedly classified as unreadable");
    Log("BUG-late-read-lock-" + iteration, new { loaded = c.Groups(chosen), portable = c.Groups(c.P), notice });
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
    public string Resolve(out string? notice, bool legacy = false) => DataFolder.Resolve(E, "0123456789abcdef", F, out notice, legacy ? L : null, legacy);
    public AppSettings Save(string dir, string[] groups, DateTime when, AppSettings? parent = null)
    {
        AppSettings.Folder = dir;
        var state = parent == null ? new AppSettings() : JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(parent))!;
        state.GroupsAdded = groups.ToList();
        state.Save();
        File.SetLastWriteTimeUtc(Path.Combine(dir, "settings.json"), when);
        return state;
    }
    public string Groups(string dir) { AppSettings.Folder = dir; return string.Join(",", AppSettings.Load().GroupsAdded); }
    public IEnumerable<string> AllSettings() => Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories)
        .Where(p => Path.GetFileName(p).Equals("settings.json", StringComparison.OrdinalIgnoreCase) || Path.GetFileName(p).Equals("settings.json.corrompido", StringComparison.OrdinalIgnoreCase));
    public static FileStream Hold(string dir) => new(Path.Combine(dir, "settings.json"), FileMode.Open, FileAccess.Read, FileShare.None);
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
