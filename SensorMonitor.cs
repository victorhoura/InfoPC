using System.Collections.ObjectModel;
using LibreHardwareMonitor.Hardware;

namespace InfoPC;

/// <summary>Encapsula o LibreHardwareMonitorLib e mantém a lista de sensores atualizada.</summary>
public sealed class SensorMonitor : IDisposable
{
    private readonly Computer _computer;
    /// <summary>Memória e discos: num PC com RAID, a detecção de discos passa de um minuto. Abrem depois, para a CPU e a GPU não esperarem por eles.</summary>
    private readonly Computer[] _later;
    private volatile Computer[] _laterOpen = Array.Empty<Computer>();
    private readonly Dictionary<string, SensorRow> _index = new();
    private volatile bool _open;

    public ObservableCollection<SensorRow> Rows { get; } = new();
    public bool IsOpen => _open;

    public SensorMonitor()
    {
        _computer = new Computer
        {
            IsCpuEnabled = true,
            IsGpuEnabled = true,
            IsMotherboardEnabled = true,
            IsNetworkEnabled = true,
            IsControllerEnabled = true
        };
        _later = new[] { new Computer { IsMemoryEnabled = true }, new Computer { IsStorageEnabled = true } };
    }

    /// <summary>Detecta CPU e GPU fora da thread da janela (menos de um segundo). Até terminar, Refresh não faz nada. Memória e discos continuam em segundo plano e entram no Refresh quando ficam prontos.</summary>
    public Task OpenAsync() => Task.Run(() =>
    {
        _computer.Open();
        _open = true;
        _ = Task.Run(OpenLater);
    });

    private void OpenLater()
    {
        foreach (var c in _later)
        {
            // sem memória ou discos, o resto continua funcionando, como sempre foi com sensor que falta
            try { c.Open(); }
            catch { continue; }
            _laterOpen = _laterOpen.Append(c).ToArray();
        }
    }

    public void Refresh()
    {
        if (!_open) return;
        foreach (var hw in _computer.Hardware)
            Walk(hw);
        foreach (var c in _laterOpen)
            foreach (var hw in c.Hardware)
                Walk(hw);
    }

    /// <summary>Clock de cada núcleo da CPU. A biblioteca chama de "CPU Core #1" na Intel e pode chamar de "Core #1" em outras; "Bus Speed" e "Effective" não contam.</summary>
    public List<float> CpuCoreClocks() =>
        Rows.Where(r => r.HardwareType == HardwareType.Cpu && r.Type == SensorType.Clock &&
                        (r.Name.StartsWith("CPU Core #") || r.Name.StartsWith("Core #")) && !r.Name.Contains("Effective") && r.RawValue > 0)
            .Select(r => r.RawValue!.Value).ToList();

    private void Walk(IHardware hw)
    {
        hw.Update();
        foreach (var s in hw.Sensors)
        {
            string id = s.Identifier.ToString();
            if (!_index.TryGetValue(id, out var row))
            {
                row = new SensorRow
                {
                    Id = id,
                    Hardware = hw.Name,
                    Category = SensorRow.CategoryName(s.SensorType),
                    Name = s.Name,
                    Type = s.SensorType,
                    HardwareType = hw.HardwareType
                };
                _index[id] = row;
                Rows.Add(row);
            }
            row.Update(s.Value, s.Min, s.Max);
        }
        foreach (var sub in hw.SubHardware)
            Walk(sub);
    }

    /// <summary>Temperatura principal da CPU: Package (Intel) / Tctl-Tdie (AMD), senão a maior entre os núcleos.</summary>
    public float? CpuTemperature
    {
        get
        {
            var temps = Rows.Where(r => r.HardwareType == HardwareType.Cpu
                                     && r.Type == SensorType.Temperature
                                     && r.RawValue is > 0).ToList();
            var main = temps.FirstOrDefault(r => r.Name.Contains("Package") || r.Name.Contains("Tctl") || r.Name.Contains("Tdie"));
            return main?.RawValue ?? temps.Max(r => r.RawValue);
        }
    }

    /// <summary>Carga total da CPU em %.</summary>
    public float? CpuLoad => Rows.FirstOrDefault(r => r.HardwareType == HardwareType.Cpu
                                                   && r.Type == SensorType.Load
                                                   && r.Name == "CPU Total")?.RawValue;

    /// <summary>Consumo do pacote da CPU em W.</summary>
    public float? CpuPower => Rows.FirstOrDefault(r => r.HardwareType == HardwareType.Cpu
                                                    && r.Type == SensorType.Power
                                                    && r.Name.Contains("Package"))?.RawValue;

    /// <summary>
    /// Driver PawnIO (necessário para temperatura/consumo da CPU via MSR/SMN).
    /// A biblioteca procura exatamente esta chave de desinstalação.
    /// </summary>
    public static bool IsPawnIOInstalled
    {
        get
        {
            using var hklm = Microsoft.Win32.RegistryKey.OpenBaseKey(Microsoft.Win32.RegistryHive.LocalMachine,
                                                                     Microsoft.Win32.RegistryView.Registry64);
            using var key = hklm.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO");
            return key != null;
        }
    }

    public static bool IsElevated =>
        new System.Security.Principal.WindowsPrincipal(System.Security.Principal.WindowsIdentity.GetCurrent())
            .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);

    public enum CpuSensorProblem { None, NotElevated, PawnIOMissing, ReadFailed }

    /// <summary>Por que a temperatura/consumo da CPU não aparecem (o motivo mais provável primeiro).</summary>
    public CpuSensorProblem DiagnoseCpuSensors()
    {
        if (CpuTemperature != null) return CpuSensorProblem.None;
        if (!IsElevated) return CpuSensorProblem.NotElevated;
        if (!IsPawnIOInstalled) return CpuSensorProblem.PawnIOMissing;
        return CpuSensorProblem.ReadFailed;
    }

    public static string Explain(CpuSensorProblem p) => p switch
    {
        CpuSensorProblem.NotElevated =>
            "O InfoPC não está rodando como administrador, e o Windows só libera os sensores internos da CPU " +
            "nesse modo. Feche e abra de novo aceitando o pedido de administrador.",
        CpuSensorProblem.PawnIOMissing =>
            "Falta o driver PawnIO, que lê a temperatura e o consumo da CPU. Ele é gratuito, assinado e usado também " +
            "pelo LibreHardwareMonitor e pelo FanControl. Depois de instalar, feche e abra o InfoPC.",
        CpuSensorProblem.ReadFailed =>
            "O PawnIO está instalado, mas a leitura falhou. Se acabou de instalar, reinicie o PC. Se continuar, " +
            "confira se o antivírus não bloqueou o driver ou se a CPU é muito antiga/recente para a biblioteca de sensores.",
        _ => ""
    };

    public void Dispose()
    {
        if (_open) _computer.Close();
        foreach (var c in _laterOpen)
            try { c.Close(); } catch { } // ao fechar o programa; quem ainda está abrindo morre com o processo
    }
}
