using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace InfoPC.Linux;

/// <summary>Cartão de saúde (disco, bateria, energia, rede): título, selo, explicação e métricas. O mesmo desenho da edição Windows.</summary>
public static class HealthCardView
{
    public static Border Create(DiskHealthCard c, params Control[] extra)
    {
        var head = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        var title = Ui.VStack(2, new TextBlock { Text = c.Title, FontSize = 16, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap });
        if (c.Meta.Length > 0) title.Children.Add(Ui.Muted(c.Meta).Cls("mono"));
        Grid.SetColumn(title, 0);
        var badge = Ui.Badge(c.VerdictText, c.IsAlert);
        Grid.SetColumn(badge, 1);
        head.Children.Add(title);
        head.Children.Add(badge);

        var body = Ui.VStack(12, head);
        if (c.Reasons.Length > 0) body.Children.Add(Ui.Text(c.Reasons).Cls("sub"));
        foreach (var e in extra) body.Children.Add(e);
        if (c.Metrics.Count > 0) body.Children.Add(Ui.KeyValues(c.Metrics));
        return Ui.Card(body);
    }
}
