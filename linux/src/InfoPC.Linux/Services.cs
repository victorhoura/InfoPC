using Avalonia;
using Avalonia.Styling;
using Avalonia.Threading;

namespace InfoPC.Linux;

/// <summary>Séries de valores por papel de sensor ("cpu.temp", "gpu0.load"...), uma por segundo, com tamanho fixo.</summary>
public sealed class SeriesStore
{
    private readonly int _capacity;
    private readonly Dictionary<string, List<float?>> _data = new();
    public SeriesStore(int capacity) => _capacity = capacity;

    public void Clear() => _data.Clear();

    public void Add(string role, float? value)
    {
        if (!_data.TryGetValue(role, out var l)) _data[role] = l = new List<float?>(_capacity + 1);
        l.Add(value);
        if (l.Count > _capacity) l.RemoveAt(0);
    }

    /// <summary>Os últimos <paramref name="seconds"/> valores (menos, se a sessão for mais curta).</summary>
    public float?[] Last(string role, int seconds)
    {
        if (!_data.TryGetValue(role, out var l)) return Array.Empty<float?>();
        int n = Math.Min(seconds, l.Count);
        return l.GetRange(l.Count - n, n).ToArray();
    }
}

/// <summary>
/// Estado compartilhado pelas páginas: configurações, sensores (lidos a cada segundo em segundo plano), GPUs, discos
/// e o histórico dos gráficos. O hardware é detectado em paralelo para a janela abrir na hora.
/// </summary>
public sealed class Services
{
    public static Services Current { get; } = new();

    public AppSettings Settings { get; private set; } = new();
    public SensorHub Hub { get; private set; } = new();
    public IReadOnlyList<GpuDevice> Gpus { get; private set; } = Array.Empty<GpuDevice>();
    public GpuDevice? MainGpu { get; private set; }
    public IReadOnlyList<PhysicalDisk> Disks { get; private set; } = Array.Empty<PhysicalDisk>();
    public CpuDetails? Cpu { get; private set; }
    public OsDetails? Os { get; private set; }
    public DmiInfo? Dmi { get; private set; }
    public RamLayout? Ram { get; set; }
    public List<SmartInfo>? Smart { get; set; }
    public SeriesStore History { get; } = new(900);
    public bool Ready { get; private set; }
    public bool Paused { get; set; }

    /// <summary>Hardware detectado (na thread da tela).</summary>
    public event Action? Loaded;
    /// <summary>Sensores atualizados (na thread da tela), uma vez por segundo.</summary>
    public event Action? Tick;
    /// <summary>Um dispositivo USB ou de armazenamento entrou ou saiu (na thread da tela).</summary>
    public event Action<DeviceChange>? DeviceChanged;

    private DeviceWatcher _watcher = new();
    private int _deviceTick;

    private bool _started;
    private bool _settingsLoaded;
    private CancellationTokenSource _cts = new();
    private TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Termina quando o hardware foi detectado.</summary>
    public Task WhenReady => _ready.Task;

    /// <summary>Para a leitura periódica e esquece o estado (os testes trocam de fixture entre uma tela e outra).</summary>
    internal void ResetForTests()
    {
        Stop();
        _cts = new CancellationTokenSource();
        _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _started = false; _settingsLoaded = false; Ready = false; Paused = false;
        Settings = new AppSettings(); Hub = new SensorHub(); Gpus = Array.Empty<GpuDevice>(); MainGpu = null; Disks = Array.Empty<PhysicalDisk>();
        Cpu = null; Os = null; Dmi = null; Ram = null; Smart = null;
        History.Clear();
        _deviceTick = 0;
        Loaded = null; Tick = null; DeviceChanged = null;
    }

    /// <summary>Impede novas leituras e descarta atualizações pendentes quando a janela fecha.</summary>
    public void Stop()
    {
        _cts.Cancel();
        _ready.TrySetCanceled();
    }

    /// <summary>Carrega uma vez só. A janela chama antes de StartAsync: o aviso de um Load que falhou precisa existir quando ela olha se há o que mostrar.</summary>
    public void LoadSettings()
    {
        if (_settingsLoaded) return;
        _settingsLoaded = true;
        Settings = AppSettings.Load();
        ApplyTheme(Settings.LightTheme);
    }

