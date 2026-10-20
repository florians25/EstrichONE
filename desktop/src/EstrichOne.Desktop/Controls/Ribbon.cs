using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace EstrichOne.Desktop.Controls;

/// <summary>Befehl im Ribbon; dieselbe Instanz nutzt die Befehlssuche in der Titelleiste.</summary>
public sealed record RibbonCommand(string Label, string Glyph, Action Execute, string? AccessKey = null, string? Description = null);

public sealed record RibbonGroup(string Label, IReadOnlyList<RibbonCommand> Commands);

/// <param name="IsContextual">Kontextregister (z. B. „Bauakte“): farbig markiert und nur sichtbar, wenn passend.</param>
public sealed record RibbonTab(string Key, string Header, IReadOnlyList<RibbonGroup> Groups, bool IsContextual = false);

/// <summary>
/// Ribbon aus nativen WinUI-Steuerelementen (docs/reengineering/06, Abschnitt 2): Register als SelectorBar,
/// Gruppen mit beschrifteten Schaltflächen, Alt-Tastenkürzel über AccessKey, Strg+F1 bzw. Doppelklick minimiert.
/// </summary>
public sealed partial class Ribbon : UserControl
{
    private readonly SelectorBar _tabs = new();
    private readonly StackPanel _groups = new() { Orientation = Orientation.Horizontal, Spacing = 0, Padding = new Thickness(8, 4, 8, 4) };
    private readonly ScrollViewer _content;
    private IReadOnlyList<RibbonTab> _all = [];
    private string? _context;

    public Ribbon()
    {
        _content = new ScrollViewer
        {
            Content = _groups, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            HorizontalScrollMode = ScrollMode.Auto, VerticalScrollMode = ScrollMode.Disabled, Height = 92,
        };
        var root = new Grid
        {
            Background = (Brush)Application.Current.Resources["RibbonBrush"],
            BorderBrush = (Brush)Application.Current.Resources["DividerBrush"],
            BorderThickness = new Thickness(0, 0, 0, 1),
        };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(_content, 1);
        root.Children.Add(_tabs);
        root.Children.Add(_content);
        Content = root;
        AutomationProperties.SetName(_tabs, "Menüband");
        _tabs.SelectionChanged += (_, _) => ShowSelected();
        _tabs.DoubleTapped += (_, _) => ToggleMinimized();
    }

    public bool IsMinimized => _content.Visibility == Visibility.Collapsed;

    public IEnumerable<RibbonCommand> AllCommands => _all.SelectMany(t => t.Groups).SelectMany(g => g.Commands);

    public void SetTabs(IReadOnlyList<RibbonTab> tabs)
    {
        _all = tabs;
        Rebuild(tabs.Count > 0 ? tabs[0].Key : null);
    }

    /// <summary>Kontextregister einblenden (Schlüssel) oder ausblenden (<c>null</c>); es wird gleich ausgewählt.</summary>
    public void ShowContext(string? key)
    {
        if (_context == key)
        {
            return;
        }

        _context = key;
        Rebuild(key ?? (_tabs.SelectedItem?.Tag as string));
    }

    public void ToggleMinimized() => _content.Visibility = IsMinimized ? Visibility.Visible : Visibility.Collapsed;

    private void Rebuild(string? selectKey)
    {
        _tabs.Items.Clear();
        foreach (var tab in _all.Where(t => !t.IsContextual || t.Key == _context))
        {
            var item = new SelectorBarItem { Text = tab.Header, Tag = tab.Key };
            if (tab.IsContextual)
            {
                item.Foreground = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 0x2E, 0x7D, 0x32));
                item.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
            }

            _tabs.Items.Add(item);
        }

        _tabs.SelectedItem = _tabs.Items.FirstOrDefault(i => (string)i.Tag == selectKey) ?? _tabs.Items.FirstOrDefault();
        ShowSelected();
    }

    private void ShowSelected()
    {
        _groups.Children.Clear();
        if (_tabs.SelectedItem?.Tag is not string key || _all.FirstOrDefault(t => t.Key == key) is not { } tab)
        {
            return;
        }

        foreach (var group in tab.Groups)
        {
            _groups.Children.Add(BuildGroup(group));
        }
    }

    private static Border BuildGroup(RibbonGroup group)
    {
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        foreach (var command in group.Commands)
        {
            buttons.Children.Add(BuildButton(command));
        }

        var label = new TextBlock
        {
            Text = group.Label, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 2, 0, 0),
            Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
        };
        var panel = new Grid { Padding = new Thickness(6, 0, 6, 0) };
        panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(label, 1);
        panel.Children.Add(buttons);
        panel.Children.Add(label);

        return new Border
        {
            Child = panel, BorderBrush = (Brush)Application.Current.Resources["DividerBrush"], BorderThickness = new Thickness(0, 0, 1, 0),
            Margin = new Thickness(0, 0, 4, 0),
        };
    }

    private static Button BuildButton(RibbonCommand command)
    {
        var content = new StackPanel { Spacing = 4, HorizontalAlignment = HorizontalAlignment.Center };
        content.Children.Add(new FontIcon { Glyph = command.Glyph, FontSize = 20 });
        content.Children.Add(new TextBlock
        {
            Text = command.Label, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, MaxWidth = 84, FontSize = 12,
        });
        var button = new Button
        {
            Content = content, MinWidth = 64, MinHeight = 62, Padding = new Thickness(6, 4, 6, 4), VerticalAlignment = VerticalAlignment.Top,
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent), BorderThickness = new Thickness(0),
        };
        AutomationProperties.SetName(button, command.Label);
        if (command.Description is { } description)
        {
            ToolTipService.SetToolTip(button, description);
            AutomationProperties.SetHelpText(button, description);
        }

        if (command.AccessKey is { } key)
        {
            button.AccessKey = key;
        }

        button.Click += (_, _) => command.Execute();
        return button;
    }
}
