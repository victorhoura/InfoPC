using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace InfoPC.Linux;

/// <summary>Contorno dos blocos que cada thread está renderizando agora, por cima da imagem do benchmark (que usa Stretch.Uniform).</summary>
public sealed class BucketOverlay : Control
{
    public Func<IReadOnlyList<(int X, int Y, int W, int H)>>? Provider { get; set; }

    public override void Render(DrawingContext ctx)
    {
        base.Render(ctx);
        var blocks = Provider?.Invoke();
        if (blocks == null || blocks.Count == 0) return;
        double scale = Math.Min(Bounds.Width / BenchRenderer.Width, Bounds.Height / BenchRenderer.Height);
        if (scale <= 0) return;
        double ox = (Bounds.Width - BenchRenderer.Width * scale) / 2, oy = (Bounds.Height - BenchRenderer.Height * scale) / 2;
        var pen = new Pen(Brushes.White, 1.5);
        foreach (var (x, y, w, h) in blocks)
            ctx.DrawRectangle(null, pen, new Rect(ox + x * scale, oy + y * scale, w * scale, h * scale));
    }
}
