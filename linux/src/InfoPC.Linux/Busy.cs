namespace InfoPC.Linux;

/// <summary>Só um teste pesado por vez (benchmark, stress, verificador): dois juntos falseariam os resultados e esquentariam o PC à toa.</summary>
public static class Busy
{
    public static string? Current { get; private set; }

    public static bool TryBegin(string what, out string message)
    {
        if (Current != null) { message = $"Pare o teste em andamento ({Current}) antes de começar outro."; return false; }
        Current = what; message = "";
        return true;
    }

    public static void End(string what) { if (Current == what) Current = null; }
}
