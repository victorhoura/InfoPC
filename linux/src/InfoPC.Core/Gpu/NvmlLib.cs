using System.Runtime.InteropServices;
using System.Text;

namespace InfoPC;

/// <summary>
/// P/Invoke mínimo da NVML (libnvidia-ml.so.1, instalada com o driver proprietário da NVIDIA).
/// Sem driver (ou com o nouveau), <see cref="Available"/> é false e as GPUs NVIDIA caem na leitura por sysfs.
/// </summary>
internal static class NvmlLib
{
    private const string Lib = "libnvidia-ml.so.1";

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    public struct PciInfo
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 16)] public string BusIdLegacy;
        public uint Domain, Bus, Device;
        public uint PciDeviceId;    // (device << 16) | vendor
        public uint PciSubSystemId; // (subsys << 16) | subvendor
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string BusId;
    }

    public struct Memory { public ulong Total, Free, Used; }
    public struct Utilization { public uint Gpu, Memory; }

    public const int ClockGraphics = 0, ClockSm = 1, ClockMem = 2, ClockVideo = 3;

    [DllImport(Lib, EntryPoint = "nvmlInit_v2")] private static extern int nvmlInit();
    [DllImport(Lib)] private static extern int nvmlShutdown();
    [DllImport(Lib, EntryPoint = "nvmlDeviceGetCount_v2")] private static extern int nvmlDeviceGetCount(out uint count);
    [DllImport(Lib, EntryPoint = "nvmlDeviceGetHandleByIndex_v2")] private static extern int nvmlDeviceGetHandleByIndex(uint index, out IntPtr device);
    [DllImport(Lib, EntryPoint = "nvmlDeviceGetPciInfo_v3")] private static extern int nvmlDeviceGetPciInfo(IntPtr device, out PciInfo pci);
    [DllImport(Lib, CharSet = CharSet.Ansi)] private static extern int nvmlDeviceGetName(IntPtr device, StringBuilder name, uint length);
    [DllImport(Lib, CharSet = CharSet.Ansi)] private static extern int nvmlDeviceGetVbiosVersion(IntPtr device, StringBuilder version, uint length);
    [DllImport(Lib, CharSet = CharSet.Ansi)] private static extern int nvmlSystemGetDriverVersion(StringBuilder version, uint length);
    [DllImport(Lib, EntryPoint = "nvmlSystemGetCudaDriverVersion_v2")] private static extern int nvmlSystemGetCudaDriverVersion(out int version);
    [DllImport(Lib)] private static extern int nvmlDeviceGetMemoryInfo(IntPtr device, out Memory memory);
    [DllImport(Lib)] private static extern int nvmlDeviceGetBAR1MemoryInfo(IntPtr device, out Memory bar1);
    [DllImport(Lib)] private static extern int nvmlDeviceGetMemoryBusWidth(IntPtr device, out uint width);
    [DllImport(Lib)] private static extern int nvmlDeviceGetNumGpuCores(IntPtr device, out uint cores);
    [DllImport(Lib)] private static extern int nvmlDeviceGetCudaComputeCapability(IntPtr device, out int major, out int minor);
    [DllImport(Lib)] private static extern int nvmlDeviceGetMaxPcieLinkGeneration(IntPtr device, out uint gen);
    [DllImport(Lib)] private static extern int nvmlDeviceGetMaxPcieLinkWidth(IntPtr device, out uint width);
    [DllImport(Lib)] private static extern int nvmlDeviceGetCurrPcieLinkGeneration(IntPtr device, out uint gen);
    [DllImport(Lib)] private static extern int nvmlDeviceGetCurrPcieLinkWidth(IntPtr device, out uint width);
    [DllImport(Lib)] private static extern int nvmlDeviceGetMaxClockInfo(IntPtr device, int type, out uint mhz);
    [DllImport(Lib)] private static extern int nvmlDeviceGetClockInfo(IntPtr device, int type, out uint mhz);
    [DllImport(Lib)] private static extern int nvmlDeviceGetPowerManagementDefaultLimit(IntPtr device, out uint mw);
    [DllImport(Lib)] private static extern int nvmlDeviceGetEnforcedPowerLimit(IntPtr device, out uint mw);
    [DllImport(Lib)] private static extern int nvmlDeviceGetPowerManagementLimitConstraints(IntPtr device, out uint minMw, out uint maxMw);
    [DllImport(Lib)] private static extern int nvmlDeviceGetPowerUsage(IntPtr device, out uint mw);
    [DllImport(Lib)] private static extern int nvmlDeviceGetPerformanceState(IntPtr device, out int pstate);
    [DllImport(Lib)] private static extern int nvmlDeviceGetCurrentClocksThrottleReasons(IntPtr device, out ulong reasons);
    [DllImport(Lib)] private static extern int nvmlDeviceGetEncoderUtilization(IntPtr device, out uint util, out uint periodUs);
    [DllImport(Lib)] private static extern int nvmlDeviceGetDecoderUtilization(IntPtr device, out uint util, out uint periodUs);
    [DllImport(Lib)] private static extern int nvmlDeviceGetUtilizationRates(IntPtr device, out Utilization util);
    [DllImport(Lib)] private static extern int nvmlDeviceGetTemperature(IntPtr device, int sensor, out uint temp);
    [DllImport(Lib)] private static extern int nvmlDeviceGetTemperatureThreshold(IntPtr device, int type, out uint temp);
    [DllImport(Lib)] private static extern int nvmlDeviceGetFanSpeed(IntPtr device, out uint percent);

    private static readonly string[] Candidates =
    {
        "libnvidia-ml.so.1", "libnvidia-ml.so",
        "/usr/lib/x86_64-linux-gnu/libnvidia-ml.so.1", "/usr/lib64/libnvidia-ml.so.1", "/usr/lib/libnvidia-ml.so.1",
        "/usr/lib/aarch64-linux-gnu/libnvidia-ml.so.1", "/run/opengl-driver/lib/libnvidia-ml.so.1", "/usr/lib/wsl/lib/libnvidia-ml.so.1",
    };

    private static bool? _ok;

    static NvmlLib()
    {
        try
        {
            NativeLibrary.SetDllImportResolver(typeof(NvmlLib).Assembly, (name, _, _) =>
            {
                if (name != Lib) return IntPtr.Zero;
                foreach (var c in Candidates)
                    if (NativeLibrary.TryLoad(c, out var h)) return h;
                return IntPtr.Zero;
            });
        }
        catch (InvalidOperationException) { /* já registrado */ }
    }

    /// <summary>Inicializa uma vez; false se não houver driver NVIDIA proprietário.</summary>
    public static bool Available => _ok ??= Call(() => nvmlInit());

    private static bool Call(Func<int> f)
    {
        try { return f() == 0; }
        catch (DllNotFoundException) { return false; }
        catch (EntryPointNotFoundException) { return false; }
        catch (BadImageFormatException) { return false; }
    }

    public static int Raw(Func<int> f)
    {
        try { return f(); }
        catch (DllNotFoundException) { return 999; }
        catch (EntryPointNotFoundException) { return 3; }
    }

    public static List<(IntPtr Handle, PciInfo Pci)> Devices()
    {
        var list = new List<(IntPtr, PciInfo)>();
        uint n = 0;
        if (!Available || !Call(() => nvmlDeviceGetCount(out n))) return list;
        for (uint i = 0; i < n; i++)
        {
            IntPtr h = IntPtr.Zero; PciInfo pci = default;
            if (Call(() => nvmlDeviceGetHandleByIndex(i, out h)) && Call(() => nvmlDeviceGetPciInfo(h, out pci))) list.Add((h, pci));
        }
        return list;
    }

    /// <summary>"00000000:01:00.0" → "0000:01:00.0", igual ao nome da pasta em /sys/bus/pci/devices.</summary>
    public static string SysfsSlot(string nvmlBusId)
    {
        string s = nvmlBusId.Trim().ToLowerInvariant();
        int firstColon = s.IndexOf(':');
        return firstColon > 4 ? s[(firstColon - 4)..] : s;
    }

    public static string? Name(IntPtr h) { var sb = new StringBuilder(96); return Call(() => nvmlDeviceGetName(h, sb, (uint)sb.Capacity)) ? sb.ToString() : null; }
    public static string? DriverVersion() { var sb = new StringBuilder(96); return Call(() => nvmlSystemGetDriverVersion(sb, (uint)sb.Capacity)) ? sb.ToString() : null; }
    public static string? CudaVersion() { int v = 0; return Call(() => nvmlSystemGetCudaDriverVersion(out v)) ? $"{v / 1000}.{v % 1000 / 10}" : null; }
    public static string? Vbios(IntPtr h) { var sb = new StringBuilder(64); return Call(() => nvmlDeviceGetVbiosVersion(h, sb, (uint)sb.Capacity)) ? sb.ToString() : null; }
    public static Memory? MemoryInfo(IntPtr h) { Memory m = default; return Call(() => nvmlDeviceGetMemoryInfo(h, out m)) ? m : null; }
    public static Memory? Bar1(IntPtr h) { Memory m = default; return Call(() => nvmlDeviceGetBAR1MemoryInfo(h, out m)) ? m : null; }
    public static uint? BusWidth(IntPtr h) => U(h, nvmlDeviceGetMemoryBusWidth);
    public static uint? Cores(IntPtr h) => U(h, nvmlDeviceGetNumGpuCores);
    public static uint? MaxPcieGen(IntPtr h) => U(h, nvmlDeviceGetMaxPcieLinkGeneration);
    public static uint? MaxPcieWidth(IntPtr h) => U(h, nvmlDeviceGetMaxPcieLinkWidth);
    public static uint? CurPcieGen(IntPtr h) => U(h, nvmlDeviceGetCurrPcieLinkGeneration);
    public static uint? CurPcieWidth(IntPtr h) => U(h, nvmlDeviceGetCurrPcieLinkWidth);
    public static uint? PowerDefaultMw(IntPtr h) => U(h, nvmlDeviceGetPowerManagementDefaultLimit);
    public static uint? PowerLimitMw(IntPtr h) => U(h, nvmlDeviceGetEnforcedPowerLimit);
    public static uint? PowerUsageMw(IntPtr h) => U(h, nvmlDeviceGetPowerUsage);
    public static uint? FanPercent(IntPtr h) => U(h, nvmlDeviceGetFanSpeed);
    public static uint? Temperature(IntPtr h) { uint v = 0; return Call(() => nvmlDeviceGetTemperature(h, 0, out v)) ? v : null; }
    public static uint? SlowdownTemp(IntPtr h) { uint v = 0; return Call(() => nvmlDeviceGetTemperatureThreshold(h, 1, out v)) ? v : null; }
    public static uint? MaxClock(IntPtr h, int type) { uint v = 0; return Call(() => nvmlDeviceGetMaxClockInfo(h, type, out v)) ? v : null; }
    public static uint? Clock(IntPtr h, int type) { uint v = 0; return Call(() => nvmlDeviceGetClockInfo(h, type, out v)) ? v : null; }
    public static (uint Min, uint Max)? PowerConstraintsMw(IntPtr h) { uint a = 0, b = 0; return Call(() => nvmlDeviceGetPowerManagementLimitConstraints(h, out a, out b)) ? (a, b) : null; }
    public static string? ComputeCapability(IntPtr h) { int a = 0, b = 0; return Call(() => nvmlDeviceGetCudaComputeCapability(h, out a, out b)) ? $"{a}.{b}" : null; }
    public static int? PState(IntPtr h) { int v = 0; return Call(() => nvmlDeviceGetPerformanceState(h, out v)) ? v : null; }
    public static ulong? ThrottleReasons(IntPtr h) { ulong v = 0; return Call(() => nvmlDeviceGetCurrentClocksThrottleReasons(h, out v)) ? v : null; }
    public static uint? EncoderUtil(IntPtr h) { uint v = 0; return Call(() => nvmlDeviceGetEncoderUtilization(h, out v, out _)) ? v : null; }
    public static uint? DecoderUtil(IntPtr h) { uint v = 0; return Call(() => nvmlDeviceGetDecoderUtilization(h, out v, out _)) ? v : null; }
    public static Utilization? Util(IntPtr h) { Utilization u = default; return Call(() => nvmlDeviceGetUtilizationRates(h, out u)) ? u : null; }

    private delegate int UFunc(IntPtr h, out uint v);
    private static uint? U(IntPtr h, UFunc f) { uint v = 0; return Call(() => f(h, out v)) ? v : null; }

    /// <summary>Motivos de limitação de clock (bits da NVML) em português; a mesma lista do Windows.</summary>
    public static string PerfCap(ulong r)
    {
        var parts = new List<string>();
        if ((r & 0x4) != 0) parts.Add("Energia");
        if ((r & 0x60) != 0) parts.Add("Térmico");
        if ((r & 0x8) != 0) parts.Add("HW slowdown");
        if ((r & 0x80) != 0) parts.Add("Power brake");
        if (parts.Count == 0) return (r & 0x1) != 0 ? "Ociosa" : "Nenhum";
        return string.Join(" + ", parts);
    }

    public static bool IsThermalThrottle(ulong r) => (r & 0x60) != 0;
}
