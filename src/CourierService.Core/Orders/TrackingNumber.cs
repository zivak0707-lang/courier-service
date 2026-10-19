using System.Globalization;

namespace CourierService.Core.Orders;

/// <summary>Результат розбору трек-номера, введеного користувачем.</summary>
public enum TrackingNumberCheck
{
    /// <summary>Номер правильний.</summary>
    Valid = 1,

    /// <summary>Текст не має форми трек-номера.</summary>
    InvalidFormat = 2,

    /// <summary>Форма правильна, але контрольна цифра не сходиться — імовірна описка.</summary>
    WrongCheckDigit = 3,
}

/// <summary>
/// Трек-номер замовлення: <c>KV</c> + дата <c>yyMMdd</c> + 4-значний номер за день + дефіс +
/// контрольна цифра за алгоритмом Луна, наприклад <c>KV2610190042-6</c> (рішення D-23).
/// </summary>
/// <remarks>
/// Контрольна цифра виявляє будь-яку помилку в одній цифрі й більшість перестановок
/// сусідніх цифр, тож описка під час ручного введення дає підказку, а не чуже замовлення.
/// </remarks>
public static class TrackingNumber
{
    /// <summary>Літерний префікс служби.</summary>
    public const string Letters = "KV";

    /// <summary>Найбільший порядковий номер за день.</summary>
    public const int MaxSequence = 9999;

    /// <summary>Довжина трек-номера: KV + 6 + 4 + дефіс + 1.</summary>
    public const int Length = 14;

    private const int PayloadDigits = 10;

    /// <summary>
    /// Спільний початок усіх номерів дня, наприклад <c>KV261019</c>.
    /// </summary>
    /// <param name="date">Дата оформлення.</param>
    /// <returns>Префікс дня.</returns>
    public static string DayPrefix(DateTime date) =>
        Letters + date.ToString("yyMMdd", CultureInfo.InvariantCulture);

    /// <summary>
    /// Складає трек-номер.
    /// </summary>
    /// <param name="date">Дата оформлення.</param>
    /// <param name="sequence">Порядковий номер за день, 1…9999.</param>
    /// <returns>Трек-номер.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Номер поза межами 1…9999.</exception>
    public static string Create(DateTime date, int sequence)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(sequence, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(sequence, MaxSequence);

        string payload = date.ToString("yyMMdd", CultureInfo.InvariantCulture)
            + sequence.ToString("D4", CultureInfo.InvariantCulture);
        return $"{Letters}{payload}-{LuhnCheckDigit(payload)}";
    }

    /// <summary>
    /// Порядковий номер за день із правильного трек-номера.
    /// </summary>
    /// <param name="trackingNumber">Трек-номер у форматі <c>KVyyMMddNNNN-C</c>.</param>
    /// <param name="sequence">Порядковий номер.</param>
    /// <returns><c>true</c>, якщо номер вдалося прочитати.</returns>
    public static bool TryGetSequence(string? trackingNumber, out int sequence)
    {
        sequence = 0;
        return trackingNumber is { Length: Length }
            && int.TryParse(trackingNumber.AsSpan(8, 4), NumberStyles.None, CultureInfo.InvariantCulture, out sequence);
    }

    /// <summary>
    /// Розбирає номер, введений вручну: регістр літер, пробіли й дефіси не важать,
    /// літери <c>KV</c> можна не вводити. Контрольна цифра обов'язкова.
    /// </summary>
    /// <param name="input">Введений текст.</param>
    /// <param name="normalized">Номер у стандартному вигляді, якщо результат <see cref="TrackingNumberCheck.Valid"/>.</param>
    /// <returns>Результат перевірки.</returns>
    public static TrackingNumberCheck Check(string? input, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(input))
        {
            return TrackingNumberCheck.InvalidFormat;
        }

        string text = input.Trim();
        if (text.StartsWith(Letters, StringComparison.OrdinalIgnoreCase))
        {
            text = text[Letters.Length..];
        }

        if (!text.All(ch => char.IsAsciiDigit(ch) || ch is ' ' or '-'))
        {
            return TrackingNumberCheck.InvalidFormat;
        }

        string digits = new(text.Where(char.IsAsciiDigit).ToArray());
        if (digits.Length != PayloadDigits + 1)
        {
            return TrackingNumberCheck.InvalidFormat;
        }

        string payload = digits[..PayloadDigits];
        if (LuhnCheckDigit(payload) != digits[PayloadDigits] - '0')
        {
            return TrackingNumberCheck.WrongCheckDigit;
        }

        normalized = $"{Letters}{payload}-{digits[PayloadDigits]}";
        return TrackingNumberCheck.Valid;
    }

    /// <summary>
    /// Контрольна цифра за алгоритмом Луна: справа наліво кожна друга цифра (починаючи
    /// з крайньої правої) подвоюється, з подвоєних понад 9 віднімається 9; цифра доповнює
    /// суму до кратної 10.
    /// </summary>
    /// <param name="digits">Рядок цифр.</param>
    /// <returns>Контрольна цифра 0…9.</returns>
    public static int LuhnCheckDigit(string digits)
    {
        ArgumentNullException.ThrowIfNull(digits);

        int sum = 0;
        bool doubleIt = true;
        for (int i = digits.Length - 1; i >= 0; i--)
        {
            if (!char.IsAsciiDigit(digits[i]))
            {
                throw new ArgumentException("Очікувалися лише цифри.", nameof(digits));
            }

            int digit = digits[i] - '0';
            if (doubleIt)
            {
                digit *= 2;
                if (digit > 9)
                {
                    digit -= 9;
                }
            }

            sum += digit;
            doubleIt = !doubleIt;
        }

        return (10 - sum % 10) % 10;
    }
}
