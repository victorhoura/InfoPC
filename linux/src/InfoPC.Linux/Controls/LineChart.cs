using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace InfoPC.Linux;

public sealed class ChartSeries
{
    public string Name { get; init; } = "";
    public float?[] Values { get; set; } = Array.Empty<float?>();
    /// <summary>Chave do pincel no tema ("Fg", "Muted"...).</summary>
    public string BrushKey { get; init; } = "Fg";
    public bool Dashed { get; init; }
    public bool Fill { get; init; }
}

/// <summary>Gráfico de linhas dos últimos N segundos (o dado novo entra pela direita), com grade e escala automática.</summary>
public sealed class LineChart : Control
{
    public List<ChartSeries> Series { get; } = new();
    public string Unit { get; set; } = "";
    /// <summary>Quantos segundos cabem na largura.</summary>
    public int Span { get; set; } = 300;
    public double? FixedMin { get; set; }
    public double? FixedMax { get; set; }
    /// <summary>Largura mínima da escala (evita que um gráfico quase vazio mostre "20 °C a 21 °C").</summary>
    public double MinSpan { get; set; } = 0;

    public LineChart() { MinHeight = 140; }

    public void Refresh() => InvalidateVisual();

    public override void Render(DrawingContext ctx)
    {
        base.Render(ctx);
        double w = Bounds.Width, h = Bounds.Height;
        if (w < 80 || h < 40) return;
        const double left = 46, bottom = 6, top = 8;
        double pw = w - left - 4, ph = h - top - bottom;

        var all = Series.SelectMany(s => s.Values).Where(v => v != null).Select(v => (double)v!.Value).ToList();
        double lo = FixedMin ?? 0;
        double hi = FixedMax ?? Nice(all.Count > 0 ? all.Max() * 1.1 : 10, lo);
        if (FixedMax == null && hi < lo + MinSpan) hi = lo + MinSpan;
        if (hi <= lo) hi = lo + 1;

        var line = Ui.Brush(this, "Line");
        var muted = Ui.Brush(this, "Muted");
        var gridPen = new Pen(line, 1);
        var tf = Typeface.Default;

        for (int i = 0; i <= 4; i++)
        {
            double y = top + ph * i / 4.0;
            ctx.DrawLine(gridPen, new Point(left, y), new Point(left + pw, y));
            double v = hi - (hi - lo) * i / 4.0;
            var ft = new FormattedText($"{v:0.#}{Unit}", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, tf, 10, muted);
            ctx.DrawText(ft, new Point(left - 6 - ft.Width, y - ft.Height / 2));
        }

        double step = Span > 1 ? pw / (Span - 1) : pw;
        foreach (var s in Series)
        {
            var vals = s.Values;
            if (vals.Length < 2) continue;
            var brush = Ui.Brush(this, s.BrushKey);
            var geo = new StreamGeometry();
            Point? first = null, last = null;
            using (var g = geo.Open())
            {
                bool open = false;
                for (int i = 0; i < vals.Length; i++)
                {
                    if (vals[i] is not float v) { if (open) { g.EndFigure(false); open = false; } continue; }
                    double x = left + pw - (vals.Length - 1 - i) * step;
                    if (x < left) continue;
                    double y = top + ph - (Math.Clamp(v, lo, hi) - lo) / (hi - lo) * ph;
                    var p = new Point(x, y);
                    if (!open) { g.BeginFigure(p, false); open = true; first ??= p; }
                    else g.LineTo(p);
                    last = p;
                }
                if (open) g.EndFigure(false);
            }
            if (first == null || last == null) continue;

            if (s.Fill)
            {
                var area = new StreamGeometry();
                using (var g = area.Open())
                {
                    g.BeginFigure(new Point(first.Value.X, top + ph), true);
                    for (int i = 0; i < vals.Length; i++)
                    {
                        if (vals[i] is not float v) continue;
                        double x = left + pw - (vals.Length - 1 - i) * step;
                        if (x < left) continue;
                        g.LineTo(new Point(x, top + ph - (Math.Clamp(v, lo, hi) - lo) / (hi - lo) * ph));
                    }
                    g.LineTo(new Point(last.Value.X, top + ph));
                    g.EndFigure(true);
                }
                ctx.DrawGeometry(new SolidColorBrush(((ISolidColorBrush)brush).Color, 0.10), null, area);
            }
            var pen = new Pen(brush, 1.8, s.Dashed ? new DashStyle(new double[] { 4, 3 }, 0) : null, lineJoin: PenLineJoin.Round);
            ctx.DrawGeometry(null, pen, geo);
        }
    }

    /// <summary>Arredonda o topo da escala para um valor "redondo" (5, 10, 20, 50, 100...).</summary>
    private static double Nice(double v, double lo)
    {
        double span = Math.Max(v - lo, 1);
        double mag = Math.Pow(10, Math.Floor(Math.Log10(span)));
        foreach (var m in new[] { 1, 2, 2.5, 5, 10 })
            if (span <= m * mag) return lo + m * mag;
        return lo + 10 * mag;
    }
}
