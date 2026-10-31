using System.Diagnostics;

namespace InfoPC;

// Latência e banda da RAM: o mesmo código nas edições Windows e Linux (parte de MemoryBench), para as notas serem comparáveis.

public sealed record MemoryResult(double LatencyNs, double ReadGBs, double CopyGBs);

public static partial class MemoryBench
{
    private const long LatencyBytes = 256L << 20;     // 256 MB: bem maior que qualquer cache L3
    private const long BandwidthBytes = 1L << 30;     // 1 GB

    /// <summary>
    /// Latência: percorre uma lista encadeada em ordem aleatória (permutação de Sattolo = um único ciclo).
    /// Cada leitura depende da anterior, então o processador não consegue adiantar: mede o tempo de ida à RAM.
    /// </summary>
    public static double MeasureLatencyNs(CancellationToken ct)
    {
        int n = (int)(LatencyBytes / sizeof(long));
        var next = new long[n];
        // Saltos de 64 bytes (linhas de cache diferentes) em ordem aleatória determinística.
        int lines = n / 8;
        var order = new int[lines];
        for (int i = 0; i < lines; i++) order[i] = i;
        var rng = new Random(42);
        for (int i = lines - 1; i > 0; i--) { int j = rng.Next(i); (order[i], order[j]) = (order[j], order[i]); }
        for (int i = 0; i < lines; i++) next[order[i] * 8] = order[(i + 1) % lines] * 8;

        long p = 0;
        for (int i = 0; i < 200_000; i++) p = next[p]; // aquece TLB/páginas
        const int steps = 20_000_000;
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < steps; i++)
        {
            p = next[p];
            if ((i & 0xFFFFF) == 0 && ct.IsCancellationRequested) break;
        }
        double ns = sw.Elapsed.TotalMilliseconds * 1e6 / steps;
        GC.KeepAlive(p);
        return ns;
    }

    /// <summary>Banda de leitura (soma) e de cópia com todas as threads, em GB/s (melhor de 3).</summary>
    public static (double Read, double Copy) MeasureBandwidth(CancellationToken ct)
    {
        int threads = Environment.ProcessorCount;
        int n = (int)(BandwidthBytes / sizeof(long));
        var src = new long[n];
        var dst = new long[n];
        Parallel.For(0, threads, t => { int per = n / threads; Array.Fill(src, t + 1, t * per, per); });

        double bestRead = 0, bestCopy = 0;
        for (int rep = 0; rep < 3 && !ct.IsCancellationRequested; rep++)
        {
            var sw = Stopwatch.StartNew();
            long total = 0;
            Parallel.For(0, threads, () => 0L, (t, _, acc) =>
            {
                int per = n / threads;
                var span = new ReadOnlySpan<long>(src, t * per, per);
                long s0 = 0, s1 = 0, s2 = 0, s3 = 0;
                for (int i = 0; i + 3 < span.Length; i += 4) { s0 += span[i]; s1 += span[i + 1]; s2 += span[i + 2]; s3 += span[i + 3]; }
                return acc + s0 + s1 + s2 + s3;
            }, acc => Interlocked.Add(ref total, acc));
            bestRead = Math.Max(bestRead, BandwidthBytes / sw.Elapsed.TotalSeconds / 1e9);

            sw.Restart();
            Parallel.For(0, threads, t =>
            {
                int per = n / threads;
                Array.Copy(src, t * per, dst, t * per, per);
            });
            // Cópia lê e escreve: conta os dois sentidos, como os benchmarks de memória fazem.
            bestCopy = Math.Max(bestCopy, 2.0 * BandwidthBytes / sw.Elapsed.TotalSeconds / 1e9);
        }
        return (bestRead, bestCopy);
    }
}
