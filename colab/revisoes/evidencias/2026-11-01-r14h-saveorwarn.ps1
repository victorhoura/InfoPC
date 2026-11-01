# Revisao R14-H / SaveOrWarn. Sem InfoPC.exe, WPF, sensores reais ou comandos de sistema.
# Linux real em Avalonia headless com fixtures; SaveOrWarn extraido sem alterar seu corpo.
# Dispatcher e MessageBox do Windows sao dubles; persistencia e a do Core.
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..'))
$reviewDir = Join-Path $repo 'obj\review\r14h-saveorwarn'
New-Item -ItemType Directory -Force $reviewDir | Out-Null
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
$windows = [IO.File]::ReadAllText((Join-Path $repo 'MainWindow.xaml.cs'))
$start = $windows.IndexOf('    private void SaveOrWarn()')
$end = $windows.IndexOf('    private void BtnComDbRestore_Click', $start)
if ($start -lt 0 -or $end -le $start) { throw 'SaveOrWarn boundaries not found' }
$method = $windows.Substring($start, $end - $start)
$shim = @'
using InfoPC;
public sealed class SaveHost
{
    private readonly AppSettings _settings;
    public SaveHost(AppSettings settings) => _settings = settings;
    public FakeDispatcher Dispatcher { get; } = new();
    public void Save() => SaveOrWarn();
METHOD
}
public sealed class FakeDispatcher
{
    public Queue<Action> Pending { get; } = new();
    public void BeginInvoke(Action action) => Pending.Enqueue(action);
    public void Drain() { while (Pending.TryDequeue(out var action)) action(); }
}
public enum MessageBoxButton { OK }
public enum MessageBoxImage { Warning }
public static class MessageBox
{
    public static List<(object Owner, string Text, string Title)> Shown { get; } = new();
    public static void Show(object owner, string text, string title, MessageBoxButton button, MessageBoxImage image)
        => Shown.Add((owner, text, title));
}
'@
$source = @'
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.Threading;
using InfoPC;
using InfoPC.Linux;
using InfoPC.Tests;
using Xunit;
using Xunit.Abstractions;

