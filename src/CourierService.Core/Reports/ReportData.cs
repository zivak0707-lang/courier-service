using CourierService.Core.Enums;

namespace CourierService.Core.Reports;

/// <summary>Звіти програми.</summary>
public enum ReportKind
{
    /// <summary>Виручка за оплаченими замовленнями.</summary>
    Revenue = 1,

    /// <summary>Неоплачені замовлення.</summary>
    Unpaid = 2,

    /// <summary>Ефективність кур'єрів.</summary>
    CourierPerformance = 3,

    /// <summary>Нарахування кур'єрам і валовий дохід.</summary>
    CourierPayout = 4,
}

/// <summary>
/// Період звіту — дні включно.
/// </summary>
/// <param name="From">Перший день.</param>
/// <param name="To">Останній день.</param>
public sealed record ReportPeriod(DateTime From, DateTime To)
{
    /// <summary>Початок першого дня.</summary>
    public DateTime Start => From.Date;

    /// <summary>Початок дня після останнього (межа «менше ніж»).</summary>
    public DateTime EndExclusive => To.Date.AddDays(1);

    /// <summary>Кількість днів.</summary>
    public int Days => (int)(EndExclusive - Start).TotalDays;

    /// <summary>
    /// Останні <paramref name="days"/> днів, включно з сьогоднішнім.
    /// </summary>
    /// <param name="today">Сьогодні.</param>
    /// <param name="days">Кількість днів.</param>
    /// <returns>Період.</returns>
    public static ReportPeriod LastDays(DateTime today, int days) => new(today.Date.AddDays(1 - days), today.Date);

    /// <summary>
    /// Чи потрапляє момент у період.
    /// </summary>
    /// <param name="moment">Момент або <c>null</c>.</param>
    /// <returns><c>true</c>, якщо момент у межах періоду.</returns>
    public bool Contains(DateTime? moment) => moment is { } value && value >= Start && value < EndExclusive;

    /// <summary>«01.10.2026 — 30.10.2026».</summary>
    /// <returns>Текст періоду.</returns>
    public override string ToString() => $"{From:dd.MM.yyyy} — {To:dd.MM.yyyy}";
}

/// <summary>
/// Легкий рядок замовлення для звітів і дашборда (рішення D-29): лише поля, потрібні обчисленням.
/// </summary>
/// <param name="Id">Ідентифікатор.</param>
/// <param name="TrackingNumber">Трек-номер.</param>
/// <param name="Status">Статус.</param>
/// <param name="CreatedAt">Оформлено.</param>
/// <param name="Deadline">Дедлайн.</param>
/// <param name="DeliveredAt">Доставлено.</param>
/// <param name="Price">Вартість доставки.</param>
/// <param name="ClientName">Відправник.</param>
/// <param name="CourierId">Кур'єр.</param>
/// <param name="CourierPayout">Нарахування кур'єру.</param>
/// <param name="Payer">Хто платить.</param>
/// <param name="Method">Спосіб оплати.</param>
/// <param name="PaymentStatus">Стан оплати.</param>
/// <param name="PaidAt">Коли оплачено.</param>
public sealed record ReportOrderRow(
    int Id,
    string TrackingNumber,
    OrderStatus Status,
    DateTime CreatedAt,
    DateTime Deadline,
    DateTime? DeliveredAt,
    Money Price,
    string ClientName,
    int? CourierId,
    Money? CourierPayout,
    PaymentPayer Payer,
    PaymentMethod Method,
    PaymentStatus PaymentStatus,
    DateTime? PaidAt);

/// <summary>Кур'єр для звітів.</summary>
/// <param name="Id">Ідентифікатор.</param>
/// <param name="FullName">Ім'я.</param>
/// <param name="IsActive">Чи активний.</param>
public sealed record ReportCourier(int Id, string FullName, bool IsActive);

/// <summary>Невдала спроба доставки: хто й коли.</summary>
/// <param name="CourierId">Кур'єр, який повідомив про невдалу спробу.</param>
/// <param name="At">Коли.</param>
public sealed record FailedAttempt(int CourierId, DateTime At);

/// <summary>
/// Вхідні дані звітів: замовлення, що стосуються періоду (оформлені, оплачені або доставлені в ньому),
/// усі неоплачені на поточний момент, невдалі спроби й кур'єри.
/// </summary>
/// <param name="Period">Період.</param>
/// <param name="Now">Момент формування.</param>
/// <param name="Orders">Замовлення.</param>
/// <param name="FailedAttempts">Невдалі спроби в періоді.</param>
/// <param name="Couriers">Усі кур'єри.</param>
public sealed record ReportData(
    ReportPeriod Period,
    DateTime Now,
    IReadOnlyList<ReportOrderRow> Orders,
    IReadOnlyList<FailedAttempt> FailedAttempts,
    IReadOnlyList<ReportCourier> Couriers)
{
    /// <summary>Оплати «оплачено» з датою оплати в періоді (виручка, D-09).</summary>
    public IEnumerable<ReportOrderRow> PaidInPeriod =>
        Orders.Where(o => o.PaymentStatus == PaymentStatus.Paid && Period.Contains(o.PaidAt));

    /// <summary>Доставлені в періоді.</summary>
    public IEnumerable<ReportOrderRow> DeliveredInPeriod =>
        Orders.Where(o => o.Status == OrderStatus.Delivered && Period.Contains(o.DeliveredAt));

    /// <summary>Оформлені в періоді.</summary>
    public IEnumerable<ReportOrderRow> CreatedInPeriod => Orders.Where(o => Period.Contains(o.CreatedAt));

    /// <summary>Виручка за період.</summary>
    public Money Revenue => Sum(PaidInPeriod.Select(o => o.Price));

    /// <summary>Нарахування кур'єрам за доставки в періоді (D-15).</summary>
    public Money Payouts => Sum(DeliveredInPeriod.Select(o => o.CourierPayout ?? Money.Zero));

    /// <summary>
    /// Сума грошових значень.
    /// </summary>
    /// <param name="values">Значення.</param>
    /// <returns>Сума.</returns>
    public static Money Sum(IEnumerable<Money> values) => values.Aggregate(Money.Zero, (total, value) => total + value);
}

/// <summary>
/// Побудова звітів (рішення D-29, D-30): дані вибираються з БД, обчислення — у класах звітів.
/// </summary>
public interface IReportService
{
    /// <summary>
    /// Формує звіт. «Нарахування кур'єрам» доступні лише адміністратору.
    /// </summary>
    /// <param name="kind">Звіт.</param>
    /// <param name="period">Період (для «Неоплачених» не використовується).</param>
    /// <param name="cancellationToken">Скасування операції.</param>
    /// <returns>Таблиця звіту.</returns>
    Task<ReportTable> BuildAsync(ReportKind kind, ReportPeriod period, CancellationToken cancellationToken = default);

    /// <summary>
    /// Дані за період для дашборда (D-33).
    /// </summary>
    /// <param name="period">Період.</param>
    /// <param name="cancellationToken">Скасування операції.</param>
    /// <returns>Вхідні дані звітів.</returns>
    Task<ReportData> GetDataAsync(ReportPeriod period, CancellationToken cancellationToken = default);
}
