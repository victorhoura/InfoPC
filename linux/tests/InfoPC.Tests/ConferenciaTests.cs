using Xunit;

namespace InfoPC.Tests;

/// <summary>
/// Ações reais de root e de sistema, sem simulação. Só valem no workflow Conferência, que prepara o ambiente
/// (polkit, hidepid, pacotes) e define CONFERENCIA: fora dele, cada teste retorna sem fazer nada.
/// </summary>
[Trait("Categoria", "Conferencia")]
public class ConferenciaTests
{
    private static string Modo => Environment.GetEnvironmentVariable("CONFERENCIA") ?? "";

    [Fact]
    public void Pkexec_de_verdade_roda_como_root()
    {
        if (Modo != "polkit-sim") return;
        var r = Elevate.Run("id", new[] { "-u" });
        Assert.True(r.Ok, r.Error);
        Assert.Equal("0", r.Output.Trim());
    }

    [Fact]
    public void Pkexec_de_verdade_recusado_mostra_o_comando_manual()
    {
        if (Modo != "polkit-nao") return;
        var r = Elevate.Run("id", new[] { "-u" });
        Assert.False(r.Ok);
        // recusa do polkit é 127 no pkexec (126 é só fechar a janela de senha, o que exige um agente de verdade)
        Assert.True(r.Unavailable, $"esperava sem autorização; erro: {r.Error}");
        Assert.Contains("Sem autorização", r.Message);
        Assert.Contains("sudo ", r.Message);
    }

    [Fact]
    public void Instalar_pelo_pkexec_e_desfazer_remove_o_pacote()
    {
        if (Modo != "polkit-sim") return;
        var s = new AppSettings();
        var (r, warning) = Packages.Track("teste", "cowsay", "apt", s, () => { },
            () => Elevate.Run("apt-get", new[] { "install", "-y", "cowsay" }, 600000));
        Assert.True(r.Ok, r.Error);
        Assert.Null(warning);
        var change = Assert.Single(s.PackageChanges);
        Assert.Contains(change.Added, p => p.Name == "cowsay");
        Assert.True(Shell.Run("dpkg", new[] { "-s", "cowsay" }, 20000).Ok);

        var undo = Packages.Undo(change, s, () => { });
        Assert.True(undo.Ok, undo.Error);
        Assert.Empty(s.PackageChanges);
        Assert.False(Shell.Run("dpkg-query", new[] { "-W", "-f=${db:Status-Status}", "cowsay" }, 20000).Out.Trim() == "installed");
    }

    [Fact]
    public void Pen_drive_de_verdade_leva_e_traz_o_desfazer_com_a_data_arredondada()
    {
        if (Modo != "pendrive") return;
        string pen = Environment.GetEnvironmentVariable("CONFERENCIA_PEN")!;
        string fallback = Directory.CreateTempSubdirectory("infopc-fallback").FullName;
        const string me = "0123456789abcdef";
        string before = AppSettings.Folder;
        void Remount(string mode) => Assert.True(Shell.Run("sudo", new[] { "mount", "-o", "remount," + mode, Environment.GetEnvironmentVariable("CONFERENCIA_MONTAGEM")! }, 30000).Ok);
        List<string> Groups(string dir) { AppSettings.Folder = dir; return AppSettings.Load().GroupsAdded; }
        void Add(string dir, string group) { AppSettings.Folder = dir; var s = AppSettings.Load(); s.GroupsAdded.Add(group); s.Save(); }
        try
        {
            string first = DataFolder.Resolve(pen, me, fallback, out _);
            Assert.StartsWith(pen, first);
            Add(first, "um");

            Remount("ro");
            string away = DataFolder.Resolve(pen, me, fallback, out var notice);
            Assert.Equal(fallback, away);
            Assert.NotNull(notice);
            Assert.Equal(new[] { "um" }, Groups(away));
            Add(away, "dois");
            Remount("rw");

            string back = DataFolder.Resolve(pen, me, fallback, out notice);
            Assert.Equal(first, back);
            Assert.Equal(new[] { "um", "dois" }, Groups(back));
            Add(back, "tres");

            // abrir de novo não troca nada, com a data arredondada pelo sistema de arquivos
            Assert.Equal(first, DataFolder.Resolve(pen, me, fallback, out notice));
            Assert.Null(notice);
            Assert.Equal(new[] { "um", "dois", "tres" }, Groups(first));
        }
        finally { AppSettings.Folder = before; Directory.Delete(fallback, true); }
    }

    [Fact]
    public void Com_hidepid_o_programa_sabe_que_nao_ve_os_instaladores()
    {
        if (Modo != "hidepid") return;
        Assert.Null(Sys.Read("/proc/1/comm"));
        Assert.Equal(InstallerState.Unknown, Packages.InstallerActivity());
    }

    [Fact]
    public void Sem_hidepid_nenhum_instalador_rodando()
    {
        if (Modo != "polkit-sim") return;
        Assert.Equal(InstallerState.Idle, Packages.InstallerActivity());
    }

    [Fact]
    public void Ubuntu_drivers_de_verdade_responde_sem_placa_recomendada()
    {
        if (Modo != "polkit-sim") return;
        Assert.True(Shell.Has("ubuntu-drivers"));
        var r = Shell.Run("ubuntu-drivers", new[] { "devices" }, 60000);
        Assert.False(r.TimedOut, "ubuntu-drivers esgotou o tempo");
        // a máquina do CI não tem GPU: o comando funciona e não tem nada a recomendar
        Assert.True(r.Ok, $"ubuntu-drivers devices falhou, código {r.ExitCode}: {r.Err}");
        Assert.DoesNotContain("recommended", r.Out);
        Assert.Null(DriverInfo.UbuntuRecommended());
    }
}
