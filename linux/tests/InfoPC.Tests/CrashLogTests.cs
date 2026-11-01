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
