using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace InfoPC;

/// <summary>
/// Arquivos abertos com O_DIRECT (o equivalente do FILE_FLAG_NO_BUFFERING do Windows): lê e escreve no disco de verdade, sem o cache de páginas.
/// Em sistemas de arquivos que não aceitam (tmpfs, overlayfs...) cai no modo comum e o chamador limpa o cache com <see cref="Evict"/>.
/// </summary>
public static class DirectIo
{
    [DllImport("libc", SetLastError = true, EntryPoint = "open")]
    private static extern int OpenFile([MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags, int mode);
    [DllImport("libc", SetLastError = true, EntryPoint = "posix_fadvise")] private static extern int Fadvise(int fd, long offset, long len, int advice);
    [DllImport("libc", SetLastError = true, EntryPoint = "fsync")] private static extern int Fsync(int fd);

    private const int O_RDONLY = 0, O_RDWR = 2, O_CREAT = 0x40, O_TRUNC = 0x200, O_DSYNC = 0x1000;
    private const int POSIX_FADV_DONTNEED = 4;

    // O_DIRECT muda por arquitetura: 0x4000 em x86, 0x10000 em ARM
    private static int ODirect => RuntimeInformation.ProcessArchitecture is Architecture.Arm64 or Architecture.Arm ? 0x10000 : 0x4000;

    public sealed class Handle : IDisposable
    {
        public SafeFileHandle File { get; }
        public bool Direct { get; }
        internal Handle(SafeFileHandle f, bool direct) { File = f; Direct = direct; }
        public void Dispose() => File.Dispose();
    }

    public static Handle? Open(string path, bool write, bool create)
    {
        if (!OperatingSystem.IsLinux()) return null;
        int baseFlags = (write ? O_RDWR | O_DSYNC : O_RDONLY) | (create ? O_CREAT | O_TRUNC : 0);
        int fd = OpenFile(path, baseFlags | ODirect, 0x180); // 0600
        bool direct = true;
        if (fd < 0) { fd = OpenFile(path, baseFlags, 0x180); direct = false; }
        return fd < 0 ? null : new Handle(new SafeFileHandle((IntPtr)fd, ownsHandle: true), direct);
    }

    /// <summary>Descarrega o arquivo para o disco e tira as páginas do cache (necessário quando o O_DIRECT não é aceito).</summary>
    public static void Evict(Handle h)
    {
        try
        {
            int fd = (int)h.File.DangerousGetHandle();
            Fsync(fd);
            Fadvise(fd, 0, 0, POSIX_FADV_DONTNEED);
        }
        catch { }
    }
}