public sealed class ReviewR14h
{
    private readonly ITestOutputHelper log;
    public ReviewR14h(ITestOutputHelper log) => this.log = log;
    private static string NewCase() => Directory.CreateDirectory(Path.Combine(
        Environment.GetEnvironmentVariable("REVIEW_ROOT")!, Guid.NewGuid().ToString("N"))).FullName;
    private static void ClearNotice() => typeof(AppSettings).GetProperty(nameof(AppSettings.FolderNotice))!.SetValue(null, null);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SaveOrWarn_keeps_state_warns_and_retries_after_unlock(bool unreadOnLoad)
    {
        AppSettings.Folder = NewCase();
        new AppSettings { GroupsAdded = { "undo-original" } }.Save();
        string file = Path.Combine(AppSettings.Folder, "settings.json");
        string original = File.ReadAllText(file);
        AppSettings? state = unreadOnLoad ? null : AppSettings.Load();
        SaveHost host;
        MessageBox.Shown.Clear();
        using (new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            state ??= AppSettings.Load();
            state.OverlayCorner = "BR";
            state.CustomGames.Add(new CustomGame { Name = "manual", Exe = "fake-game" });
            state.BenchRecords.Add(new BenchRecord(DateTime.Now, "gpu", 123, "fake-result"));
            host = new SaveHost(state);
            for (int i = 0; i < 3; i++) host.Save();
            Assert.Equal(3, host.Dispatcher.Pending.Count);
            Assert.Empty(MessageBox.Shown);
            host.Dispatcher.Drain();
            Assert.Equal(3, MessageBox.Shown.Count);
            Assert.All(MessageBox.Shown, warning => {
                Assert.Same(host, warning.Owner);
                Assert.Equal("InfoPC", warning.Title);
                Assert.Contains("vale at\u00e9 fechar", warning.Text);
            });
        }
        Assert.Equal(original, File.ReadAllText(file));
        host.Save();
        Assert.Empty(host.Dispatcher.Pending);
        Assert.Equal(3, MessageBox.Shown.Count);
        var loaded = AppSettings.Load();
        Assert.Equal("BR", loaded.OverlayCorner);
        Assert.Single(loaded.CustomGames);
        Assert.Single(loaded.BenchRecords);
        if (unreadOnLoad) Assert.Equal(original, File.ReadAllText(file + ".corrompido"));
        else Assert.Contains("undo-original", loaded.GroupsAdded);
        log.WriteLine($"unreadOnLoad={unreadOnLoad}: 3 caught failures, 3 queued warnings, original intact, retry saved all preferences/history");
        ClearNotice();
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cold_window_applies_persisted_theme_and_notifies_ready_once(bool light)
    {
        using var fx = Fx.Use("intel-desktop");
        AppSettings.Folder = NewCase();
        ClearNotice();
        new AppSettings { LightTheme = light, OverlayCorner = "BR", DeviceToasts = false,
            BenchRecords = { new BenchRecord(DateTime.Now, "gpu", 456, "saved-result") } }.Save();
        var s = Services.Current;
        s.ResetForTests();
        int notifications = 0;
        s.Loaded += () => {
            notifications++;
            Assert.True(s.Ready);
            Assert.Equal(light, s.Settings.LightTheme);
            Assert.Equal(light ? ThemeVariant.Light : ThemeVariant.Dark, Application.Current!.RequestedThemeVariant);
        };
        var window = new MainWindow();
        try
        {
            window.Show();
            await s.WhenReady.WaitAsync(TimeSpan.FromSeconds(15));
            Dispatcher.UIThread.RunJobs();
            var state = s.Settings;
            Assert.Equal("BR", state.OverlayCorner);
            Assert.False(state.DeviceToasts);
            Assert.Single(state.BenchRecords);
            Assert.Empty(window.OwnedWindows);
            window.Navigate("sensors");
            s.TickNow();
            Assert.NotEmpty(s.Hub.Rows);
            s.LoadSettings();
            await s.StartAsync();
            Assert.Same(state, s.Settings);
            Assert.Equal(1, notifications);
            log.WriteLine($"cold-window light={light}: persisted preferences, theme, sensors, Ready and single Loaded verified");
        }
        finally { window.Close(); s.ResetForTests(); Services.ApplyTheme(false); ClearNotice(); }
    }

    [AvaloniaFact]
    public async Task Preload_is_idempotent_and_StartAsync_keeps_unsaved_memory()
    {
        using var fx = Fx.Use("intel-desktop");
        AppSettings.Folder = NewCase();
        ClearNotice();
        new AppSettings { LightTheme = true, OverlayCorner = "BR" }.Save();
        var s = Services.Current;
        s.ResetForTests();
        try
        {
            s.LoadSettings();
            var state = s.Settings;
            Assert.False(s.Ready);
            Assert.Equal(ThemeVariant.Light, Application.Current!.RequestedThemeVariant);
            state.OverlayCorner = "BL";
            s.LoadSettings();
            Assert.Same(state, s.Settings);
            await s.StartAsync();
            Assert.True(s.Ready);
            Assert.Same(state, s.Settings);
            Assert.Equal("BL", s.Settings.OverlayCorner);
            Assert.Equal("BR", AppSettings.Load().OverlayCorner);
            log.WriteLine("preload: Settings object and unsaved change survived StartAsync; no extra disk write");
        }
        finally { s.ResetForTests(); Services.ApplyTheme(false); ClearNotice(); }
    }

    [AvaloniaFact]
    public async Task StartAsync_without_preload_still_loads_and_applies_theme()
    {
        using var fx = Fx.Use("intel-desktop");
        AppSettings.Folder = NewCase();
        ClearNotice();
        new AppSettings { LightTheme = true, OverlayScale = 1.5 }.Save();
        var s = Services.Current;
        s.ResetForTests();
        try
        {
            await s.StartAsync();
            Assert.True(s.Ready);
            Assert.True(s.Settings.LightTheme);
            Assert.Equal(1.5, s.Settings.OverlayScale);
            Assert.Equal(ThemeVariant.Light, Application.Current!.RequestedThemeVariant);
            log.WriteLine("StartAsync alone: settings, theme and hardware readiness verified");
        }
        finally { s.ResetForTests(); Services.ApplyTheme(false); ClearNotice(); }
    }
}
'@
[IO.File]::WriteAllText((Join-Path $reviewDir 'Review.csproj'), $project.Replace('REPOROOT', $escapedRepo))
[IO.File]::WriteAllText((Join-Path $reviewDir 'SaveHost.cs'), $shim.Replace('METHOD', $method))
[IO.File]::WriteAllText((Join-Path $reviewDir 'Review.cs'), $source)
$names = @('INFOPC_DADOS', 'REVIEW_ROOT', 'TEMP', 'TMP', 'CONFERENCIA', 'INFOPC_TEST_REAL_PACKAGES', 'INFOPC_SHOTS')
$previous = @{}
foreach ($name in $names) { $previous[$name] = [Environment]::GetEnvironmentVariable($name) }
Push-Location $repo
try {
    $env:REVIEW_ROOT = Join-Path $reviewDir ('cases-' + [Guid]::NewGuid().ToString('N'))
    $env:INFOPC_DADOS = Join-Path $env:REVIEW_ROOT 'initial-data'
    $env:TEMP = Join-Path $env:REVIEW_ROOT 'temp'
    $env:TMP = $env:TEMP
    New-Item -ItemType Directory -Force $env:INFOPC_DADOS,$env:TEMP | Out-Null
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
