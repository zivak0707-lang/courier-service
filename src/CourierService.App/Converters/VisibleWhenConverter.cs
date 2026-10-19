using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace CourierService.App.Converters;

/// <summary>
/// <c>true</c> → Visible, інакше → Collapsed. Також приховує елемент для порожнього рядка чи <c>null</c>.
/// Параметр «invert» обертає умову: показати, коли значення хибне (наприклад, текст «Немає записів»).
/// </summary>
public sealed class VisibleWhenConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool visible = value switch
        {
            bool flag => flag,
            string text => !string.IsNullOrWhiteSpace(text),
            null => false,
            _ => true,
        };

        if (parameter is "invert")
        {
            visible = !visible;
        }

        return visible ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException("Зворотне перетворення не підтримується.");
}
