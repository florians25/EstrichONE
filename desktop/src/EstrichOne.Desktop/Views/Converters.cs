using Microsoft.UI.Xaml;

namespace EstrichOne.Desktop.Views;

/// <summary>Hilfsfunktionen für x:Bind-Ausdrücke.</summary>
public static class Show
{
    public static Visibility If(bool value) => value ? Visibility.Visible : Visibility.Collapsed;

    public static Visibility IfNot(bool value) => value ? Visibility.Collapsed : Visibility.Visible;

    public static Visibility IfText(string? value) => string.IsNullOrWhiteSpace(value) ? Visibility.Collapsed : Visibility.Visible;

    public static Visibility IfEmpty(int count) => count == 0 ? Visibility.Visible : Visibility.Collapsed;

    public static Visibility IfAny(int count) => count > 0 ? Visibility.Visible : Visibility.Collapsed;
}
