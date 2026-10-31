using System.Diagnostics;
using System.Runtime.InteropServices;

namespace InfoPC;

public sealed record DiskBenchResult(string Drive, long FileMb, double SeqReadMBs, double SeqWriteMBs,
                                     double ReadIopsQ1, double WriteIopsQ1, double ReadLatencyUs, double WriteLatencyUs,
                                     double ReadIopsQ8, bool Direct)
{
    /// <summary>Nota só para a barra do histórico: sequencial pesa em cópia/carregamento, 4K em abrir programas e sistema.</summary>
    public double Score => (SeqReadMBs + SeqWriteMBs) / 2 + (ReadIopsQ1 + WriteIopsQ1) / 200.0 + ReadIopsQ8 / 2000.0;
}

/// <summary>
/// Benchmark de armazenamento no estilo CrystalDiskMark: leitura e escrita sequenciais (blocos de 1 MB) e acesso aleatório de 4 KB
/// (1 e 8 requisições em paralelo). Usa O_DIRECT + O_DSYNC (o equivalente do NO_BUFFERING + WRITE_THROUGH do Windows) para medir o
/// disco e não o cache do sistema. O arquivo de teste fica numa pasta própria da unidade e é apagado no final.
/// Algoritmo da edição Windows (DiskBench.cs), adaptado para Linux.
/// </summary>
public static class DiskBench
{
    private const int Block = 1 << 20;     // 1 MB (sequencial)
    private const int Small = 4096;        // 4 KB (aleatório)
    private const double RandomSeconds = 4;

    public static unsafe DiskBenchResult Run(BenchTarget target, long fileMb, Action<string, double> progress, CancellationToken ct)
    {
        long fileBytes = fileMb << 20;
        Directory.CreateDirectory(target.Dir);
        long free = new DriveInfo(target.Dir).AvailableFreeSpace;
        if (free < fileBytes + (512L << 20))
            throw new IOException($"Pouco espaço livre em {target.MountPoint} (precisa de {fileMb + 512} MB).");

        string path = Path.Combine(target.Dir, "bench.tmp");
        void* seqMem = NativeMemory.AlignedAlloc((nuint)Block, 4096);
        DirectIo.Handle? h = null;
        try
        {
            var seq = new Span<byte>(seqMem, Block);
            new Random(42).NextBytes(seq); // dados incompressíveis (não deixam o controlador "trapacear")

            h = DirectIo.Open(path, write: true, create: true) ?? throw new IOException("Não consegui criar o arquivo de teste em " + target.Dir);
            var f = h.File;

            // ---------- Sequencial: escrita ----------
            var sw = Stopwatch.StartNew();
            for (long off = 0; off < fileBytes; off += Block)
            {
                ct.ThrowIfCancellationRequested();
                RandomAccess.Write(f, seq, off);
                progress("Escrita sequencial (1 MB)", 0.25 * off / fileBytes);
            }
            if (!h.Direct) DirectIo.Evict(h);
            double seqWrite = fileMb / sw.Elapsed.TotalSeconds;

            // ---------- Sequencial: leitura ----------
            if (!h.Direct) DirectIo.Evict(h);
            sw.Restart();
            for (long off = 0; off < fileBytes; off += Block)
            {
                ct.ThrowIfCancellationRequested();
                RandomAccess.Read(f, seq, off);
                progress("Leitura sequencial (1 MB)", 0.25 + 0.25 * off / fileBytes);
            }
            double seqRead = fileMb / sw.Elapsed.TotalSeconds;

            long slots = fileBytes / Small;
            var small = new Span<byte>(seqMem, Small);

            if (!h.Direct) DirectIo.Evict(h);
            double readIops = RandomLoop(h, small, slots, write: false, progress, "Leitura 4 KB aleatória (1 por vez)", 0.50, 0.15, ct, out double readLat);
            double writeIops = RandomLoop(h, small, slots, write: true, progress, "Escrita 4 KB aleatória (1 por vez)", 0.65, 0.15, ct, out double writeLat);

            if (!h.Direct) DirectIo.Evict(h);
            progress("Leitura 4 KB aleatória (8 em paralelo)", 0.80);
            double q8 = ParallelRead(f, slots, 8, ct, progress);

            progress("Concluído", 1.0);
            return new DiskBenchResult(target.MountPoint, fileMb, seqRead, seqWrite, readIops, writeIops, readLat, writeLat, q8, h.Direct);
        }
        finally
        {
            h?.Dispose();
            NativeMemory.AlignedFree(seqMem);
            try { File.Delete(path); } catch { }
            try { if (Path.GetFileName(target.Dir) == "InfoPCBench") Directory.Delete(target.Dir); } catch { }
        }
    }

    private static double RandomLoop(DirectIo.Handle h, Span<byte> buf, long slots, bool write, Action<string, double> progress, string phase,
                                     double start, double span, CancellationToken ct, out double latencyUs)
    {
        var rng = new Random(write ? 7 : 3);
        long ops = 0;
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed.TotalSeconds < RandomSeconds)
        {
            for (int i = 0; i < 64; i++) // checa o cancelamento a cada 64 operações (barato)
            {
                long off = rng.NextInt64(slots) * Small;
                if (write) RandomAccess.Write(h.File, buf, off); else RandomAccess.Read(h.File, buf, off);
                ops++;
            }
            ct.ThrowIfCancellationRequested();
            progress(phase, start + span * Math.Min(1, sw.Elapsed.TotalSeconds / RandomSeconds));
        }
        double secs = sw.Elapsed.TotalSeconds;
        latencyUs = secs / ops * 1e6;
        return ops / secs;
    }

    private static unsafe double ParallelRead(Microsoft.Win32.SafeHandles.SafeFileHandle f, long slots, int threads, CancellationToken ct, Action<string, double> progress)
    {
        long total = 0;
        var sw = Stopwatch.StartNew();
        var tasks = new Task[threads];
        for (int t = 0; t < threads; t++)
        {
            int seed = 100 + t;
            tasks[t] = Task.Factory.StartNew(() =>
            {
                void* mem = NativeMemory.AlignedAlloc((nuint)Small, 4096);
                try
                {
                    var buf = new Span<byte>(mem, Small);
                    var rng = new Random(seed);
                    long local = 0;
                    while (sw.Elapsed.TotalSeconds < RandomSeconds && !ct.IsCancellationRequested)
                        for (int i = 0; i < 32; i++) { RandomAccess.Read(f, buf, rng.NextInt64(slots) * Small); local++; }
                    Interlocked.Add(ref total, local);
                }
                finally { NativeMemory.AlignedFree(mem); }
            }, TaskCreationOptions.LongRunning);
        }
        while (!Task.WaitAll(tasks, 150))
            progress("Leitura 4 KB aleatória (8 em paralelo)", 0.80 + 0.19 * Math.Min(1, sw.Elapsed.TotalSeconds / RandomSeconds));
        ct.ThrowIfCancellationRequested();
        return total / sw.Elapsed.TotalSeconds;
    }
}
