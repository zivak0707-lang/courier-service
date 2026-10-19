using System.Globalization;
using System.Windows.Data;
using CourierService.Core.Enums;
using CourierService.Core.Validation;

namespace CourierService.App.Converters;

/// <summary>
/// Основа перетворювачів лише для показу (зворотне перетворення не потрібне).
/// </summary>
public abstract class DisplayConverterBase : IValueConverter
{
    /// <inheritdoc />
    public abstract object Convert(object? value, Type targetType, object? parameter, CultureInfo culture);

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException("Зворотне перетворення не підтримується.");
}

/// <summary>Значення перелічення → назва українською («Courier» → «Кур'єр»).</summary>
public sealed class EnumDisplayConverter : DisplayConverterBase
{
    /// <inheritdoc />
    public override object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        UserRole role => role.ToDisplayName(),
        TransportType transport => transport.ToDisplayName(),
        ClientType clientType => clientType.ToDisplayName(),
        RiverBank bank => bank.ToDisplayName(),
        OrderStatus status => status.ToDisplayName(),
        _ => value?.ToString() ?? string.Empty,
    };
}

/// <summary>Коефіцієнт у відсотках → «1,15».</summary>
public sealed class CoefficientConverter : DisplayConverterBase
{
    /// <inheritdoc />
    public override object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is int percent ? InputParsing.FormatCoefficient(percent) : string.Empty;
}

/// <summary>Вага в грамах → «2 кг», «0,5 кг».</summary>
public sealed class KilogramsConverter : DisplayConverterBase
{
    /// <inheritdoc />
    public override object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is int grams ? InputParsing.FormatKilograms(grams) + " кг" : string.Empty;
}

/// <summary>Телефон +380XXXXXXXXX → «+380 (50) 123-45-67».</summary>
public sealed class PhoneConverter : DisplayConverterBase
{
    /// <inheritdoc />
    public override object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        PhoneNumber.Format(value as string);
}
