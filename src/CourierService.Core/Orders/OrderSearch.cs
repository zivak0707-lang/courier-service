using System.Text.RegularExpressions;
using CourierService.Core.Enums;

namespace CourierService.Core.Orders;

/// <summary>
/// Умови пошуку замовлень (рішення D-20). <c>null</c> в умові означає «будь-який».
/// </summary>
/// <param name="Query">Трек-номер або фрагмент телефону відправника чи одержувача.</param>
/// <param name="Status">Статус замовлення.</param>
/// <param name="From">Перший день періоду оформлення (включно).</param>
/// <param name="To">Останній день періоду оформлення (включно).</param>
/// <param name="CourierId">Кур'єр.</param>
/// <param name="ClientId">Відправник.</param>
/// <param name="PaymentStatus">Стан оплати.</param>
public sealed record OrderFilter(
    string? Query = null,
    OrderStatus? Status = null,
    DateTime? From = null,
    DateTime? To = null,
    int? CourierId = null,
    int? ClientId = null,
    PaymentStatus? PaymentStatus = null)
{
    /// <summary>Найбільша кількість рядків у результаті.</summary>
    public const int MaxResults = 500;

    /// <summary>Типовий період списку, днів (включно з сьогоднішнім).</summary>
    public const int DefaultPeriodDays = 30;

    /// <summary>
    /// Типовий фільтр: замовлення за останні 30 днів.
    /// </summary>
    /// <param name="today">Сьогоднішня дата.</param>
    /// <returns>Фільтр.</returns>
    public static OrderFilter LastDays(DateTime today) =>
        new(From: today.Date.AddDays(1 - DefaultPeriodDays), To: today.Date);
}

/// <summary>
/// Рядок списку замовлень — лише поля, потрібні таблиці (проєкція запиту, а не повна сутність).
/// </summary>
/// <param name="Id">Ідентифікатор.</param>
/// <param name="TrackingNumber">Трек-номер.</param>
/// <param name="CreatedAt">Дата й час оформлення.</param>
/// <param name="Status">Статус.</param>
/// <param name="ClientName">Відправник.</param>
/// <param name="ClientPhone">Телефон відправника.</param>
/// <param name="RecipientName">Одержувач.</param>
/// <param name="RecipientPhone">Телефон одержувача.</param>
/// <param name="PickupZone">Зона забору.</param>
/// <param name="DeliveryZone">Зона доставки.</param>
/// <param name="TariffName">Тариф.</param>
/// <param name="Price">Вартість доставки.</param>
/// <param name="PaymentStatus">Стан оплати.</param>
/// <param name="Payer">Хто платить.</param>
/// <param name="PaymentMethod">Спосіб оплати.</param>
/// <param name="CourierName">Призначений кур'єр або <c>null</c>.</param>
/// <param name="Deadline">Крайній термін доставки.</param>
public sealed record OrderListItem(
    int Id,
    string TrackingNumber,
    DateTime CreatedAt,
    OrderStatus Status,
    string ClientName,
    string ClientPhone,
    string RecipientName,
    string RecipientPhone,
    string PickupZone,
    string DeliveryZone,
    string TariffName,
    Money Price,
    PaymentStatus PaymentStatus,
    PaymentPayer Payer,
    PaymentMethod PaymentMethod,
    string? CourierName,
    DateTime Deadline);

/// <summary>
/// Результат пошуку.
/// </summary>
/// <param name="Items">Знайдені замовлення, новіші першими.</param>
/// <param name="IsTruncated">Чи знайдено більше за <see cref="OrderFilter.MaxResults"/> (показано перші).</param>
/// <param name="Notice">Пояснення для користувача (помилка в запиті, обрізаний результат) або <c>null</c>.</param>
public sealed record OrderSearchResult(IReadOnlyList<OrderListItem> Items, bool IsTruncated, string? Notice);

/// <summary>Що саме ввів користувач у рядок пошуку.</summary>
public enum OrderQueryKind
{
    /// <summary>Нічого — пошук лише за фільтрами.</summary>
    None = 1,

    /// <summary>Правильний трек-номер.</summary>
    TrackingNumber = 2,

    /// <summary>Фрагмент номера телефону.</summary>
    Phone = 3,

    /// <summary>Запит не розпізнано або в трек-номері помилка (див. повідомлення).</summary>
    Invalid = 4,
}

/// <summary>
/// Розібраний рядок пошуку замовлень.
/// </summary>
/// <param name="Kind">Вид запиту.</param>
/// <param name="Value">Трек-номер у стандартному вигляді або цифри телефону.</param>
/// <param name="Message">Пояснення для <see cref="OrderQueryKind.Invalid"/>.</param>
public sealed partial record OrderQuery(OrderQueryKind Kind, string Value, string? Message)
{
    /// <summary>Найменша кількість цифр для пошуку за телефоном.</summary>
    public const int MinPhoneDigits = 3;

    /// <summary>
    /// Розпізнає запит: трек-номер — якщо починається з «KV» або має вигляд «2610190042-6»;
    /// телефон — цифри з пробілами, дефісами, дужками чи «+».
    /// </summary>
    /// <param name="text">Текст із рядка пошуку.</param>
    /// <returns>Розібраний запит.</returns>
    public static OrderQuery Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new(OrderQueryKind.None, string.Empty, null);
        }

        string trimmed = text.Trim();
        if (trimmed.StartsWith(TrackingNumber.Letters, StringComparison.OrdinalIgnoreCase)
            || TrackingWithoutLetters().IsMatch(trimmed))
        {
            return TrackingNumber.Check(trimmed, out string normalized) switch
            {
                TrackingNumberCheck.Valid => new(OrderQueryKind.TrackingNumber, normalized, null),
                TrackingNumberCheck.WrongCheckDigit => new(OrderQueryKind.Invalid, string.Empty,
                    "Схоже, у трек-номері помилка: не сходиться контрольна цифра (остання). Перевірте номер."),
                _ => new(OrderQueryKind.Invalid, string.Empty,
                    "Трек-номер має вигляд KV2610190042-6: KV, 10 цифр, дефіс і контрольна цифра."),
            };
        }

        if (trimmed.All(ch => char.IsAsciiDigit(ch) || ch is ' ' or '-' or '(' or ')' or '+'))
        {
            string digits = new(trimmed.Where(char.IsAsciiDigit).ToArray());
            if (digits.Length >= MinPhoneDigits)
            {
                return new(OrderQueryKind.Phone, digits, null);
            }
        }

        return new(OrderQueryKind.Invalid, string.Empty,
            $"Введіть трек-номер (наприклад, KV2610190042-6) або щонайменше {MinPhoneDigits} цифри телефону.");
    }

    [GeneratedRegex(@"^\d{10}\s*-\s*\d$")]
    private static partial Regex TrackingWithoutLetters();
}
