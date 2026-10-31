using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform.Storage;

namespace InfoPC.Linux;

/// <summary>Diálogos simples. O seletor de arquivos do sistema depende do portal do desktop (xdg-desktop-portal); sem ele, pede o caminho em texto.</summary>
public static class Dialogs
{
    public static async Task<string?> AskTextAsync(Control owner, string title, string message, string initial = "")
    {
        if (TopLevel.GetTopLevel(owner) is not Window parent) return null;
        string? result = null;
        var box = new TextBox { Text = initial, MinWidth = 440 };
        var win = new Window { Title = title, Width = 560, Height = 210, CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var ok = Ui.Primary("OK", () => { result = box.Text?.Trim(); win.Close(); });
        var cancel = Ui.Outline("CANCELAR", () => win.Close());
        box.KeyDown += (_, e) => { if (e.Key == Avalonia.Input.Key.Enter) { result = box.Text?.Trim(); win.Close(); } };
        win.Content = new StackPanel
        {
            Margin = new Thickness(24), Spacing = 14,
            Children = { Ui.Text(message), box, Ui.HStack(10, ok, cancel) },
        };
        await win.ShowDialog(parent);
        return string.IsNullOrWhiteSpace(result) ? null : result;
    }

    public static async Task<bool> ConfirmAsync(Control owner, string title, string message, string yes = "CONTINUAR")
    {
        if (TopLevel.GetTopLevel(owner) is not Window parent) return false;
        bool answer = false;
        var win = new Window { Title = title, Width = 560, SizeToContent = SizeToContent.Height, CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        win.Content = new StackPanel
        {
            Margin = new Thickness(24), Spacing = 18,
            Children = { Ui.Text(message), Ui.HStack(10, Ui.Primary(yes, () => { answer = true; win.Close(); }), Ui.Outline("CANCELAR", () => win.Close())) },
        };
        await win.ShowDialog(parent);
        return answer;
    }

    /// <summary>Escolhe um arquivo: pelo seletor do sistema quando existe, senão pede o caminho.</summary>
    public static async Task<string?> PickFileAsync(Control owner, string title)
    {
        var sp = TopLevel.GetTopLevel(owner)?.StorageProvider;
        if (sp is { CanOpen: true })
        {
            try
            {
                var files = await sp.OpenFilePickerAsync(new FilePickerOpenOptions { Title = title, AllowMultiple = false });
                var f = files.FirstOrDefault();
                if (f != null) return f.TryGetLocalPath();
                return null;
            }
            catch { /* portal indisponível: cai no texto */ }
        }
        return await AskTextAsync(owner, title, "Digite ou cole o caminho completo do arquivo:");
    }
}
