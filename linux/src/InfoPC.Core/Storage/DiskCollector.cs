using System.Text;
using System.Text.RegularExpressions;

namespace InfoPC;

public sealed record MountInfo(string Device, string MountPoint, string FsType, long TotalBytes, long FreeBytes, string? Disk)
{
    public long UsedBytes => Math.Max(0, TotalBytes - FreeBytes);
    public double UsedPct => TotalBytes > 0 ? 100.0 * UsedBytes / TotalBytes : 0;
    public bool IsNetwork => FsType is "nfs" or "nfs4" or "cifs" or "smb3" or "fuse.sshfs";
}

public sealed class PhysicalDisk
{
    public string Name { get; init; } = "";
    public string DevPath => "/dev/" + Name;
    public string Model { get; init; } = "";
    public string Vendor { get; init; } = "";
    public string Serial { get; init; } = "";
    public string Firmware { get; init; } = "";
    public long SizeBytes { get; init; }
    public bool Rotational { get; init; }
    public bool Removable { get; init; }
    public string Bus { get; init; } = "";
    public double? TempC { get; set; }
    public List<MountInfo> Mounts { get; } = new();

    /// <summary>Discos de máquina virtual dizem que giram (rotational=1) mesmo sendo arquivos: não são HDD de verdade.</summary>
    public bool IsVirtual => Bus == "Virtio" || new[] { "Virtual", "QEMU", "VBOX", "VMware", "Msft" }.Any(v => Model.Contains(v, StringComparison.OrdinalIgnoreCase) || Vendor.Contains(v, StringComparison.OrdinalIgnoreCase));

    public string MediaType => IsVirtual ? "Disco virtual" : Bus switch
    {
        "NVMe" => "SSD NVMe", "MMC" => "Cartão / eMMC",
        _ => Rotational ? "HDD" : "SSD"
    };

    public string Title => string.IsNullOrWhiteSpace(Model) ? Name : Model;
}

/// <summary>Discos físicos e unidades montadas, lidos do sysfs e de /proc/self/mountinfo (sem depender de lsblk).</summary>
public static class DiskCollector
{
    private static readonly string[] RealFs =
    {
        "ext2", "ext3", "ext4", "xfs", "btrfs", "f2fs", "jfs", "reiserfs", "zfs", "ntfs", "ntfs3", "fuseblk", "vfat", "exfat", "msdos", "hfsplus", "udf", "iso9660",
        "nfs", "nfs4", "cifs", "smb3", "fuse.sshfs", "bcachefs",
    };

    public static List<PhysicalDisk> Disks()
    {
        var list = new List<PhysicalDisk>();
        foreach (var n in Sys.List("/sys/block"))
        {
            if (!IsCandidate(n)) continue;
            long sectors = Sys.Long($"/sys/block/{n}/size") ?? 0;
            if (sectors <= 0) continue;
            string b = $"/sys/block/{n}";
            string real = Sys.RealPath(b) ?? "";
            string bus = BusOf(n, real);
            var disk = new PhysicalDisk
            {
                Name = n, Bus = bus, SizeBytes = sectors * 512,
                Model = (Sys.Read(b + "/device/model") ?? "").Trim(), Vendor = (Sys.Read(b + "/device/vendor") ?? "").Trim(),
                Serial = (Sys.Read(b + "/device/serial") ?? "").Trim(),
                Firmware = (Sys.Read(b + "/device/firmware_rev") ?? Sys.Read(b + "/device/rev") ?? "").Trim(),
                Rotational = Sys.Read(b + "/queue/rotational") == "1", Removable = Sys.Read(b + "/removable") == "1" || bus == "USB",
            };
            disk.TempC = Temperature(n);
            list.Add(disk);
        }
        var mounts = Mounts();
        foreach (var m in mounts)
        {
            var d = list.FirstOrDefault(x => x.Name == m.Disk);
            d?.Mounts.Add(m);
        }
        return list;
    }

    private static bool IsCandidate(string n) =>
        (n.StartsWith("nvme") || n.StartsWith("sd") || n.StartsWith("vd") || n.StartsWith("hd") || n.StartsWith("xvd") || n.StartsWith("mmcblk"))
        && !n.Contains("boot") && !n.Contains("rpmb");

    private static string BusOf(string name, string real)
    {
        if (name.StartsWith("nvme")) return "NVMe";
        if (name.StartsWith("mmcblk")) return "MMC";
        if (name.StartsWith("vd") || real.Contains("/virtio")) return "Virtio";
        if (real.Contains("/usb")) return "USB";
        if (real.Contains("/ata")) return "SATA";
        return "SCSI/SAS";
    }

