# Dados sinteticos contra o assembly compilado do produto. Sem abrir o app,
# construir Computer, Open, Refresh, registro, driver ou chamada nativa de GPU.
$ErrorActionPreference = 'Stop'
$reviewRepo = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$reviewDir = Join-Path $reviewRepo 'obj/review-sensores-117'
$reviewDll = Join-Path $reviewRepo 'bin/Release/net8.0-windows/InfoPC.dll'
if (-not (Test-Path -LiteralPath $reviewDll)) { throw 'Build InfoPC Release first.' }
New-Item -ItemType Directory -Force -Path $reviewDir | Out-Null
@'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0-windows</TargetFramework>
    <UseWPF>true</UseWPF>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="../../InfoPC.csproj" />
  </ItemGroup>
</Project>
'@ | Set-Content -Encoding UTF8 -LiteralPath (Join-Path $reviewDir 'Review.csproj')
@'
using System.Collections.ObjectModel;
using System.Reflection;
using System.Runtime.CompilerServices;
using InfoPC;
using LibreHardwareMonitor.Hardware;

static class Program
{
    static int checks;
    static void Check(bool ok, string message)
    {
        if (!ok) throw new Exception(message);
        checks++;
    }

    // Pula o construtor por completo: nenhum objeto Computer e criado.
    static SensorMonitor Monitor(params SensorRow[] rows)
    {
        var m = (SensorMonitor)RuntimeHelpers.GetUninitializedObject(typeof(SensorMonitor));
        typeof(SensorMonitor).GetField("<Rows>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(m, new ObservableCollection<SensorRow>(rows));
        return m;
    }

    static SensorRow Row(string name, float? value, SensorType type = SensorType.Clock,
                         HardwareType hardware = HardwareType.Cpu)
    {
        var row = new SensorRow { Name = name, Type = type, HardwareType = hardware };
        row.Update(value, value, value);
        return row;
    }

    static LoadSample Sample(SensorMonitor m) => BottleneckAnalyzer.Sample(m, null, null);

    static void Main()
    {
        var empty = Monitor();
        Check(empty.CpuCoreClocks().Count == 0, "Empty clocks");
        var e = Sample(empty);
        Check(e.CpuMaxCore == null && e.BusiestCore == null && e.RamPct == null, "Empty sample");

        var intel = Monitor(Row("CPU Core #1", 4000), Row("CPU Core #2", 4200),
            Row("Bus Speed", 100), Row("GPU Core", 2000, hardware: HardwareType.GpuNvidia));
        Check(intel.CpuCoreClocks().SequenceEqual(new float[] { 4000, 4200 }), "Intel names");
        Check(intel.CpuCoreClocks().Average() == 4100 && intel.CpuCoreClocks().Max() == 4200, "Intel mean/max");

        var amd = Monitor(Row("Core #1", 3500), Row("Core #2", 4500),
            Row("Core #1 (Effective)", 1000), Row("Core #2 (Effective)", 1500),
            Row("Cores (Average)", 4000), Row("Cores (Average Effective)", 1250), Row("Bus Speed", 100));
        Check(amd.CpuCoreClocks().SequenceEqual(new float[] { 3500, 4500 }), "AMD names and effective exclusion");
        Check(amd.CpuCoreClocks().Average() == 4000 && amd.CpuCoreClocks().Max() == 4500, "AMD mean/max");

        var bad = Monitor(Row("CPU Core #1", null), Row("Core #2", 0), Row("Core #3", -1),
            Row("CPU Core #4", 65, SensorType.Temperature));
        Check(bad.CpuCoreClocks().Count == 0, "Missing/zero/wrong type excluded");

        var smt = Monitor(Row("CPU Total", 30, SensorType.Load),
            Row("CPU Core #1 Thread #1", 20, SensorType.Load),
            Row("CPU Core #1 Thread #2", 93, SensorType.Load),
            Row("CPU Core #2 Thread #1", null, SensorType.Load),
            Row("CPU Core #2 Thread #2", 45, SensorType.Load));
        var s = Sample(smt);
        Check(s.CpuMaxCore == 93 && s.BusiestCore == "n\u00facleo 1", "SMT maximum and label");
        Check(s.Classify() == Limiter.Cpu, "Busy SMT thread identifies CPU limit");

        var plain = Monitor(Row("CPU Total", 10, SensorType.Load),
            Row("CPU Core #1", 15, SensorType.Load), Row("CPU Core #2", 40, SensorType.Load));
        var p = Sample(plain);
        Check(p.CpuMaxCore == 40 && p.BusiestCore == "n\u00facleo 2", "Non-SMT preserved");
        Check(p.Classify() == Limiter.Capped && p.RamPct == null, "Before RAM arrival");
        plain.Rows.Add(Row("Memory", 95, SensorType.Load, HardwareType.Memory));
        Check(Sample(plain).RamPct == 95 && Sample(plain).Classify() == Limiter.Ram, "After RAM arrival");
        plain.Rows.Add(Row("CPU Package", 96, SensorType.Temperature));
        Check(Sample(plain).Classify() == Limiter.Thermal, "Temperature keeps priority");
        Console.WriteLine($"PASS: {checks} pure-data checks; no hardware opened.");
    }
}
'@ | Set-Content -Encoding UTF8 -LiteralPath (Join-Path $reviewDir 'Program.cs')
dotnet run --project (Join-Path $reviewDir 'Review.csproj') -c Release
if ($LASTEXITCODE -ne 0) { throw "Review harness failed: $LASTEXITCODE" }
