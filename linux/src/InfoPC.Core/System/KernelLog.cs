using System.Text.RegularExpressions;

namespace InfoPC;

/// <summary>Erros de hardware vistos no log do kernel: o equivalente do WHEA e do reset do driver de vídeo do Windows.</summary>
public sealed record KernelErrors(int Mce, int GpuResets, int DiskErrors, int Oom, string? LastDetail, string? Error)
{
    public int Total => Mce + GpuResets + DiskErrors + Oom;
    public string Summary => $"{Mce} de CPU/memória (MCE) · {GpuResets} da placa de vídeo · {DiskErrors} de disco · {Oom} de memória esgotada";
}

public static class KernelLog
{
    private static readonly Regex MceRx = new(@"\[Hardware Error\]|Machine check|mce: .*(error|CPU)|EDAC .*\b(CE|UE)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex GpuRx = new(
        @"NVRM: Xid|NVRM: GPU .* has fallen off|amdgpu.*(GPU reset|ring \S+ timeout|\*ERROR\* ring)|i915.*(GPU HANG|Hangcheck)|nouveau.*(fifo: .*(fault|timeout)|timeout)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex DiskRx = new(
        @"blk_update_request: I/O error|Buffer I/O error|ata\d+(\.\d+)?: (failed command|exception Emask)|nvme\d+: .*(timeout|controller is down|I/O \d+ QID)|EXT4-fs error|XFS .*(Corruption|metadata I/O error)|BTRFS error",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex OomRx = new(@"Out of memory: Killed process|oom-kill:", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Lê o log do kernel dos últimos dias (journalctl; sem ele, o dmesg só alcança desde a inicialização).</summary>
    public static KernelErrors ErrorsSince(int days)
    {
        var r = Shell.Run("journalctl", new[] { "-k", "--no-pager", "-q", "--since", $"{days} days ago" }, 15000);
        if (r.Ok && r.Out.Trim().Length > 0) return Scan(r.Out);
        var d = Shell.Run("dmesg", null, 8000);
        if (d.Ok) return Scan(d.Out);
        string why = r.NotFound && d.NotFound ? "journalctl e dmesg não encontrados"
            : (r.Err + d.Err).Contains("denied", StringComparison.OrdinalIgnoreCase) || (r.Err + d.Err).Contains("permitted", StringComparison.OrdinalIgnoreCase)
                ? "sem permissão para ler o log do kernel (entre no grupo systemd-journal ou adm)"
                : "não foi possível ler o log do kernel";
        return new KernelErrors(0, 0, 0, 0, null, why);
    }

    public static KernelErrors Scan(string log)
    {
        int mce = 0, gpu = 0, disk = 0, oom = 0;
        string? last = null;
        foreach (var raw in log.Split('\n'))
        {
            string line = raw.Trim();
            if (line.Length == 0) continue;
            if (MceRx.IsMatch(line)) { mce++; last = line; }
            else if (GpuRx.IsMatch(line)) { gpu++; last = line; }
            else if (DiskRx.IsMatch(line)) { disk++; last = line; }
            else if (OomRx.IsMatch(line)) { oom++; last = line; }
        }
        if (last is { Length: > 160 }) last = last[..160] + "…";
        return new KernelErrors(mce, gpu, disk, oom, last, null);
    }
}
