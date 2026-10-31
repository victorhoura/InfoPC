using Avalonia;
using Avalonia.Controls;

namespace InfoPC.Linux;

/// <summary>Linha de status das páginas: só aparece quando tem texto (senão deixaria um buraco entre os blocos).</summary>
public sealed class StatusText : TextBlock
{
    protected override Type StyleKeyOverride => typeof(TextBlock);

    public StatusText()
    {
        IsVisible = false;
        TextWrapping = Avalonia.Media.TextWrapping.Wrap;
        FontSize = 12.5;
        Classes.Add("sub");
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TextProperty) IsVisible = !string.IsNullOrEmpty(Text);
    }
}
