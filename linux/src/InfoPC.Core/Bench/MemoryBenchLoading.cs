using System.Diagnostics;
using System.Runtime.InteropServices;

namespace InfoPC;

public sealed record LoadResult(double SeqReadMBs, double SmallFilesMBs, double FilesPerSec, double MapLoadSeconds, string Drive, bool Direct);

/// <summary>
/// "Carregamento de mapa" (um pacote grande + mil arquivos pequenos) lido do disco sem o cache do sistema (O_DIRECT).
/// A parte de RAM (latência e banda) é a mesma da edição Windows e vem do arquivo compartilhado MemoryBenchRam.cs.
/// </summary>
public static partial class MemoryBench
{
    private const long PackBytes = 512L << 20;
    private const int AssetCount = 1000, AssetBytes = 64 << 10;

    public static LoadResult MeasureLoading(BenchTarget target, Action<string, double> progress, CancellationToken ct)
    {
        string dir = Path.Combine(target.Dir, "mapa");
        try
        {
            Directory.CreateDirectory(dir);
            string pack = Path.Combine(dir, "mapa.pak");

            // 1) Preparação (escrita): não entra na nota.
            var block = new byte[4 << 20];
            new Random(7).NextBytes(block); // dados incompressíveis
            double total = PackBytes + (double)AssetCount * AssetBytes;
            using (var fs = new FileStream(pack, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20, FileOptions.WriteThrough))
            {
                for (long w = 0; w < PackBytes && !ct.IsCancellationRequested; w += block.Length)
                {
                    fs.Write(block);
                    progress("Preparando arquivos de teste", w / total);
                }
                fs.Flush(true);
            }
            for (int i = 0; i < AssetCount && !ct.IsCancellationRequested; i++)
            {
                File.WriteAllBytes(Path.Combine(dir, $"asset{i:D4}.dat"), block.AsSpan(0, AssetBytes).ToArray());
                if (i % 50 == 0) progress("Preparando arquivos de teste", (PackBytes + (double)i * AssetBytes) / total);
            }
            ct.ThrowIfCancellationRequested();

            // 2) Leitura sequencial do pacote, sem cache (O_DIRECT exige buffer alinhado).
            return ReadAll(dir, pack, target, progress, ct);
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch { } }
    }

    private static unsafe LoadResult ReadAll(string dir, string pack, BenchTarget target, Action<string, double> progress, CancellationToken ct)
    {
        const int chunk = 4 << 20;
        void* mem = NativeMemory.AlignedAlloc(chunk, 4096);
        try
        {
            var buf = new Span<byte>(mem, chunk);
            bool direct = true;
            var sw = Stopwatch.StartNew();
            using (var h = DirectIo.Open(pack, write: false, create: false) ?? throw new IOException("Não consegui abrir o pacote de teste."))
            {
                direct = h.Direct;
                if (!direct) DirectIo.Evict(h);
                for (long off = 0; off < PackBytes && !ct.IsCancellationRequested; off += chunk)
                {
                    RandomAccess.Read(h.File, buf, off);
                    progress("Lendo pacote do mapa", off / (double)PackBytes);
                }
            }
            double seqMBs = PackBytes / 1048576.0 / sw.Elapsed.TotalSeconds;

            // 3) Muitos arquivos pequenos, 8 threads como as engines modernas. Antes, uma passada não medida (antivírus e cache frio).
            progress("Lendo assets pequenos (1ª passada, não conta)", 0);
            ReadAssets(dir, 8, ct);
            sw.Restart();
            progress("Lendo assets pequenos (8 threads)", 0.5);
            ReadAssets(dir, 8, ct);
            double secs = sw.Elapsed.TotalSeconds;
            ct.ThrowIfCancellationRequested();
            return new LoadResult(seqMBs, (double)AssetCount * AssetBytes / 1048576.0 / secs, AssetCount / secs,
                                  PackBytes / 1048576.0 / seqMBs + secs, target.MountPoint, direct);
        }
        finally { NativeMemory.AlignedFree(mem); }
    }

    private static unsafe void ReadAssets(string dir, int threads, CancellationToken ct)
    {
        Parallel.For(0, threads, new ParallelOptions { MaxDegreeOfParallelism = threads }, t =>
        {
            void* mem = NativeMemory.AlignedAlloc(AssetBytes, 4096);
            try
            {
                var buf = new Span<byte>(mem, AssetBytes);
                for (int i = t; i < AssetCount && !ct.IsCancellationRequested; i += threads)
                {
                    using var h = DirectIo.Open(Path.Combine(dir, $"asset{i:D4}.dat"), write: false, create: false);
                    if (h == null) continue;
                    RandomAccess.Read(h.File, buf, 0);
                }
            }
            finally { NativeMemory.AlignedFree(mem); }
        });
    }
}
