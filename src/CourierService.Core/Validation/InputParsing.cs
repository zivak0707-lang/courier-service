using System.Globalization;

namespace CourierService.Core.Validation;

/// <summary>
/// Розбір чисел, введених користувачем: кома або крапка як десятковий роздільник,
/// пробіли між розрядами, «грн» наприкінці суми.
/// </summary>
public static class InputParsing
{
    private static readonly CultureInfo Ukrainian = CultureInfo.GetCultureInfo("uk-UA");

    /// <summary>
    /// Розбирає десяткове число з обмеженою кількістю знаків після коми.
    /// </summary>
    /// <param name="text">Введений текст («1,15», «1.15», «1 234,5»).</param>
    /// <param name="maxDecimals">Найбільша допустима кількість знаків після коми.</param>
    /// <param name="value">Число.</param>
    /// <returns><c>true</c>, якщо текст — коректне число.</returns>
    public static bool TryParseDecimal(string? text, int maxDecimals, out decimal value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        string normalized = text
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace(',', '.');

        if (!decimal.TryParse(normalized, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture, out value))
        {
            return false;
        }

        // «1,155» для коефіцієнта з двома знаками — помилка, а не мовчазне округлення
        return decimal.Round(value, maxDecimals) == value;
    }

    /// <summary>
    /// Розбирає ціле число.
    /// </summary>
    /// <param name="text">Введений текст.</param>
    /// <param name="value">Число.</param>
    /// <returns><c>true</c>, якщо текст — коректне ціле число.</returns>
    public static bool TryParseInt(string? text, out int value)
    {
        value = 0;
        if (!TryParseDecimal(text, 0, out decimal number) || number < int.MinValue || number > int.MaxValue)
        {
            return false;
        }

        value = (int)number;
        return true;
    }

    /// <summary>
    /// Розбирає грошову суму в гривнях (до двох знаків після коми, «грн» наприкінці допускається).
    /// </summary>
    /// <param name="text">Введений текст («110», «110,50», «1 234,5 грн»).</param>
    /// <param name="money">Сума.</param>
    /// <returns><c>true</c>, якщо текст — коректна сума.</returns>
    public static bool TryParseMoney(string? text, out Money money)
    {
        money = Money.Zero;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        string trimmed = text.Trim();
        if (trimmed.EndsWith("грн", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed[..^3];
        }

        if (!TryParseDecimal(trimmed, 2, out decimal hryvnias) || Math.Abs(hryvnias) > 1_000_000_000m)
        {
            return false;
        }

        money = Money.FromHryvnias(hryvnias);
        return true;
    }

    /// <summary>
    /// Розбирає вагу в кілограмах (до трьох знаків після коми) і повертає її в грамах.
    /// </summary>
    /// <param name="text">Введений текст («2», «0,5», «1.25»).</param>
    /// <param name="grams">Вага в грамах.</param>
    /// <returns><c>true</c>, якщо текст — коректна вага.</returns>
    public static bool TryParseKilograms(string? text, out int grams)
    {
        grams = 0;
        if (!TryParseDecimal(text, 3, out decimal kilograms) || Math.Abs(kilograms) > 1_000_000m)
        {
            return false;
        }

        grams = (int)(kilograms * 1000m);
        return true;
    }

    /// <summary>
    /// Розбирає коефіцієнт («1,15») і повертає його у відсотках (115).
    /// </summary>
    /// <param name="text">Введений текст.</param>
    /// <param name="percent">Коефіцієнт у відсотках.</param>
    /// <returns><c>true</c>, якщо текст — коректний коефіцієнт.</returns>
    public static bool TryParseCoefficient(string? text, out int percent)
    {
        percent = 0;
        if (!TryParseDecimal(text, 2, out decimal coefficient) || Math.Abs(coefficient) > 1_000m)
        {
            return false;
        }

        percent = (int)(coefficient * 100m);
        return true;
    }

    /// <summary>Показ ваги в кілограмах для поля вводу: 2000 г → «2», 500 г → «0,5».</summary>
    /// <param name="grams">Вага в грамах.</param>
    /// <returns>Текст українською.</returns>
    public static string FormatKilograms(int grams) => (grams / 1000m).ToString("0.###", Ukrainian);

    /// <summary>Показ коефіцієнта: 115 → «1,15».</summary>
    /// <param name="percent">Коефіцієнт у відсотках.</param>
    /// <returns>Текст українською.</returns>
    public static string FormatCoefficient(int percent) => (percent / 100m).ToString("0.00", Ukrainian);

    /// <summary>Показ суми для поля вводу без позначки валюти: 11050 коп. → «110,50».</summary>
    /// <param name="money">Сума.</param>
    /// <returns>Текст українською.</returns>
    public static string FormatMoneyForInput(Money money) => money.Hryvnias.ToString("0.00", Ukrainian);
}
