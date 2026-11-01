# Revisao R14-F/G e v1.1.5. Nao abre InfoPC.exe nem altera o sistema.
# Usa Core e Avalonia reais, com fixtures e render headless. Tudo em obj/review.
# R14-H agora confere o aviso presente (claude); passar nao significa aprovar.
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..'))
$reviewDir = Join-Path $repo 'obj\review\r14fg-data'
New-Item -ItemType Directory -Force -Path $reviewDir | Out-Null
$escapedRepo = [Security.SecurityElement]::Escape($repo)
$project = @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable><IsTestProject>true</IsTestProject><IsPackable>false</IsPackable>
    <AssemblyName>InfoPC.Tests</AssemblyName>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
    <PackageReference Include="xunit" Version="2.9.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="3.1.4" />
    <PackageReference Include="Avalonia.Headless.XUnit" Version="11.3.22" />
    <PackageReference Include="Avalonia.Skia" Version="11.3.22" />
    <ProjectReference Include="REPOROOT\linux\src\InfoPC.Linux\InfoPC.Linux.csproj" />
    <Compile Include="REPOROOT\linux\tests\InfoPC.Tests\HeadlessSetup.cs" Link="HeadlessSetup.cs" />
    <Compile Include="REPOROOT\linux\tests\InfoPC.Tests\Fx.cs" Link="Fx.cs" />
    <Content Include="REPOROOT\linux\tests\InfoPC.Tests\Fixtures\**\*" Link="Fixtures\%(RecursiveDir)%(Filename)%(Extension)" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>
</Project>
'@
$source = @'
using System.Reflection;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using InfoPC;
using InfoPC.Linux;
using InfoPC.Tests;
using Xunit;
using Xunit.Abstractions;

public sealed class ReviewR14fg
{
    private readonly ITestOutputHelper log;
    public ReviewR14fg(ITestOutputHelper output) => log = output;
    private static string NewCase(string name) => Directory.CreateDirectory(Path.Combine(
        Environment.GetEnvironmentVariable("REVIEW_ROOT")!, name + "-" + Guid.NewGuid().ToString("N"))).FullName;
    private static string FileAt(string dir) => Path.Combine(dir, "settings.json");
    private static FileStream Hold(string file) => new(file, FileMode.Open, FileAccess.Read, FileShare.None);
    private static void Notice(string? value) => typeof(AppSettings).GetField("<FolderNotice>k__BackingField", BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, value);

