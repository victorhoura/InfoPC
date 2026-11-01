using System.Runtime.InteropServices;

namespace InfoPC;

/// <summary>Gera carga sintética de CPU (todos os threads) e/ou RAM (escrita + verificação). Algoritmo da edição Windows, adaptado para Linux.</summary>
public sealed class StressTester
{
    private const int ChunkLongs = 16 * 1024 * 1024; // 128 MB por bloco
    private const long ChunkBytes = ChunkLongs * 8L;

    private CancellationTokenSource? _cts;
    private readonly List<Thread> _threads = new();
    private long _memErrors;
    private long _memPasses;
    private volatile bool _memWorker;

    public bool Running => _cts != null;
    public long MemoryErrors => Interlocked.Read(ref _memErrors);
    public long MemoryPasses => Interlocked.Read(ref _memPasses);
    public long MemoryAllocatedBytes { get; private set; }

    /// <summary>O teste de RAM ainda está em execução; deixa de estar se precisou devolver toda a memória ao sistema.</summary>
    public bool MemoryWorkerRunning => _memWorker;

    /// <summary>Texto para o usuário quando o teste precisou reduzir a memória usada; nulo enquanto não precisou.</summary>
    public string? MemoryNotice { get; private set; }

    internal Func<long>? AvailableBytesForTests { get; set; }
    internal Func<long>? TotalBytesForTests { get; set; }
    internal Action? OnChunkReleasedForTests { get; set; }

    /// <param name="cpu">Carga em todos os processadores lógicos.</param>
    /// <param name="ram">Aloca uma fração da RAM disponível e grava/verifica padrões continuamente.</param>
    /// <param name="ramFraction">Fração da memória disponível a usar (0,1 a 0,8: o Linux mata processos se a RAM acabar).</param>
    public void Start(bool cpu, bool ram, double ramFraction = 0.6)
    {
        if (Running) return;
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        Interlocked.Exchange(ref _memErrors, 0);
        Interlocked.Exchange(ref _memPasses, 0);
        MemoryAllocatedBytes = 0;
        MemoryNotice = null;

        int cpuThreads = Environment.ProcessorCount;
        if (cpu && ram && cpuThreads > 1) cpuThreads--; // reserva um thread para o worker de memória

        if (cpu)
            for (int i = 0; i < cpuThreads; i++)
            {
                int seed = i;
                StartThread(() => CpuWorker(token, seed), $"Stress CPU {seed}");
            }

        if (ram)
        {
            long bytes = (long)(AvailableBytes() * Math.Clamp(ramFraction, 0.1, 0.8));
            MemoryAllocatedBytes = bytes;
            _memWorker = true;
            StartThread(() => RamWorker(token, bytes), "Stress RAM");
        }
    }

    public void Stop()
    {
        if (_cts == null) return;
        _cts.Cancel();
        foreach (var t in _threads) t.Join(2000);
        _threads.Clear();
        _cts.Dispose();
        _cts = null;
        GC.Collect();
    }

    private void StartThread(ThreadStart body, string name)
    {
        var t = new Thread(() => { LowerCurrentThread(); body(); }) { IsBackground = true, Name = name, Priority = ThreadPriority.BelowNormal };
        _threads.Add(t);
        t.Start();
    }

    [DllImport("libc", SetLastError = true)] private static extern int setpriority(int which, uint who, int prio);

    /// <summary>
    /// No Linux o .NET ignora Thread.Priority: sem isto, a carga disputa os núcleos em pé de igualdade com a janela, o servidor gráfico e o compositor.
    /// Com nice 10 os núcleos continuam a 100%, mas a área de trabalho é atendida primeiro. "who = 0" vale só para a thread que chama.
    /// </summary>
    internal static void LowerCurrentThread()
    {
        if (!OperatingSystem.IsLinux()) return;
        try { setpriority(0, 0, 10); }
        catch (DllNotFoundException) { }
        catch (EntryPointNotFoundException) { }
    }

    private static void CpuWorker(CancellationToken token, int seed)
    {
        // Mistura ponto flutuante (FMA/sqrt/sin) e inteiros sobre um buffer que cabe no cache L1/L2.
        var buf = new double[4096];
        for (int i = 0; i < buf.Length; i++) buf[i] = i + seed + 1;
        ulong x = (ulong)(seed + 1) * 0x9E3779B97F4A7C15UL;

        while (!token.IsCancellationRequested)
        {
            for (int rep = 0; rep < 64; rep++)
            {
                for (int i = 0; i < buf.Length; i++)
                {
                    double v = buf[i];
                    v = Math.FusedMultiplyAdd(v, 1.0000001, 0.5);
                    v = Math.Sqrt(v * v + 1.0) + Math.Sin(v) * 1e-3;
                    buf[i] = v > 1e12 ? i + 1 : v;

                    x ^= x << 13; x ^= x >> 7; x ^= x << 17;
                }
            }
            if (x == 0) buf[0] = 0; // evita que o JIT elimine o cálculo inteiro
        }
    }

