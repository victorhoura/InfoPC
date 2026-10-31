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
    public void Pkexec_de_verdade_recusado_vira_cancelado()
    {
        if (Modo != "polkit-nao") return;
        var r = Elevate.Run("id", new[] { "-u" });
        Assert.False(r.Ok);
        Assert.True(r.Cancelled, $"esperava cancelado; erro: {r.Error}");
        Assert.Contains("senha", r.Message);
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
        Assert.True(Shell.Run("ubuntu-drivers", new[] { "devices" }, 60000).Ok || Shell.Has("ubuntu-drivers"));
        // a máquina do CI não tem GPU: nada a recomendar, e a chamada real não pode quebrar
        Assert.Null(DriverInfo.UbuntuRecommended());
    }
}