    [Fact]
    public void Repeated_failed_saves_keep_the_original_and_retry_preservation()
    {
        string dir = NewCase("five-retries");
        AppSettings.Folder = dir;
        new AppSettings { GroupsAdded = { "undo-original" } }.Save();
        string original = File.ReadAllText(FileAt(dir));
        AppSettings empty;
        using (Hold(FileAt(dir)))
        {
            empty = AppSettings.Load();
            Assert.NotNull(empty.LoadError);
            for (int i = 0; i < 5; i++) Assert.ThrowsAny<IOException>(() => empty.Save());
            Assert.False(File.Exists(FileAt(dir) + ".tmp"));
        }
        Assert.Equal(original, File.ReadAllText(FileAt(dir)));
        empty.Save();
        Assert.Equal(original, File.ReadAllText(FileAt(dir) + ".corrompido"));
        Assert.Empty(AppSettings.Load().GroupsAdded);
        log.WriteLine("five-retries: original byte-for-byte preserved; final Save succeeded");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Existing_backup_is_preserved_and_identical_content_is_reused(bool identical)
    {
        string dir = NewCase("existing-backup");
        AppSettings.Folder = dir;
        new AppSettings { GroupsAdded = { "undo-original" } }.Save();
        string original = File.ReadAllText(FileAt(dir));
        string prior = identical ? original : "older-backup";
        File.WriteAllText(FileAt(dir) + ".corrompido", prior);
        AppSettings empty;
        using (Hold(FileAt(dir))) empty = AppSettings.Load();
        empty.Save();
        Assert.Equal(prior, File.ReadAllText(FileAt(dir) + ".corrompido"));
        var extra = Directory.GetFiles(dir, "settings.json.corrompido-*");
        if (identical) Assert.Empty(extra);
        else Assert.Equal(original, File.ReadAllText(Assert.Single(extra)));
        log.WriteLine($"existing-backup identical={identical}: both versions preserved");
    }

    [Fact]
    public void Unavailable_backup_destination_refuses_Save_and_can_retry()
    {
        string dir = NewCase("backup-destination-directory");
        AppSettings.Folder = dir;
        new AppSettings { GroupsAdded = { "undo-original" } }.Save();
        string original = File.ReadAllText(FileAt(dir));
        AppSettings empty;
        using (Hold(FileAt(dir))) empty = AppSettings.Load();
        string destination = FileAt(dir) + ".corrompido";
        Directory.CreateDirectory(destination);
        Assert.ThrowsAny<Exception>(() => empty.Save());
        Assert.Equal(original, File.ReadAllText(FileAt(dir)));
        Directory.Delete(destination); // empty directory created immediately above, inside REVIEW_ROOT
        empty.Save();
        Assert.Equal(original, File.ReadAllText(destination));
        log.WriteLine("unavailable-destination: no overwrite on failure; retry preserved original");
    }

    [Fact]
    public void Failure_after_preservation_leaves_a_durable_copy_for_the_next_Save()
    {
        string dir = NewCase("tmp-locked");
        AppSettings.Folder = dir;
        new AppSettings { GroupsAdded = { "undo-original" } }.Save();
        string original = File.ReadAllText(FileAt(dir));
        AppSettings empty;
        using (Hold(FileAt(dir))) empty = AppSettings.Load();
        string temp = FileAt(dir) + ".tmp";
        File.WriteAllText(temp, "temporary-marker");
        using (Hold(temp)) Assert.ThrowsAny<IOException>(() => empty.Save());
        Assert.Equal(original, File.ReadAllText(FileAt(dir)));
        Assert.Equal(original, File.ReadAllText(FileAt(dir) + ".corrompido"));
        empty.Save();
        Assert.Equal(original, File.ReadAllText(FileAt(dir) + ".corrompido"));
        log.WriteLine("tmp-locked: preservation succeeded before failed write; retry safe");
    }

    [Fact]
    public void System_changing_callbacks_refuse_to_proceed_when_preservation_fails()
    {
        string dir = NewCase("callbacks");
        using var fixture = Fx.Use("intel-desktop");
        AppSettings.Folder = dir;
        new AppSettings { GroupsAdded = { "undo-original" } }.Save();
        AppSettings empty;
        using var held = Hold(FileAt(dir));
        empty = AppSettings.Load();
        int writes = 0;
        var tweak = new Tweak { Id = "test", Remember = s => s.TweakBackup["test"] = "original",
            Write = (_, _) => { writes++; return new RootResult(true, "", ""); } };
        var result = LinuxTweaks.Apply(tweak, true, empty, empty.Save);
        Assert.False(result.Ok);
        Assert.Equal(0, writes);
        Assert.Empty(empty.TweakBackup);
        int commands = 0;
        Shell.Override = (_, _) => { commands++; return new ProcResult(0, "", ""); };
        string? error = StartupManager.SetEnabled(new StartupEntry { Kind = StartupKind.SystemdUser,
            File = "example.service", Comment = "test" }, false, empty, empty.Save);
        Assert.NotNull(error);
        Assert.Equal(0, commands);
        Assert.Empty(empty.SystemdDisabled);
        typeof(Services).GetProperty(nameof(Services.Settings))!.SetValue(Services.Current, empty);
        Services.Current.Save(); // actual wrapper must catch the preservation error
        Assert.False(File.Exists(FileAt(dir) + ".tmp"));
        log.WriteLine("callbacks: tweak writes=0; systemctl commands=0; Services.Save returned normally");
    }

    [AvaloniaTheory]
    [InlineData("damaged", true)]
    [InlineData("locked-after-resolve", true)]
    [InlineData("resolve-notice-control", false)]
    public async Task Linux_first_Load_notice_reaches_the_dialog_check(string mode, bool missing)
    {
        using var fixture = Fx.Use("intel-desktop");
        string dir = NewCase(mode);
        Environment.SetEnvironmentVariable("INFOPC_DADOS", dir);
        AppSettings.Folder = dir;
        Notice(null); // isolate the same static state as a fresh process; never change UI code
        new AppSettings { GroupsAdded = { "undo-original" } }.Save();
        if (mode == "damaged") File.WriteAllText(FileAt(dir), "{ damaged: undo-original");
        // Real Resolve, a single usable candidate, with fallback empty. No notice for the damaged singleton.
        string exe = Path.Combine(dir, "exe");
        string portable = Path.Combine(exe, "dados", "0123456789abcdef");
        Directory.CreateDirectory(portable);
        File.Copy(FileAt(dir), FileAt(portable));
        AppSettings.Folder = DataFolder.Resolve(exe, "0123456789abcdef", Path.Combine(dir, "fallback"), out var resolved);
        Assert.Null(resolved);
        FileStream? held = mode == "damaged" ? null : Hold(FileAt(portable));
        if (!missing) Notice("Control: an unread notice already exists before Load");
        Services.Current.ResetForTests();
        var window = new MainWindow();
        try
        {
            window.Show();
            await Services.Current.WhenReady.WaitAsync(TimeSpan.FromSeconds(15));
            for (int i = 0; i < 5; i++) { await Task.Delay(30); Dispatcher.UIThread.RunJobs(); }
            Assert.NotNull(Services.Current.Settings.LoadError);
            Assert.Empty(Services.Current.Settings.GroupsAdded);
            Assert.Contains("N\u00e3o consegui ler", AppSettings.FolderNotice);
            var dialogs = window.OwnedWindows.Where(w => w.Title == "Pasta de dados").ToArray();
            log.WriteLine($"{(missing ? "FIXED" : "CONTROL")}-{mode}: LoadError=yes; GroupsAdded=0; FolderNotice=yes; dialogs={dialogs.Length}");
            Assert.Single(dialogs);
            held?.Dispose(); held = null;
            foreach (var dialog in dialogs) dialog.Close();
            window.Close(); // actual Closing -> Services.Save; original must still survive
            Assert.Contains("undo-original", File.ReadAllText(FileAt(portable) + ".corrompido"));
            Assert.Empty(AppSettings.Load().GroupsAdded);
            log.WriteLine("Closing saved defaults and preserved the original; recovery is manual (declared limit)");
        }
        finally
        {
            held?.Dispose();
            foreach (var dialog in window.OwnedWindows.ToArray()) dialog.Close();
            if (window.IsVisible) window.Close();
            Services.Current.ResetForTests();
            Notice(null);
        }
    }
}
'@
[IO.File]::WriteAllText((Join-Path $reviewDir 'Review.csproj'), $project.Replace('REPOROOT', $escapedRepo), [Text.Encoding]::UTF8)
[IO.File]::WriteAllText((Join-Path $reviewDir 'Review.cs'), $source, [Text.Encoding]::UTF8)
$names = @('INFOPC_DADOS', 'REVIEW_ROOT', 'TEMP', 'TMP', 'CONFERENCIA', 'INFOPC_TEST_REAL_PACKAGES', 'INFOPC_SHOTS')
$previous = @{}
foreach ($name in $names) { $previous[$name] = [Environment]::GetEnvironmentVariable($name) }
Push-Location $repo
try {
    $env:REVIEW_ROOT = Join-Path $reviewDir ('cases-' + [Guid]::NewGuid().ToString('N'))
    $env:INFOPC_DADOS = Join-Path $env:REVIEW_ROOT 'initial-data'
    $env:TEMP = Join-Path $env:REVIEW_ROOT 'temp'
    $env:TMP = $env:TEMP
    New-Item -ItemType Directory -Force -Path $env:INFOPC_DADOS,$env:TEMP | Out-Null
    $env:CONFERENCIA = ''
    $env:INFOPC_TEST_REAL_PACKAGES = ''
    $env:INFOPC_SHOTS = ''
    & dotnet test (Join-Path $reviewDir 'Review.csproj') -c Release --logger 'console;verbosity=detailed'
    if ($LASTEXITCODE -ne 0) { throw ('Reproducao falhou, codigo ' + $LASTEXITCODE) }
}
finally {
    foreach ($name in $names) { [Environment]::SetEnvironmentVariable($name, $previous[$name]) }
    Pop-Location
}
