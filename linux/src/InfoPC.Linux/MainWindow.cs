using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace InfoPC.Linux;

public sealed record NavEntry(string Key, string Group, string Title, Func<PageBase> Create);

/// <summary>Menu lateral por categorias (as mesmas da edição Windows: HARDWARE, JOGOS, DESEMPENHO, FERRAMENTAS, MAKER) e área da página.</summary>
public sealed class MainWindow : Window
{
    private readonly List<NavEntry> _entries = Nav.Entries();
    private readonly Dictionary<string, PageBase> _pages = new();
    private readonly Dictionary<string, RadioButton> _buttons = new();
    private readonly ContentControl _host = new();
    private readonly TextBlock _title = new() { };
    private readonly TextBlock _subtitle = new() { };
    private readonly TextBlock _errorNotice = Ui.Note("");
    private readonly Image _logo = new() { Width = 34, Height = 34 };
    private PageBase? _current;
    public string? CurrentKey { get; private set; }

    /// <summary>Aviso de uma ação que falhou nesta página ("" sem aviso).</summary>
    public string ErrorNotice => _errorNotice.IsVisible ? _errorNotice.Text ?? "" : "";

    public MainWindow()
    {
        Title = "InfoPC";
        Width = 1240; Height = 800; MinWidth = 980; MinHeight = 620;
        TrySetIcon();
        Content = Build();
        Opened += async (_, _) =>
        {
            ShowLogo();
            Services.Current.LoadSettings();
            if (AppSettings.FolderNotice is { } notice) _ = Dialogs.ConfirmAsync(this, "Pasta de dados", notice, "ENTENDI");
            try { await Services.Current.StartAsync(); }
            catch (Exception ex)
            {
                await Dialogs.ConfirmAsync(this, "Leitura de hardware", "Não consegui concluir a detecção: " + ex.Message +
                    ". Os detalhes foram registrados em erros.log na pasta de dados.", "ENTENDI");
            }
        };
        Closing += (_, _) => Services.Current.Save();
        Closed += (_, _) =>
        {
            foreach (var page in _pages.Values)
            {
                try { page.OnClosed(); }
                catch (Exception ex) { CrashLog.Write("Fechamento de página", ex); }
            }
            CrashLog.Recovered -= ShowErrorNotice;
            Services.Current.Stop();
        };
        CrashLog.Recovered += ShowErrorNotice;
        Services.Current.DeviceChanged += c =>
        {
            if (Services.Current.Settings.DeviceToasts) ToastWindow.Show(c);
        };
        Navigate(_entries[0].Key);
    }

    private void ShowErrorNotice(string logFile)
    {
        _errorNotice.Text = "Uma ação desta página falhou, mas o programa continuou aberto. Detalhes em " + logFile;
        _errorNotice.IsVisible = true;
    }

