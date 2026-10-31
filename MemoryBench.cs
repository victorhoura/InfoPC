using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace InfoPC;

public sealed record LoadResult(double SeqReadMBs, double SmallFilesMBs, double FilesPerSec, double MapLoadSeconds, string Drive);

/// <summary>
/// Testes do que pesa em jogos fora da CPU/GPU: latência e banda da RAM, e velocidade de "carregamento de mapa"
/// (um pacote grande + muitos arquivos pequenos), lendo do disco sem o cache do Windows.
/// </summary>
public static partial class MemoryBench
{
    private const long PackBytes = 512L << 20;        // "pacote" do mapa
    private const int AssetCount = 1000, AssetBytes = 64 << 10;
    private const int FILE_FLAG_NO_BUFFERING = 0x20000000;

    // ------------------------------------------------------------------ disco

    /// <summary>
    /// Cria os arquivos de teste numa pasta temporária, lê sem cache e apaga tudo no final.
    /// progress recebe (fase, fração).
    /// </summary>
    public static LoadResult MeasureLoading(Action<string, double> progress, CancellationToken ct)
    {
        string dir = Path.Combine(Path.GetTempPath(), "InfoPCBench");
        string drive = Path.GetPathRoot(dir) ?? "";
        try
        {
            Directory.CreateDirectory(dir);
            string pack = Path.Combine(dir, "mapa.pak");

            // 1) Preparação (escrita) — não entra na nota.
            var block = new byte[4 << 20];
            new Random(7).NextBytes(block); // dados incompressíveis
            using (var fs = new FileStream(pack, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20, FileOptions.WriteThrough))
                for (long w = 0; w < PackBytes && !ct.IsCancellationRequested; w += block.Length)
                {
                    fs.Write(block);
                    progress("Preparando arquivos de teste", w / (double)(PackBytes + (long)AssetCount * AssetBytes));
                }
            for (int i = 0; i < AssetCount && !ct.IsCancellationRequested; i++)
            {
                File.WriteAllBytes(Path.Combine(dir, $"asset{i:D4}.dat"), block.AsSpan(0, AssetBytes).ToArray());
                if (i % 50 == 0) progress("Preparando arquivos de teste", (PackBytes + (double)i * AssetBytes) / (PackBytes + (long)AssetCount * AssetBytes));
            }
            ct.ThrowIfCancellationRequested();

            // 2) Leitura sequencial do pacote, sem cache (FILE_FLAG_NO_BUFFERING exige buffer alinhado).
            const int chunk = 4 << 20;
            double seqMBs, smallMBs, filesPerSec;
            unsafe
            {
                void* mem = NativeMemory.AlignedAlloc(chunk, 4096);
                try
                {
                    var buf = new Span<byte>(mem, chunk);
                    var sw = Stopwatch.StartNew();
                    using (var h = File.OpenHandle(pack, FileMode.Open, FileAccess.Read, FileShare.Read, (FileOptions)FILE_FLAG_NO_BUFFERING))
                        for (long off = 0; off < PackBytes && !ct.IsCancellationRequested; off += chunk)
                        {
                            RandomAccess.Read(h, buf, off);
                            progress("Lendo pacote do mapa", off / (double)PackBytes);
                        }
                    seqMBs = PackBytes / 1048576.0 / sw.Elapsed.TotalSeconds;

                    // 3) Muitos arquivos pequenos (texturas, sons, scripts): abrir + ler + fechar, com 8 threads
                    //    como as engines modernas. Antes, uma passada não medida: o antivírus escaneia cada arquivo
                    //    novo na 1ª abertura (~30x mais lento), coisa que já aconteceu com os arquivos de um jogo instalado.
                    progress("Antivírus verificando os arquivos novos", 0);
                    ReadAssets(dir, 8, ct);
                    sw.Restart();
                    progress("Lendo assets pequenos (8 threads)", 0.5);
                    ReadAssets(dir, 8, ct);
                    double secs = sw.Elapsed.TotalSeconds;
                    double mapSeconds = PackBytes / 1048576.0 / seqMBs + secs;
                    smallMBs = (double)AssetCount * AssetBytes / 1048576.0 / secs;
                    filesPerSec = AssetCount / secs;
                    ct.ThrowIfCancellationRequested();
                    return new LoadResult(seqMBs, smallMBs, filesPerSec, mapSeconds, drive);
                }
                finally { NativeMemory.AlignedFree(mem); }
            }
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
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
                    using var h = File.OpenHandle(Path.Combine(dir, $"asset{i:D4}.dat"), FileMode.Open, FileAccess.Read,
                                                  FileShare.Read, (FileOptions)FILE_FLAG_NO_BUFFERING);
                    RandomAccess.Read(h, buf, 0);
                }
            }
            finally { NativeMemory.AlignedFree(mem); }
        });
    }
}
