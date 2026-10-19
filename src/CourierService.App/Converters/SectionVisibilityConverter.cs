using System.Globalization;
using System.Windows;
using System.Windows.Data;
using CourierService.App.Navigation;
using CourierService.Core.Enums;

namespace CourierService.App.Converters;

/// <summary>
/// Показує пункт меню, лише якщо розділ (параметр) доступний ролі поточного користувача (значення).
/// </summary>
public sealed class SectionVisibilityConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        parameter is AppSection section && SectionAccess.IsAllowed(section, value as UserRole?)
            ? Visibility.Visible
            : Visibility.Collapsed;

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException("Зворотне перетворення не підтримується.");
}