    private unsafe void RamWorker(CancellationToken token, long totalBytes)
    {
        // Memória nativa: free devolve o bloco ao sistema na hora, sem depender de uma coleta do GC (que pausaria a janela justamente com a RAM acabando).
        var chunks = new List<nint>();
        try
        {
            long allocated = 0;
            try
            {
                while (allocated + ChunkBytes <= totalBytes && !token.IsCancellationRequested)
                {
                    chunks.Add((nint)NativeMemory.Alloc((nuint)ChunkBytes));
                    allocated += ChunkBytes;
                }
            }
            catch (OutOfMemoryException) { /* segue com o que conseguiu alocar */ }
            MemoryAllocatedBytes = allocated;
            if (chunks.Count == 0) return;

            long reserve = ReserveBytes(TotalBytes());
            ulong pass = 0;
            while (!token.IsCancellationRequested)
            {
                pass++;
                long pattern = unchecked((long)(pass * 0x9E3779B97F4A7C15UL));
                for (int n = 0; n < chunks.Count; n++)
                {
                    if (!KeepHeadroom(chunks, reserve)) return;
                    if (n >= chunks.Count) break;
                    var c = (long*)chunks[n];
                    for (int i = 0; i < ChunkLongs; i++) c[i] = pattern ^ i;
                    if (token.IsCancellationRequested) return;
                }
                for (int n = 0; n < chunks.Count; n++)
                {
                    if (!KeepHeadroom(chunks, reserve)) return;
                    if (n >= chunks.Count) break;
                    var c = (long*)chunks[n];
                    long errs = 0;
                    for (int i = 0; i < ChunkLongs; i++)
                        if (c[i] != (pattern ^ i)) errs++;
                    if (errs > 0) Interlocked.Add(ref _memErrors, errs);
                    if (token.IsCancellationRequested) return;
                }
                Interlocked.Increment(ref _memPasses);
            }
        }
        finally
        {
            foreach (var c in chunks) NativeMemory.Free((void*)c);
            chunks.Clear();
            _memWorker = false;
        }
    }

    /// <summary>
    /// A página só passa a ocupar RAM na primeira escrita, e outros programas podem crescer durante o teste. Sem folga, o sistema entra em swap (a tela congela)
    /// e o OOM killer escolhe o maior processo: o próprio InfoPC, que some sem deixar erro. Devolve memória antes disso. False: não sobrou nada para testar.
    /// </summary>
    private unsafe bool KeepHeadroom(List<nint> chunks, long reserve)
    {
        if (AvailableBytes() >= reserve) return true;
        while (chunks.Count > 0 && AvailableBytes() < reserve)
        {
            NativeMemory.Free((void*)chunks[^1]);
            chunks.RemoveAt(chunks.Count - 1);
            MemoryAllocatedBytes = chunks.Count * ChunkBytes;
            OnChunkReleasedForTests?.Invoke();
        }
        MemoryNotice = chunks.Count > 0
            ? $"O sistema ficou com pouca memória livre: o teste de RAM devolveu parte da memória e segue com {MemoryAllocatedBytes / 1048576:N0} MB."
            : "O sistema ficou com pouca memória livre: o teste de RAM devolveu toda a memória e parou, para não travar o PC.";
        return chunks.Count > 0;
    }

    /// <summary>Quanto de memória precisa continuar disponível para o sistema durante o teste: 5% da RAM, no mínimo 512 MB.</summary>
    internal static long ReserveBytes(long totalBytes) => Math.Max(512L << 20, totalBytes / 20);

    /// <summary>Memória realmente disponível (livre + cache recuperável), em bytes.</summary>
    private long AvailableBytes() => AvailableBytesForTests?.Invoke() ?? (MemInfo.Read() is { } m ? m.AvailableKb * 1024 : 1L << 30);

    private long TotalBytes() => TotalBytesForTests?.Invoke() ?? (MemInfo.Read() is { } m ? m.TotalKb * 1024 : 0);
}
