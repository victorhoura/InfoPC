namespace InfoPC;

/// <summary>
/// Reúne as fontes de sensores e mantém a lista de leituras (com mínimo e máximo da sessão).
/// <see cref="Collect"/> pode rodar em segundo plano; <see cref="Apply"/> mexe nas linhas e deve rodar na thread da tela.
/// </summary>
public sealed class SensorHub
{
    private readonly List<ISensorSource> _sources = new();
    private readonly Dictionary<string, SensorReading> _index = new();
    private readonly List<SensorReading> _rows = new();
    private readonly Dictionary<string, (float Value, int Prio)> _roles = new();

    public IReadOnlyList<SensorReading> Rows => _rows;
    public RaplSource? Rapl { get; private set; }

    /// <summary>Chamado depois de cada Apply, na mesma thread.</summary>
    public event Action<IReadOnlyList<SensorReading>>? Added;

    public SensorHub(IEnumerable<ISensorSource>? sources = null)
    {
        if (sources != null) _sources.AddRange(sources);
    }

    /// <summary>Fontes padrão do Linux (GPUs entram por <paramref name="gpus"/>).</summary>
    public static SensorHub CreateDefault(IEnumerable<GpuDevice>? gpus = null)
    {
        var rapl = new RaplSource();
        var hub = new SensorHub(new ISensorSource[]
        {
            new CpuSource(), new HwmonSource(gpus?.ToList()), new ThermalSource(), rapl, new MemorySource(), new DiskIoSource(), new NetSource(), new BatterySource(),
        }) { Rapl = rapl };
        foreach (var g in gpus ?? Array.Empty<GpuDevice>()) hub._sources.Add(new GpuSource(g));
        return hub;
    }

    public void AddSource(ISensorSource s) => _sources.Add(s);

    /// <summary>Lê todas as fontes (sem mexer nas linhas). Uma fonte que falha não derruba as outras.</summary>
    public List<SensorSample> Collect()
    {
        var all = new List<SensorSample>(256);
        foreach (var s in _sources)
        {
            try { all.AddRange(s.Read()); }
            catch { /* sensor ausente ou sem permissão: segue com os demais */ }
        }
        return all;
    }

    public void Apply(IReadOnlyList<SensorSample> samples)
    {
        var added = new List<SensorReading>();
        var seen = new HashSet<string>();
        _roles.Clear();
        foreach (var s in samples)
        {
            if (!seen.Add(s.Id)) continue; // mesma leitura vinda de duas fontes
            if (!_index.TryGetValue(s.Id, out var row))
            {
                row = new SensorReading { Id = s.Id, Hardware = s.Hardware, HwKind = s.HwKind, Kind = s.Kind, Name = s.Name };
                _index[s.Id] = row;
                _rows.Add(row);
                added.Add(row);
            }
            row.Update(s.Value);
            if (s.Role != null && s.Value != null &&
                (!_roles.TryGetValue(s.Role, out var cur) || s.Priority > cur.Prio))
                _roles[s.Role] = (s.Value.Value, s.Priority);
        }
        if (added.Count > 0) Added?.Invoke(added);
    }

    /// <summary>Coleta e aplica na mesma thread (modo texto e testes).</summary>
    public void Refresh() => Apply(Collect());

    public float? Role(string role) => _roles.TryGetValue(role, out var v) ? v.Value : null;

    public float? CpuTemperature => Role("cpu.temp");
    public float? CpuLoad => Role("cpu.load");
    public float? CpuPower => Role("cpu.power");
    public float? RamPercent => Role("ram.pct");

    public string DescribeCpuTempProblem()
    {
        if (CpuTemperature != null) return "";
        return DescribeMissingCpuTemperature();
    }

    /// <summary>Explica a ausência já constatada, sem acessar os dicionários da thread da tela.</summary>
    public static string DescribeMissingCpuTemperature()
    {
        if (Sys.List("/sys/class/hwmon").Count == 0 && Sys.List("/sys/class/thermal").Count == 0)
            return "O kernel não expõe sensores de temperatura (comum em máquina virtual, contêiner ou WSL).";
        return "Nenhum sensor de temperatura da CPU foi encontrado. Instale o lm-sensors e rode 'sudo sensors-detect', ou confira se os módulos coretemp (Intel) ou k10temp (AMD) estão carregados.";
    }
}
