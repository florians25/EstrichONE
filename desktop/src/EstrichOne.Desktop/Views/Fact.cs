using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace EstrichOne.Desktop.Views;

/// <summary>Beschriftung über Wert (wie die Faktenleiste im Web).</summary>
public sealed partial class Fact : StackPanel
{
    public static readonly DependencyProperty LabelProperty =
        DependencyProperty.Register(nameof(Label), typeof(string), typeof(Fact), new PropertyMetadata(string.Empty, (d, e) => ((Fact)d)._label.Text = (string)e.NewValue));

    public static readonly DependencyProperty ValueProperty =
        DependencyProperty.Register(nameof(Value), typeof(string), typeof(Fact), new PropertyMetadata(string.Empty, (d, e) => ((Fact)d)._value.Text = (string?)e.NewValue ?? "–"));

    private readonly TextBlock _label = new() { Style = (Style)Application.Current.Resources["Overline"] };
    private readonly TextBlock _value = new() { TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };

    public Fact()
    {
        Spacing = 2;
        Children.Add(_label);
        Children.Add(_value);
    }

    public string Label
    {
        get => (string)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public string Value
    {
        get => (string)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }
}
