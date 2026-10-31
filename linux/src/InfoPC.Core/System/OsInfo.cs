using System.Runtime.InteropServices;

namespace InfoPC;

public sealed record OsDetails(
    string Name, string Id, string IdLike, string VersionId, string Kernel, string KernelBuild, string Arch, string Hostname,
    TimeSpan Uptime, string? Desktop, string? Session, string? Init, string BootMode, bool? SecureBoot,
    string? Virtualization, string? PackageManager, string Libc);

/// <summary>Sistema operacional: distribuição, kernel, sessão gráfica, firmware e virtualização.</summary>
public static class OsInfo
{
    public static OsDetails Read()
    {
        var rel = Sys.KeyValues("/etc/os-release", '=');
        if (rel.Count == 0) rel = Sys.KeyValues("/usr/lib/os-release", '=');
        string G(string k) => rel.TryGetValue(k, out var v) ? v : "";

        string name = G("PRETTY_NAME");
        if (name.Length == 0) name = G("NAME").Length > 0 ? G("NAME") : "Linux";

        string kernel = Sys.Read("/proc/sys/kernel/osrelease") ?? "—";
        string build = Sys.Read("/proc/sys/kernel/version") ?? "";
        double up = Num.D((Sys.Read("/proc/uptime") ?? "").Split(' ')[0]) ?? 0;
        string host = Sys.Read("/proc/sys/kernel/hostname") ?? Environment.MachineName;

        string arch = RuntimeInformation.OSArchitecture switch
        {
            Architecture.X64 => "x86_64", Architecture.Arm64 => "aarch64", Architecture.X86 => "i686",
            Architecture.Arm => "armv7l", var a => a.ToString().ToLowerInvariant()
        };

        string? init = null;
        if (Sys.DirExists("/run/systemd/system")) init = "systemd";
        else if (Sys.FileExists("/sbin/openrc")) init = "OpenRC";
        else if (Sys.Read("/proc/1/comm") is { Length: > 0 } c) init = c;

        return new OsDetails(
            name, G("ID"), G("ID_LIKE"), G("VERSION_ID"), kernel, build, arch, host, TimeSpan.FromSeconds(up),
            Env("XDG_CURRENT_DESKTOP"), SessionType(), init,
            Sys.DirExists("/sys/firmware/efi") ? "UEFI" : "BIOS (legado)", SecureBoot(),
            Virtualization(kernel), PackageManager(), Libc());
    }

    private static string? Env(string k) => Environment.GetEnvironmentVariable(k) is { Length: > 0 } v ? v : null;

    private static string? SessionType()
    {
        string? t = Env("XDG_SESSION_TYPE");
        if (t != null) return t switch { "wayland" => "Wayland", "x11" => "X11", var o => o };
        if (Env("WAYLAND_DISPLAY") != null) return "Wayland";
        if (Env("DISPLAY") != null) return "X11";
        return null;
    }

    public static bool? SecureBoot()
    {
        try
        {
            string dir = Sys.P("/sys/firmware/efi/efivars");
            if (!Directory.Exists(dir)) return null;
            var f = Directory.EnumerateFiles(dir, "SecureBoot-*").FirstOrDefault();
            if (f == null) return null;
            var b = File.ReadAllBytes(f);          // 4 bytes de atributos + 1 byte de valor
            return b.Length >= 5 ? b[4] == 1 : null;
        }
        catch { return null; }
    }

    public static string? Virtualization(string kernel)
    {
        if (kernel.Contains("microsoft", StringComparison.OrdinalIgnoreCase)) return "WSL (Windows Subsystem for Linux)";
        if (Sys.FileExists("/.dockerenv") || Sys.FileExists("/run/.containerenv")) return "Contêiner";
        string vendor = Sys.Read("/sys/class/dmi/id/sys_vendor") ?? "", prod = Sys.Read("/sys/class/dmi/id/product_name") ?? "";
        string both = vendor + " " + prod;
        if (both.Contains("QEMU", StringComparison.OrdinalIgnoreCase) || both.Contains("KVM", StringComparison.OrdinalIgnoreCase)) return "QEMU/KVM";
        if (both.Contains("VMware", StringComparison.OrdinalIgnoreCase)) return "VMware";
        if (both.Contains("VirtualBox", StringComparison.OrdinalIgnoreCase) || both.Contains("innotek", StringComparison.OrdinalIgnoreCase)) return "VirtualBox";
        if (vendor.Contains("Microsoft", StringComparison.OrdinalIgnoreCase) && prod.Contains("Virtual", StringComparison.OrdinalIgnoreCase)) return "Hyper-V";
        if (both.Contains("Xen", StringComparison.OrdinalIgnoreCase)) return "Xen";
        if (both.Contains("Parallels", StringComparison.OrdinalIgnoreCase)) return "Parallels";
        if (both.Contains("Amazon EC2", StringComparison.OrdinalIgnoreCase)) return "Amazon EC2";
        if (both.Contains("Google Compute", StringComparison.OrdinalIgnoreCase)) return "Google Compute Engine";
        if ((Sys.Read("/proc/cpuinfo") ?? "").Contains(" hypervisor", StringComparison.Ordinal)) return "Máquina virtual (hipervisor não identificado)";
        return null;
    }

    /// <summary>Gerenciador de pacotes da distribuição (o primeiro que existir, na ordem da família).</summary>
    public static string? PackageManager()
    {
        foreach (var (bin, name) in new[] { ("apt-get", "apt"), ("dnf", "dnf"), ("pacman", "pacman"), ("zypper", "zypper"),
                                            ("apk", "apk"), ("xbps-install", "xbps"), ("emerge", "portage") })
            if (Shell.Which(bin) != null) return name;
        return null;
    }

    private static string Libc()
    {
        // glibc é o alvo suportado (D-017); em musl (Alpine) o programa não roda
        foreach (var d in new[] { "/lib/ld-musl-x86_64.so.1", "/lib/ld-musl-aarch64.so.1" })
            if (Sys.FileExists(d)) return "musl";
        return "glibc";
    }
}
