using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;

namespace InfoPC.Linux;

/// <summary>Todos os sensores do kernel em tempo real, agrupados por componente, com busca e filtro por tipo.</summary>
public sealed class SensorsPage : PageBase
{
    public override string Title => "Sensores";
    public override string Subtitle => "Temperatura, carga, clock, consumo, tensão e ventoinhas. Atualiza a cada segundo; mínimo e máximo valem desde que o programa abriu.";

    private static readonly (string Label, SensorKind[]? Kinds)[] Filters =
    {
        ("Todos", null), ("Temperatura", new[] { SensorKind.Temperature }), ("Carga", new[] { SensorKind.Load, SensorKind.Level }),
        ("Clock", new[] { SensorKind.Clock }), ("Consumo", new[] { SensorKind.Power, SensorKind.Current, SensorKind.Energy }),
        ("Tensão", new[] { SensorKind.Voltage }), ("Ventoinhas", new[] { SensorKind.Fan, SensorKind.Control }),
        ("Memória e dados", new[] { SensorKind.Data, SensorKind.SmallData }), ("Taxas", new[] { SensorKind.Throughput }),
    };

    private readonly TextBox _search = new() { Watermark = "Buscar sensor ou hardware…", MaxWidth = 380, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly StackPanel _list = new() { Spacing = 14 };
    private readonly StackPanel _notes = new() { Spacing = 8 };
    private int _filter;
    private int _builtFor = -1;

    public SensorsPage()
    {
        _search.TextChanged += (_, _) => Rebuild();
        Body.Children.Add(_search);
        Body.Children.Add(Ui.Chips("sensorfilter", Filters.Select(f => f.Label).ToList(), 0, i => { _filter = i; Rebuild(); }));
        Body.Children.Add(_notes);
        Body.Children.Add(_list);
    }

    public override void OnShown()
    {
        S.Hub.Added -= OnAdded; S.Hub.Added += OnAdded;
        S.Loaded -= OnLoaded; S.Loaded += OnLoaded;
        S.Tick -= UpdateNotes; S.Tick += UpdateNotes;
        Rebuild();
        UpdateNotes();
    }

    public override void OnHidden() { S.Hub.Added -= OnAdded; S.Loaded -= OnLoaded; S.Tick -= UpdateNotes; }

    private void OnAdded(IReadOnlyList<SensorReading> _) => Rebuild();
    private void OnLoaded() { S.Hub.Added -= OnAdded; S.Hub.Added += OnAdded; Rebuild(); }

    private void UpdateNotes()
    {
        _notes.Children.Clear();
        if (S.Hub.Rows.Count > 0 && S.Hub.CpuTemperature == null)
            _notes.Children.Add(Ui.Card(Ui.Text("Temperatura da CPU: " + S.Hub.DescribeCpuTempProblem()).Cls("sub")));
        if (S.Hub.Rapl?.Blocked == true)
            _notes.Children.Add(Ui.Card(Ui.Text("Consumo da CPU (RAPL): o kernel só deixa o administrador ler (proteção contra o ataque PLATYPUS). Os outros sensores não precisam de root.").Cls("sub")));
    }

    private void Rebuild()
    {
        _list.Children.Clear();
        string q = (_search.Text ?? "").Trim();
        var kinds = Filters[_filter].Kinds;
        var rows = S.Hub.Rows.Where(r => kinds == null || kinds.Contains(r.Kind))
                             .Where(r => q.Length == 0 || r.Name.Contains(q, StringComparison.OrdinalIgnoreCase) || r.Hardware.Contains(q, StringComparison.OrdinalIgnoreCase))
                             .ToList();
        if (rows.Count == 0)
        {
            _list.Children.Add(Ui.Muted(S.Ready ? "Nenhum sensor para este filtro." : "Detectando o hardware…"));
            return;
        }
        foreach (var grp in rows.GroupBy(r => r.Hardware).OrderBy(g => g.First().HwKind).ThenBy(g => g.Key, StringComparer.Ordinal))
        {
            var table = new Grid { ColumnDefinitions = new ColumnDefinitions("110,*,120,110,110"), RowSpacing = 4 };
            AddHeader(table);
            int i = 1;
            foreach (var r in grp.OrderBy(r => r.Order).ThenBy(r => r.Name, StringComparer.Ordinal))
            {
                table.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                var cat = Ui.Muted(r.Category); cat.FontSize = 12;
                var name = new TextBlock { Text = r.Name, FontSize = 12.5, TextTrimming = TextTrimming.CharacterEllipsis };
                var val = Mono(r, nameof(SensorReading.Value), true);
                var min = Mono(r, nameof(SensorReading.Min), false);
                var max = Mono(r, nameof(SensorReading.Max), false);
                Place(table, i, cat, name, val, min, max);
                i++;
            }
            _list.Children.Add(Ui.Card(table, grp.Key));
        }
        _builtFor = rows.Count;
    }

    private static void AddHeader(Grid g)
    {
        g.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        string[] heads = { "Tipo", "Sensor", "Valor", "Mín.", "Máx." };
        for (int c = 0; c < heads.Length; c++)
        {
            var t = Ui.Label(heads[c]);
            if (c >= 2) t.TextAlignment = TextAlignment.Right;
            Grid.SetColumn(t, c); Grid.SetRow(t, 0);
            g.Children.Add(t);
        }
    }

    private static TextBlock Mono(SensorReading r, string prop, bool strong)
    {
        var t = new TextBlock { FontFamily = new FontFamily("DejaVu Sans Mono, Liberation Mono, monospace"), FontSize = 12.5, TextAlignment = TextAlignment.Right, DataContext = r };
        if (!strong) t.Classes.Add("muted");
        else t.FontWeight = FontWeight.SemiBold;
        t[!TextBlock.TextProperty] = new Binding(prop);
        return t;
    }

    private static void Place(Grid g, int row, params Control[] cells)
    {
        for (int c = 0; c < cells.Length; c++)
        {
            Grid.SetRow(cells[c], row); Grid.SetColumn(cells[c], c);
            g.Children.Add(cells[c]);
        }
    }
}
