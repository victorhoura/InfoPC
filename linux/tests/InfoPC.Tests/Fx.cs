using System.Runtime.CompilerServices;
using Xunit;

// Os testes trocam o estado global (Sys.Root, Shell.Override): nada de paralelismo entre classes.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace InfoPC.Tests;

/// <summary>
/// Esconde as variáveis XDG_* do ambiente (o CI do GitHub define XDG_CONFIG_HOME, por exemplo): os testes usam o home falso e não podem
/// enxergar a configuração de quem os executa.
/// </summary>
public static class TestEnv
{
    private static readonly string[] Names = { "XDG_CONFIG_HOME", "XDG_DATA_HOME", "XDG_CACHE_HOME", "INFOPC_DADOS" };

    public static Dictionary<string, string?> Isolate()
    {
        var saved = new Dictionary<string, string?>();
        foreach (var n in Names) { saved[n] = Environment.GetEnvironmentVariable(n); Environment.SetEnvironmentVariable(n, null); }
        return saved;
    }

    public static void Restore(Dictionary<string, string?> saved)
    {
        foreach (var (n, v) in saved) Environment.SetEnvironmentVariable(n, v);
    }
}

/// <summary>Aponta o programa para uma das árvores de teste (Fixtures\&lt;nome&gt;) e devolve tudo ao normal no fim.</summary>
public sealed class Fx : IDisposable
{
    public string Root { get; }
    private readonly Dictionary<string, string?> _env = TestEnv.Isolate();

    public Fx(string name)
    {
        Root = System.IO.Path.Combine(AppContext.BaseDirectory, "Fixtures", name);
        Assert.True(Directory.Exists(Root), "fixture ausente: " + Root);
        Sys.Root = Root;
        Sys.Home = "/home/tester";
        Shell.Override = (_, _) => ProcResult.Missing("teste");   // nada de executar programas de verdade
        Elevate.Override = null;
        Locale.Init();                                              // pt-BR: acusa qualquer leitura numérica dependente de cultura
    }

    public static Fx Use(string name) => new(name);

    public void Dispose()
    {
        Sys.Root = "/";
        Sys.Home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Shell.Override = null;
        Elevate.Override = null;
        TestEnv.Restore(_env);
    }

    /// <summary>Links simbólicos exigem privilégio no Windows; onde não der, o teste de link é pulado (roda no CI Linux).</summary>
    public static bool SymlinksWork()
    {
        string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "infopc-symtest-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(dir);
            Directory.CreateSymbolicLink(System.IO.Path.Combine(dir, "l"), dir);
            return true;
        }
        catch { return false; }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }
}
