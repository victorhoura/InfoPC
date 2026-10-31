using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace InfoPC.Linux;

/// <summary>Aviso no canto da tela quando um dispositivo entra ou sai. Fica uns segundos e some sozinho; um clique fecha.</summary>
public sealed class ToastWindow : Window
{
    private static ToastWindow? _current;

    public static void Show(DeviceChange c)
    {
        _current?.Close();
        var w = new ToastWindow(c);
        _current = w;
        w.Show();
    }

    private ToastWindow(DeviceChange c)
    {
        SystemDecorations = SystemDecorations.None;
        Topmost = true; ShowInTaskbar = false; CanResize = false; ShowActivated = false;
        Width = 340; SizeToContent = SizeToContent.Height;
        Background = Brushes.Transparent;
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
        var bar = new Border { Width = 4, CornerRadius = new CornerRadius(2), Background = c.Added ? Brushes.White : new SolidColorBrush(Color.FromRgb(140, 140, 140)) };
        var text = new StackPanel { Spacing = 2, Margin = new Thickness(12, 0, 0, 0) };
        text.Children.Add(new TextBlock { Text = c.Headline.ToUpperInvariant(), FontSize = 10.5, FontWeight = FontWeight.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(150, 150, 150)) });
        text.Children.Add(new TextBlock { Text = c.Title, FontSize = 14, FontWeight = FontWeight.SemiBold, Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap });
        text.Children.Add(new TextBlock { Text = c.Detail, FontSize = 12, Foreground = new SolidColorBrush(Color.FromRgb(170, 170, 170)), TextWrapping = TextWrapping.Wrap });
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        Grid.SetColumn(text, 1);
        row.Children.Add(bar); row.Children.Add(text);
        Content = new Border { Background = new SolidColorBrush(Color.FromArgb(240, 14, 14, 14)), BorderBrush = new SolidColorBrush(Color.FromRgb(58, 58, 58)), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(14), Child = row };
        PointerPressed += (_, _) => Close();
        Opened += (_, _) => Place();
        var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        t.Tick += (_, _) => { t.Stop(); Close(); };
        t.Start();
        Closed += (_, _) => { t.Stop(); if (_current == this) _current = null; };
    }

    private void Place()
    {
        var screen = Screens.Primary;
        if (screen == null) return;
        var wa = screen.WorkingArea;
        double s = screen.Scaling;
        int w = (int)(Width * s), h = (int)(Bounds.Height * s);
        Position = new PixelPoint(wa.Right - w - 20, wa.Bottom - h - 20);
    }
}
