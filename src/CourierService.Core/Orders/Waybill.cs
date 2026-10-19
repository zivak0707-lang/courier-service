using System.Globalization;
using CourierService.Core.Entities;
using CourierService.Core.Enums;
using CourierService.Core.Validation;

namespace CourierService.Core.Orders;

/// <summary>
/// Сторона перевезення на накладній: відправник або одержувач.
/// </summary>
/// <param name="Name">Ім'я або назва.</param>
/// <param name="Phone">Телефон у зручному вигляді.</param>
/// <param name="Address">Адреса.</param>
/// <param name="Zone">Зона (район) доставки.</param>
public sealed record WaybillParty(string Name, string Phone, string Address, string Zone);

/// <summary>
/// Дані друкованої накладної (рішення D-36). Містить лише готові до друку тексти,
/// тож макет сторінки не знає про сутності БД, а зміст перевіряється тестами без принтера.
/// </summary>
/// <param name="TrackingNumber">Трек-номер (його ж закодовано в QR).</param>
/// <param name="CreatedAt">Дата оформлення.</param>
/// <param name="Deadline">Доставити до.</param>
/// <param name="Tariff">Назва тарифу.</param>
/// <param name="Sender">Відправник і адреса, звідки забрати.</param>
/// <param name="Recipient">Одержувач і адреса доставки.</param>
/// <param name="Parcel">Опис посилки: вага, габарити, позначки.</param>
/// <param name="Price">Вартість доставки.</param>
/// <param name="PaymentText">Хто, як і чи оплатив.</param>
/// <param name="CollectOnDelivery">Сума, яку кур'єр має отримати від одержувача, або <c>null</c>.</param>
/// <param name="Courier">Кур'єр або <c>null</c>, якщо ще не призначено.</param>
/// <param name="Comment">Коментар до замовлення або <c>null</c>.</param>
public sealed record Waybill(
    string TrackingNumber,
    DateTime CreatedAt,
    DateTime Deadline,
    string Tariff,
    WaybillParty Sender,
    WaybillParty Recipient,
    IReadOnlyList<string> Parcel,
    Money Price,
    string PaymentText,
    Money? CollectOnDelivery,
    string? Courier,
    string? Comment)
{
    private static readonly CultureInfo Ukrainian = CultureInfo.GetCultureInfo("uk-UA");

    /// <summary>
    /// Чи можна друкувати накладну для замовлення: скасоване замовлення нікуди не їде.
    /// </summary>
    /// <param name="order">Замовлення.</param>
    /// <returns><c>true</c>, якщо накладна має сенс.</returns>
    public static bool CanPrint(Order order) => order.Status != OrderStatus.Cancelled;

    /// <summary>
    /// Складає накладну із замовлення з усіма пов'язаними даними (клієнт, зони, тариф, оплата, кур'єр).
    /// </summary>
    /// <param name="order">Замовлення.</param>
    /// <returns>Накладна.</returns>
    /// <exception cref="InvalidOperationException">Замовлення скасовано.</exception>
    public static Waybill From(Order order)
    {
        ArgumentNullException.ThrowIfNull(order);
        if (!CanPrint(order))
        {
            throw new InvalidOperationException("Для скасованого замовлення накладна не друкується.");
        }

        var parcel = new List<string>
        {
            $"Вага: {InputParsing.FormatKilograms(order.WeightGrams)} кг (розрахункова {InputParsing.FormatKilograms(order.ChargeableWeightGrams)} кг)",
            $"Габарити: {order.LengthCm} × {order.WidthCm} × {order.HeightCm} см",
        };
        if (order.DeclaredValue.Kopecks > 0)
        {
            parcel.Add($"Оголошена цінність: {order.DeclaredValue}");
        }

        if (order.IsFragile)
        {
            parcel.Add("КРИХКА — обережно!");
        }

        return new Waybill(
            order.TrackingNumber,
            order.CreatedAt,
            order.Deadline,
            order.Tariff.Name,
            new WaybillParty(order.Client.Name, PhoneNumber.Format(order.Client.Phone), order.PickupAddress, order.PickupZone.Name),
            new WaybillParty(order.RecipientName, PhoneNumber.Format(order.RecipientPhone), order.DeliveryAddress, order.DeliveryZone.Name),
            parcel,
            order.Price,
            PaymentTextFor(order.Payment),
            CollectOnDeliveryFor(order.Payment),
            order.Courier?.User?.FullName,
            string.IsNullOrWhiteSpace(order.Comment) ? null : order.Comment.Trim());
    }

    /// <summary>Дата оформлення й термін для шапки накладної.</summary>
    public string DatesText =>
        $"Оформлено {CreatedAt.ToString("dd.MM.yyyy HH:mm", Ukrainian)} · доставити до {Deadline.ToString("dd.MM.yyyy HH:mm", Ukrainian)}";

    private static string PaymentTextFor(Payment? payment) => payment is null
        ? "Оплату не оформлено"
        : $"Платник: {payment.Payer.ToDisplayName().ToLower(Ukrainian)}, {payment.Method.ToDisplayName().ToLower(Ukrainian)} — "
          + payment.Status.ToDisplayName().ToLower(Ukrainian);

    // Накладний платіж: одержувач ще не заплатив — кур'єр бере гроші під час доставки
    private static Money? CollectOnDeliveryFor(Payment? payment) =>
        payment is { Payer: PaymentPayer.Recipient, Status: PaymentStatus.Pending } ? payment.Amount : null;
}