    private void TrySetIcon()
    {
        try { Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://InfoPC/Assets/logo.png"))); } catch { }
    }

    private void ShowLogo()
    {
        try
        {
            string file = Services.Current.Settings.LightTheme ? "logo-dark.png" : "logo.png";
            _logo.Source = new Bitmap(AssetLoader.Open(new Uri("avares://InfoPC/Assets/" + file)));
        }
        catch { }
    }

    private Control Build()
    {
        var root = new Grid { ColumnDefinitions = new ColumnDefinitions("248,*") };

        // ---------- menu lateral ----------
        var nav = new StackPanel { Margin = new Thickness(10, 0, 10, 8) };
        string? group = null;
        StackPanel? items = null;
        foreach (var e in _entries)
        {
            if (e.Group != group)
            {
                group = e.Group;
                if (group.Length > 0)
                {
                    var toggle = new ToggleButton { Content = group, IsChecked = true };
                    toggle.Classes.Add("group");
                    items = new StackPanel { Margin = new Thickness(8, 0, 0, 4) };
                    var panel = items;
                    toggle.IsCheckedChanged += (_, _) => panel.IsVisible = toggle.IsChecked == true;
                    nav.Children.Add(toggle);
                    nav.Children.Add(items);
                }
                else items = null;
            }
            var rb = new RadioButton { Content = e.Title, GroupName = "nav", Tag = e.Key };
            rb.Classes.Add("nav");
            rb.IsCheckedChanged += (_, _) => { if (rb.IsChecked == true) Navigate((string)rb.Tag!); };
            _buttons[e.Key] = rb;
            (items ?? nav).Children.Add(rb);
        }

        var brand = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Margin = new Thickness(18, 20, 18, 14) };
        brand.Children.Add(_logo);
        brand.Children.Add(new TextBlock { Text = "InfoPC", FontSize = 20, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center });

        var themeBtn = Ui.Outline("TEMA CLARO / ESCURO", ToggleTheme).Small();
        themeBtn.HorizontalAlignment = HorizontalAlignment.Stretch;
        themeBtn.HorizontalContentAlignment = HorizontalAlignment.Center;
        var footer = Ui.VStack(8,
            themeBtn,
            Ui.Muted($"v{AppInfo.Version} · baseado no RESET EXPLODE").Cls("label").WithMargin(2, 0, 0, 0));
        footer.Margin = new Thickness(14, 8, 14, 16);

        var side = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto") };
        Grid.SetRow(brand, 0);
        var scroll = new ScrollViewer { Content = nav, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Grid.SetRow(scroll, 1);
        Grid.SetRow(footer, 2);
        side.Children.Add(brand); side.Children.Add(scroll); side.Children.Add(footer);

        var sideBorder = new Border { Child = side, BorderThickness = new Thickness(0, 0, 1, 0) };
        sideBorder.Classes.Add("line");
        sideBorder[!Border.BackgroundProperty] = new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("Panel");
        Grid.SetColumn(sideBorder, 0);

        // ---------- área da página ----------
        _title.Classes.Add("h1");
        _subtitle.Classes.Add("sub");
        _errorNotice.IsVisible = false;
        var header = Ui.VStack(2, _title, _subtitle, _errorNotice);
        header.Margin = new Thickness(0, 0, 0, 18);
        var area = new Grid { RowDefinitions = new RowDefinitions("Auto,*"), Margin = new Thickness(30, 26, 12, 0) };
        Grid.SetRow(header, 0);
        Grid.SetRow(_host, 1);
        area.Children.Add(header); area.Children.Add(_host);
        Grid.SetColumn(area, 1);

        root.Children.Add(sideBorder);
        root.Children.Add(area);
        return root;
    }

    public void Navigate(string key)
    {
        var entry = _entries.FirstOrDefault(e => e.Key == key);
        if (entry == null) return;
        if (_current != null && CurrentKey == key) return;
        if (_current != null) { _current.OnHidden(); _current.HeaderChanged -= RefreshHeader; }
        if (!_pages.TryGetValue(key, out var page)) _pages[key] = page = entry.Create();
        _current = page;
        CurrentKey = key;
        _errorNotice.IsVisible = false;
        page.HeaderChanged += RefreshHeader;
        RefreshHeader();
        _host.Content = page;
        if (_buttons.TryGetValue(key, out var rb) && rb.IsChecked != true) rb.IsChecked = true;
        page.OnShown();
    }

    private void RefreshHeader()
    {
        if (_current == null) return;
        _title.Text = _current.Title;
        _subtitle.Text = _current.Subtitle;
        _subtitle.IsVisible = _current.Subtitle.Length > 0;
    }

    public PageBase? Page(string key) => _pages.GetValueOrDefault(key);
    public IReadOnlyList<NavEntry> Entries => _entries;

    private void ToggleTheme()
    {
        var s = Services.Current.Settings;
        s.LightTheme = !s.LightTheme;
        Services.ApplyTheme(s.LightTheme);
        ShowLogo();
        Services.Current.Save();
    }
}
