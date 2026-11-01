using Avalonia;
using Avalonia.Controls;

namespace InfoPC.Linux;

/// <summary>
/// Página do menu. O conteúdo é montado em <see cref="Body"/> (já dentro de uma rolagem); <see cref="OnShown"/> e
/// <see cref="OnHidden"/> ligam e desligam o que é periódico, para uma página fora de vista não gastar CPU.
/// </summary>
public abstract class PageBase : UserControl
{
    public abstract string Title { get; }
    public virtual string Subtitle => "";

    protected StackPanel Body { get; } = new() { Spacing = 16, Margin = new Thickness(0, 0, 18, 24) };
    protected static Services S => Services.Current;

    protected PageBase()
    {
        Content = new ScrollViewer { Content = Body, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
    }

    public virtual void OnShown() { }
    public virtual void OnHidden() { }

    /// <summary>Encerramento da janela principal, inclusive para páginas fora de vista.</summary>
    public virtual void OnClosed() => OnHidden();

    /// <summary>A página mudou o próprio título ou subtítulo (ex.: a GPU escolhida): o cabeçalho se atualiza.</summary>
    public event Action? HeaderChanged;
    protected void RaiseHeaderChanged() => HeaderChanged?.Invoke();

    /// <summary>Executa na thread da tela (para continuações de tarefas em segundo plano).</summary>
    protected static void Post(Action a) => Avalonia.Threading.Dispatcher.UIThread.Post(a);
}
