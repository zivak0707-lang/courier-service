using System.Text;

namespace CourierService.Core.Validation;

/// <summary>
/// Українські номери телефону: перевірка, приведення до єдиного формату й гарний показ.
/// </summary>
/// <remarks>
/// У БД номер зберігається в міжнародному форматі <c>+380XXXXXXXXX</c>, тому
/// «050 123 45 67» і «+38 (050) 123-45-67» — той самий номер і для пошуку, і для унікальності.
/// </remarks>
public static class PhoneNumber
{
    private const string CountryCode = "380";
    private const int NationalLength = 9;

    /// <summary>
    /// Приводить номер до формату <c>+380XXXXXXXXX</c>.
    /// Допускаються пробіли, дефіси й дужки: «+380 50 123 45 67», «(050) 123-45-67», «0501234567».
    /// </summary>
    /// <param name="input">Введений номер.</param>
    /// <param name="normalized">Номер у форматі +380XXXXXXXXX або порожній рядок.</param>
    /// <returns><c>true</c>, якщо номер коректний.</returns>
    public static bool TryNormalize(string? input, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var digits = new StringBuilder();
        bool hasPlus = false;
        foreach (char ch in input.Trim())
        {
            if (char.IsAsciiDigit(ch))
            {
                digits.Append(ch);
            }
            else if (ch == '+' && digits.Length == 0 && !hasPlus)
            {
                hasPlus = true;
            }
            else if (ch is not (' ' or '-' or '(' or ')'))
            {
                return false; // літери та інші символи в номері неприпустимі
            }
        }

        string all = digits.ToString();
        string? national = all.Length switch
        {
            12 when all.StartsWith(CountryCode, StringComparison.Ordinal) => all[3..],
            10 when all[0] == '0' && !hasPlus => all[1..],
            _ => null,
        };

        // Код оператора не може починатися з нуля: «+380 05…» не існує
        if (national is null || national.Length != NationalLength || national[0] == '0')
        {
            return false;
        }

        normalized = "+" + CountryCode + national;
        return true;
    }

    /// <summary>
    /// Показує номер у зручному вигляді: «+380 (50) 123-45-67».
    /// </summary>
    /// <param name="phone">Номер у будь-якому допустимому форматі.</param>
    /// <returns>Відформатований номер або вхідний рядок без змін, якщо він некоректний.</returns>
    public static string Format(string? phone)
    {
        if (!TryNormalize(phone, out string normalized))
        {
            return phone ?? string.Empty;
        }

        string n = normalized[4..];
        return $"+380 ({n[..2]}) {n[2..5]}-{n[5..7]}-{n[7..]}";
    }
}
