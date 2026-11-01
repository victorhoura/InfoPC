using InfoPC.Linux;
using Xunit;

namespace InfoPC.Tests;

public class CrashLogTests
{
    [Fact]
    public void Failures_are_recorded_locally_and_the_log_is_bounded()
    {
        using var fx = Fx.Use("intel-desktop");
        string old = AppSettings.Folder;
        string dir = Directory.CreateTempSubdirectory("infopc-log-").FullName;
        AppSettings.Folder = dir;
        try
        {
            CrashLog.Write("GPU", new InvalidOperationException("falha de leitura"));
            string file = Path.Combine(dir, "erros.log");
            Assert.Contains("falha de leitura", File.ReadAllText(file));
            Assert.Contains("InvalidOperationException", File.ReadAllText(file));
            for (int i = 0; i < 60; i++) CrashLog.Write("sensor", new Exception(new string('x', 20000)));
            Assert.True(new FileInfo(file).Length < 300000);
            Assert.True(new FileInfo(file + ".anterior").Length < 300000);
            CrashLog.Write("GPU", new Exception("última falha"));
            Assert.Contains("última falha", File.ReadAllText(file));
        }
        finally { AppSettings.Folder = old; Directory.Delete(dir, true); }
    }

    [Fact]
    public void Recovery_stops_when_the_failure_repeats_or_is_fatal()
    {
        CrashLog.ResetForTests();
        var ordinary = new IOException("uma ação falhou");
        for (int i = 0; i < CrashLog.BurstLimit; i++) Assert.True(CrashLog.CanContinue(ordinary, nowMs: 1000 + i), $"falha {i}");
        // erro em todo desenho/layout repetiria sem fim: fecha, com o registro já gravado, em vez de travar
        Assert.False(CrashLog.CanContinue(ordinary, nowMs: 1000 + CrashLog.BurstLimit));
        // passada a rajada, volta a tolerar falhas isoladas
        Assert.True(CrashLog.CanContinue(ordinary, nowMs: 1000 + CrashLog.BurstWindowMs + 100));
        Assert.False(CrashLog.CanContinue(new OutOfMemoryException(), nowMs: 500000));
        CrashLog.ResetForTests();
    }

    [Fact]
    public void An_unwritable_log_does_not_cause_another_crash()
    {
        using var fx = Fx.Use("intel-desktop");
        string old = AppSettings.Folder;
        string file = Path.GetTempFileName();
        AppSettings.Folder = file;
        try { CrashLog.Write("teste", new IOException("pasta indisponível")); }
        finally { AppSettings.Folder = old; File.Delete(file); }
    }
}