    public async Task StartAsync()
    {
        if (_started) return;
        _started = true;
        var token = _cts.Token;
        var ready = _ready;
        LoadSettings();

        try
        {
            // Só publica o resultado se esta ainda for a sessão ativa.
            var state = await Task.Run(() =>
            {
                var os = OsInfo.Read();
                var cpu = CpuInfo.Read();
                var dmi = InfoPC.Dmi.Read();
                var gpus = GpuCollector.Detect(refresh: true);
                var disks = DiskCollector.Disks();
                var hub = SensorHub.CreateDefault(gpus);
                var watcher = new DeviceWatcher();
                watcher.Poll();
                return (os, cpu, dmi, gpus, disks, hub, watcher);
            });
            if (token.IsCancellationRequested) return;
            (Os, Cpu, Dmi, Gpus, Disks, Hub, _watcher) = state;
            MainGpu = GpuCollector.Main(Gpus);
        }
        catch (Exception ex)
        {
            CrashLog.Write("Detecção de hardware", ex);
            ready.TrySetException(ex);
            throw;
        }
        Ready = true;
        ready.TrySetResult();
        Loaded?.Invoke();

        var hub = Hub;
        var watcher = _watcher;
        _ = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    if (!Paused)
                    {
                        var samples = hub.Collect();
                        // sysfs pode esperar um dispositivo/driver: nunca o leia na thread da janela.
                        var changes = ++_deviceTick % 2 == 0 ? watcher.Poll() : new List<DeviceChange>();
                        await Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            if (token.IsCancellationRequested || Paused) return;
                            hub.Apply(samples);
                            Record();
                            NotifyTick();
                            foreach (var c in changes) DeviceChanged?.Invoke(c);
                        });
                    }
                }
                catch (Exception ex) { CrashLog.Write("Atualização de sensores", ex); }
                // Esperar a aplicação limita a fila a uma atualização, mesmo com a janela ocupada.
                try { await Task.Delay(1000, token); }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
            }
        });
    }

    /// <summary>Uma leitura completa e síncrona (usado pelos testes de interface).</summary>
    internal void TickNow()
    {
        Hub.Apply(Hub.Collect());
        Record();
        NotifyTick();
    }

    private void NotifyTick()
    {
        foreach (Action handler in Tick?.GetInvocationList() ?? Array.Empty<Delegate>())
        {
            try { handler(); }
            catch (Exception ex) { CrashLog.Write("Página durante a leitura de sensores", ex); }
        }
    }

    /// <summary>Recarrega a lista de discos (pendrive plugado, S.M.A.R.T. lido...).</summary>
    public async Task RefreshDisksAsync()
    {
        var disks = await Task.Run(DiskCollector.Disks);
        Disks = disks;
    }

    private static readonly string[] Roles = { "cpu.temp", "cpu.load", "cpu.power", "cpu.clock", "ram.pct" };
    private static readonly string[] GpuRoles = { "temp", "load", "power", "clock", "vram.pct" };

    private void Record()
    {
        foreach (var r in Roles) History.Add(r, Hub.Role(r));
        foreach (var g in Gpus)
            foreach (var r in GpuRoles) History.Add($"gpu{g.Index}.{r}", Hub.Role($"gpu{g.Index}.{r}"));
    }

    /// <summary>Papel de sensor da GPU principal ("temp", "load"...), ex.: GpuRole("temp") → "gpu0.temp".</summary>
    public string? GpuKey(string what) => MainGpu == null ? null : $"gpu{MainGpu.Index}.{what}";
    public float? GpuValue(string what) => GpuKey(what) is { } k ? Hub.Role(k) : null;

    public void Save()
    {
        try { Settings.Save(); } catch { /* pasta sem permissão: o programa segue sem gravar */ }
    }

    public static void ApplyTheme(bool light)
    {
        if (Application.Current != null)
            Application.Current.RequestedThemeVariant = light ? ThemeVariant.Light : ThemeVariant.Dark;
    }
}
