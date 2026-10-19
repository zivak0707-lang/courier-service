using CourierService.Core.Enums;
using CourierService.Core.Sla;

namespace CourierService.Core.Orders;

/// <summary>
/// Робочий стіл оператора (рішення D-42): що потребує уваги зараз.
/// </summary>
/// <param name="NewOrders">Нові замовлення без кур'єра, найстаріші першими (не більше <see cref="WorkDesk.ListSize"/>).</param>
/// <param name="NewCount">Скільки всього нових без кур'єра.</param>
/// <param name="Overdue">Незавершені прострочені, найдавніший дедлайн першим (не більше <see cref="WorkDesk.ListSize"/>).</param>
/// <param name="OverdueCount">Скільки всього прострочених.</param>
/// <param name="UnpaidCount">Неоплачених замовлень.</param>
/// <param name="UnpaidSum">Сума, що очікується до оплати.</param>
public sealed record OperatorDesk(
    IReadOnlyList<OrderListItem> NewOrders,
    int NewCount,
    IReadOnlyList<OrderListItem> Overdue,
    int OverdueCount,
    int UnpaidCount,
    Money UnpaidSum)
{
    /// <summary>Чи є нові без кур'єра.</summary>
    public bool HasNew => NewCount > 0;

    /// <summary>Чи є прострочені.</summary>
    public bool HasOverdue => OverdueCount > 0;
}

/// <summary>
/// Робочий стіл кур'єра (рішення D-42).
/// </summary>
/// <param name="ActiveCount">Активних доставок.</param>
/// <param name="OverdueCount">З них прострочених.</param>
/// <param name="Nearest">Доставка з найближчим дедлайном або <c>null</c>.</param>
/// <param name="NextStep">Що зробити з нею далі: «Забрати: адреса» або «Доставити: адреса».</param>
public sealed record CourierDesk(int ActiveCount, int OverdueCount, OrderListItem? Nearest, string? NextStep)
{
    /// <summary>Чи є активні доставки.</summary>
    public bool HasActive => ActiveCount > 0;
}

/// <summary>
/// Підсумки для робочих столів за ролями: рахуються з рядків списку замовлень, які вже повертає сервіс.
/// </summary>
public static class WorkDesk
{
    /// <summary>Скільки замовлень показувати в коротких списках.</summary>
    public const int ListSize = 5;


    /// <summary>
    /// Робочий стіл оператора.
    /// </summary>
    /// <param name="unfinished">Незавершені замовлення з оцінкою терміну.</param>
    /// <param name="unpaid">Замовлення з оплатою, що очікується.</param>
    /// <returns>Підсумки.</returns>
    public static OperatorDesk ForOperator(IEnumerable<OrderListItem> unfinished, IEnumerable<OrderListItem> unpaid)
    {
        ArgumentNullException.ThrowIfNull(unfinished);
        ArgumentNullException.ThrowIfNull(unpaid);
        List<OrderListItem> items = unfinished.Where(o => OrderWorkflow.OpenStatuses.Contains(o.Status)).ToList();

        // Статус «Нове» означає, що кур'єра ще не призначено
        List<OrderListItem> fresh = items.Where(o => o.Status == OrderStatus.New).OrderBy(o => o.CreatedAt).ToList();
        List<OrderListItem> overdue = items.Where(o => o.Sla.State == SlaState.Overdue).OrderBy(o => o.Deadline).ToList();
        List<OrderListItem> waiting = unpaid.Where(o => o.PaymentStatus == PaymentStatus.Pending).ToList();

        return new OperatorDesk(
            fresh.Take(ListSize).ToList(),
            fresh.Count,
            overdue.Take(ListSize).ToList(),
            overdue.Count,
            waiting.Count,
            waiting.Aggregate(Money.Zero, (sum, o) => sum + o.Price));
    }

    /// <summary>
    /// Робочий стіл кур'єра.
    /// </summary>
    /// <param name="active">Активні доставки кур'єра з оцінкою терміну.</param>
    /// <returns>Підсумки.</returns>
    public static CourierDesk ForCourier(IEnumerable<OrderListItem> active)
    {
        ArgumentNullException.ThrowIfNull(active);
        List<OrderListItem> items = active.OrderBy(o => o.Deadline).ToList();
        OrderListItem? nearest = items.FirstOrDefault();
        string? nextStep = nearest?.Status switch
        {
            OrderStatus.Assigned => $"Забрати: {nearest.PickupAddress} ({nearest.PickupZone})",
            OrderStatus.PickedUp => $"Доставити: {nearest.DeliveryAddress} ({nearest.DeliveryZone})",
            _ => null,
        };

        return new CourierDesk(items.Count, items.Count(o => o.Sla.State == SlaState.Overdue), nearest, nextStep);
    }
}
