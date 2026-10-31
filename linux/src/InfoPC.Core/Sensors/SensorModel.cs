using System.ComponentModel;

namespace InfoPC;

public enum SensorKind { Temperature, Load, Clock, Power, Voltage, Current, Fan, Control, Data, SmallData, Throughput, Energy, Level, Frequency }

public enum HwKind { Cpu, Gpu, Memory, Storage, Network, Board, Battery, Other }

/// <summary>
/// Uma leitura vinda de uma fonte. <paramref name="Role"/> marca os sensores que o resto do programa procura
/// (painel, overlay, gargalo, iluminação): "cpu.temp", "cpu.load", "gpu0.load"... A de maior <paramref name="Priority"/> vence.
/// </summary>
public sealed record SensorSample(string Id, string Hardware, HwKind HwKind, SensorKind Kind, string Name, float? Value,
                                  string? Role = null, int Priority = 1);

public interface ISensorSource
{
    IEnumerable<SensorSample> Read();
}

/// <summary>Linha do painel de sensores, com mínimo e máximo da sessão e notificação de mudança para a tela.</summary>
public sealed class SensorReading : INotifyPropertyChanged
{
    public string Id { get; init; } = "";
    public string Hardware { get; init; } = "";
    public HwKind HwKind { get; init; }
    public SensorKind Kind { get; init; }
    public string Name { get; init; } = "";
    public string Category => CategoryName(Kind);

    private float? _value, _min, _max;
    public float? RawValue => _value;
    public float? RawMin => _min;
    public float? RawMax => _max;

    public string Value => Format(Kind, _value);
    public string Min => Format(Kind, _min);
    public string Max => Format(Kind, _max);

    /// <summary>Ordem de exibição por tipo (clocks e temperaturas primeiro, como no GPU-Z).</summary>
    public int Order => Kind switch
    {
        SensorKind.Clock => 0, SensorKind.Temperature => 1, SensorKind.Load => 2, SensorKind.Fan => 3, SensorKind.Control => 4,
        SensorKind.Power => 5, SensorKind.Voltage => 6, SensorKind.Current => 7, SensorKind.SmallData => 8, SensorKind.Data => 9,
        SensorKind.Throughput => 10, _ => 11
    };

    public void Update(float? v)
    {
        _value = v;
        if (v != null)
        {
            _min = _min == null ? v : Math.Min(_min.Value, v.Value);
            _max = _max == null ? v : Math.Max(_max.Value, v.Value);
        }
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Min)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Max)));
    }

    public static string Format(SensorKind k, float? v)
    {
        if (v == null) return "—";
        return k switch
        {
            SensorKind.Temperature => $"{v:0.#} °C",
            SensorKind.Load or SensorKind.Control or SensorKind.Level => $"{v:0.#} %",
            SensorKind.Clock => $"{v:0} MHz",
            SensorKind.Power => $"{v:0.#} W",
            SensorKind.Voltage => $"{v:0.###} V",
            SensorKind.Current => $"{v:0.###} A",
            SensorKind.Fan => $"{v:0} RPM",
            SensorKind.Frequency => $"{v:0.#} Hz",
            SensorKind.Data => $"{v:0.##} GB",
            SensorKind.SmallData => $"{v:0.#} MB",
            SensorKind.Throughput => v >= 1048576 ? $"{v / 1048576f:0.##} MB/s" : $"{v / 1024f:0.#} KB/s",
            SensorKind.Energy => $"{v:0.#} Wh",
            _ => $"{v:0.##}"
        };
    }

    public static string CategoryName(SensorKind k) => k switch
    {
        SensorKind.Temperature => "Temperatura", SensorKind.Load => "Carga", SensorKind.Clock => "Clock", SensorKind.Power => "Consumo",
        SensorKind.Voltage => "Tensão", SensorKind.Current => "Corrente", SensorKind.Fan => "Ventoinhas", SensorKind.Control => "Controle",
        SensorKind.Level => "Nível", SensorKind.Data or SensorKind.SmallData => "Dados", SensorKind.Throughput => "Taxa",
        SensorKind.Frequency => "Frequência", SensorKind.Energy => "Energia", _ => k.ToString()
    };

    public static string HwKindName(HwKind k) => k switch
    {
        HwKind.Cpu => "Processador", HwKind.Gpu => "Placa de vídeo", HwKind.Memory => "Memória", HwKind.Storage => "Armazenamento",
        HwKind.Network => "Rede", HwKind.Board => "Placa-mãe", HwKind.Battery => "Bateria", _ => "Outros"
    };

    public event PropertyChangedEventHandler? PropertyChanged;
}