    /// <summary>Temperatura do disco: hwmon do NVMe (device/hwmon) ou do drivetemp (SATA).</summary>
    private static double? Temperature(string name)
    {
        var roots = new List<string> { $"/sys/block/{name}/device", $"/sys/block/{name}/device/hwmon" };   // NVMe sem multipath; SATA (drivetemp)
        var m = Regex.Match(name, @"^nvme(\d+)n\d+$");
        if (m.Success) roots.Add($"/sys/class/nvme/nvme{m.Groups[1].Value}");                                // controlador NVMe
        foreach (var root in roots)
            foreach (var h in Sys.List(root).Where(x => x.StartsWith("hwmon", StringComparison.Ordinal)))
                if (Sys.Double($"{root}/{h}/temp1_input") is { } t && t > -100000) return t / 1000;
        return null;
    }

    /// <summary>Unidades montadas de sistemas de arquivos de verdade, cada uma ligada ao disco físico que a hospeda.</summary>
    public static List<MountInfo> Mounts() => ParseMountInfo(Sys.Lines("/proc/self/mountinfo"), true);

    public static List<MountInfo> ParseMountInfo(IEnumerable<string> lines, bool query)
    {
        var raw = new List<(string MajMin, string Mount, string Fs, string Source)>();
        foreach (var line in lines)
        {
            int dash = line.IndexOf(" - ", StringComparison.Ordinal);
            if (dash < 0) continue;
            var pre = line[..dash].Split(' ');
            var post = line[(dash + 3)..].Split(' ');
            if (pre.Length < 5 || post.Length < 2) continue;
            raw.Add((pre[2], Unescape(pre[4]), post[0], Unescape(post[1])));
        }

        var result = new List<MountInfo>();
        foreach (var grp in raw.Where(r => RealFs.Contains(r.Fs) && !Excluded(r.Mount)).GroupBy(r => r.Source + "|" + r.MajMin))
        {
            // vários pontos de montagem do mesmo dispositivo (subvolumes do btrfs): fica o mais curto
            var m = grp.OrderBy(r => r.Mount == "/" ? 0 : 1).ThenBy(r => r.Mount.Length).First();
            long total = 0, free = 0;
            if (query)
            {
                try
                {
                    var di = new DriveInfo(m.Mount);
                    total = di.TotalSize; free = di.AvailableFreeSpace;
                }
                catch { continue; }
            }
            string? disk = m.Source.StartsWith('/') || m.Fs == "zfs" ? DiskOf(m.MajMin) : null;
            result.Add(new MountInfo(m.Source, m.Mount, m.Fs, total, free, disk));
        }
        return result.OrderBy(m => m.MountPoint == "/" ? "" : m.MountPoint, StringComparer.Ordinal).ToList();
    }

    private static bool Excluded(string mount) =>
        mount.StartsWith("/snap/") || mount.StartsWith("/var/lib/docker") || mount.StartsWith("/var/lib/flatpak")
        || mount.StartsWith("/sys") || mount.StartsWith("/proc")
        || (mount.StartsWith("/run/") && !mount.StartsWith("/run/media/"));

    private static string Unescape(string s) =>
        s.Contains('\\') ? Regex.Replace(s, @"\\([0-7]{3})", m => ((char)Convert.ToInt32(m.Groups[1].Value, 8)).ToString()) : s;

    /// <summary>Disco físico dono de "maj:min": sobe de partição para disco e atravessa dm/LUKS/LVM/RAID.</summary>
    public static string? DiskOf(string majMin) => DiskOfName(Sys.RealPath("/sys/dev/block/" + majMin)?.Split('/').LastOrDefault(), 0);

    private static string? DiskOfName(string? name, int depth)
    {
        if (string.IsNullOrEmpty(name) || depth > 6) return null;
        if (name.StartsWith("dm-") || name.StartsWith("md"))
        {
            string? slave = Sys.List($"/sys/class/block/{name}/slaves").FirstOrDefault();
            return DiskOfName(slave, depth + 1);
        }
        string? real = Sys.RealPath($"/sys/class/block/{name}");
        if (real == null) return IsCandidate(name) ? name : null;
        if (Sys.Exists(real + "/partition") || Sys.Exists($"/sys/class/block/{name}/partition"))
            return real.Split('/').Reverse().Skip(1).FirstOrDefault();
        return name;
    }
}
