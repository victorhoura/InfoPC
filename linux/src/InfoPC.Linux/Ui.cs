using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;

namespace InfoPC.Linux;

/// <summary>Montagem de telas em código: cada helper devolve um controle já com as classes de estilo do tema (Styles.axaml).</summary>
public static class Ui
{
    public static TextBlock Text(string text, params string[] classes)
    {
        var t = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap };
        foreach (var c in classes) t.Classes.Add(c);
        return t;
    }

    public static SelectableTextBlock Selectable(string text, params string[] classes)
    {
        var t = new SelectableTextBlock { Text = text, TextWrapping = TextWrapping.Wrap };
        foreach (var c in classes) t.Classes.Add(c);
        return t;
    }

    public static TextBlock Label(string text) => Text(text.ToUpperInvariant(), "label");
    public static TextBlock Sub(string text) => Text(text, "sub");
    public static TextBlock Muted(string text) => Text(text, "muted");

    public static StackPanel VStack(double spacing = 8, params Control[] kids)
    {
        var p = new StackPanel { Orientation = Orientation.Vertical, Spacing = spacing };
        p.Children.AddRange(kids);
        return p;
    }

    public static StackPanel HStack(double spacing = 8, params Control[] kids)
    {
        var p = new StackPanel { Orientation = Orientation.Horizontal, Spacing = spacing };
        p.Children.AddRange(kids);
        return p;
    }

    public static WrapPanel Wrap(params Control[] kids)
    {
        var p = new WrapPanel { Orientation = Orientation.Horizontal };
        p.Children.AddRange(kids);
        return p;
    }

    /// <summary>Cartão com título opcional (em caixa alta e cinza) e o conteúdo logo abaixo.</summary>
    public static Border Card(Control content, string? label = null, double padding = 20)
    {
        Control inner = content;
        if (label != null) inner = VStack(12, Label(label), content);
        var b = new Border { Child = inner, Padding = new Thickness(padding) };
        b.Classes.Add("card");
        return b;
    }

    public static Button Primary(string text, Action onClick, string? tip = null) => MakeButton(text, onClick, "primary", tip);
    public static Button Outline(string text, Action onClick, string? tip = null) => MakeButton(text, onClick, "outline", tip);

    private static Button MakeButton(string text, Action onClick, string cls, string? tip)
    {
        var b = new Button { Content = text };
        b.Classes.Add(cls);
        b.Click += (_, _) => onClick();
        if (tip != null) ToolTip.SetTip(b, tip);
        return b;
    }

    public static Button Small(this Button b) { b.Classes.Add("small"); return b; }

    /// <summary>Selo de saúde: contorno quando está tudo bem, preenchido quando pede atenção.</summary>
    public static Border Badge(string text, bool alert)
    {
        var tb = new TextBlock { Text = text, FontSize = 11, FontWeight = FontWeight.SemiBold, LetterSpacing = 0.6 };
        var b = new Border { Child = tb, VerticalAlignment = VerticalAlignment.Center };
        b.Classes.Add("badge");
        if (alert) b.Classes.Add("alert");
        return b;
    }

    /// <summary>Tabela de duas colunas (nome em cinza, valor selecionável).</summary>
    public static Control KeyValues(IEnumerable<KeyValuePair<string, string>> rows, double keyWidth = 230)
    {
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions($"{keyWidth},*"), RowSpacing = 4 };
        int i = 0;
        foreach (var kv in rows)
        {
            g.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var k = Muted(kv.Key); k.FontSize = 12.5; k.Margin = new Thickness(0, 0, 12, 0);
            var v = Selectable(kv.Value); v.FontSize = 12.5;
            Grid.SetRow(k, i); Grid.SetRow(v, i); Grid.SetColumn(v, 1);
            g.Children.Add(k); g.Children.Add(v);
            i++;
        }
        return g;
    }

    /// <summary>Árvore de informações (Sistema, GPU) com todos os níveis abertos.</summary>
    public static TreeView InfoTree(IEnumerable<InfoNode> nodes)
    {
        var list = nodes.ToList();
        var depth = new Dictionary<InfoNode, int>(ReferenceEqualityComparer.Instance);
        void Walk(InfoNode n, int d) { depth[n] = d; foreach (var c in n.Children) Walk(c, d + 1); }
        foreach (var n in list) Walk(n, 0);

        var tv = new TreeView
        {
            ItemsSource = list,
            ItemTemplate = new FuncTreeDataTemplate<InfoNode>(
                (n, _) =>
                {
                    var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Margin = new Thickness(0, 2) };
                    bool title = n.IsGroup && n.Value.Length == 0;
                    var head = Text(n.Header, title ? "label" : "muted");
                    if (!title) { head.FontSize = 12.5; head.Width = Math.Max(140, 300 - 20 * (depth.TryGetValue(n, out var d) ? d : 0)); }
                    row.Children.Add(head);
                    if (n.Value.Length > 0) { var v = Selectable(n.Value); v.FontSize = 12.5; row.Children.Add(v); }
                    return row;
                },
                n => n.Children),
            Background = Brushes.Transparent,
        };
        return tv;
    }

    public static ProgressBar Bar(double percent, double width = double.NaN)
    {
        var p = new ProgressBar { Minimum = 0, Maximum = 100, Value = Math.Clamp(percent, 0, 100) };
        if (!double.IsNaN(width)) p.Width = width;
        return p;
    }

    /// <summary>Opções em linha (estilo "chip"); uma só fica marcada.</summary>
    public static WrapPanel Chips(string group, IReadOnlyList<string> options, int selected, Action<int> changed)
    {
        var w = new WrapPanel();
        for (int i = 0; i < options.Count; i++)
        {
            int idx = i;
            var rb = new RadioButton { Content = options[i], GroupName = group, IsChecked = i == selected };
            rb.Classes.Add("chip");
            rb.IsCheckedChanged += (_, _) => { if (rb.IsChecked == true) changed(idx); };
            w.Children.Add(rb);
        }
        return w;
    }

    public static Control Separator()
    {
        var b = new Border { Height = 1, Margin = new Thickness(0, 4) };
        b.Classes.Add("line");
        b.BorderThickness = new Thickness(0, 1, 0, 0);
        return b;
    }

    /// <summary>Texto em cinza, quebrando linha, para avisos e explicações.</summary>
    public static TextBlock Note(string text) { var t = Sub(text); t.FontSize = 12.5; return t; }

    public static T WithMargin<T>(this T c, double left, double top, double right, double bottom) where T : Control
    {
        c.Margin = new Thickness(left, top, right, bottom);
        return c;
    }

    public static T Cls<T>(this T c, params string[] classes) where T : StyledElement
    {
        foreach (var x in classes) c.Classes.Add(x);
        return c;
    }

    /// <summary>Linha de métrica grande: rótulo pequeno em cima, valor em fonte monoespaçada embaixo.</summary>
    public static StackPanel Metric(string label, TextBlock value)
    {
        value.Classes.Add("big");
        return VStack(2, Label(label), value);
    }

    public static IBrush Brush(Control c, string key, IBrush? fallback = null)
    {
        if (c.TryFindResource(key, c.ActualThemeVariant, out var v) && v is IBrush b) return b;
        return fallback ?? Brushes.Gray;
    }

    public static string Num0(float? v, string unit) => v is float x ? $"{x:0}{unit}" : "—";
    public static string Num1(float? v, string unit) => v is float x ? $"{x:0.#}{unit}" : "—";
}
