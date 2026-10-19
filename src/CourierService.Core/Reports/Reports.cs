using CourierService.Core.Directories;
using CourierService.Core.Enums;
using CourierService.Core.Sla;

namespace CourierService.Core.Reports;

/// <summary>
/// Абстрактний звіт — шаблонний метод (рішення D-30): <see cref="Build"/> виконує спільний сценарій
/// «дані → рядки → підсумок → показники → пояснення», а нащадки визначають лише свої кроки.
/// </summary>
public abstract class ReportBase
{
    /// <summary>Який це звіт.</summary>
    public abstract ReportKind Kind { get; }

    /// <summary>Назва звіту.</summary>
    public abstract string Title { get; }

    /// <summary>Чи залежить звіт від періоду (інакше — стан на поточний момент).</summary>
    public virtual bool UsesPeriod => true;

    /// <summary>Хто може формувати звіт.</summary>
    public virtual IReadOnlyList<UserRole> AllowedRoles { get; } = [UserRole.Admin, UserRole.Operator];

    /// <summary>Колонки таблиці.</summary>
    protected abstract IReadOnlyList<ReportColumn> Columns { get; }

    /// <summary>
    /// Будує звіт. Метод не віртуальний: порядок кроків однаковий для всіх звітів.
    /// </summary>
    /// <param name="data">Вхідні дані.</param>
    /// <returns>Таблиця звіту.</returns>
    public ReportTable Build(ReportData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        List<IReadOnlyList<object?>> rows = BuildRows(data).ToList();
        string subtitle = UsesPeriod ? $"Період: {data.Period}" : $"Стан на {data.Now:dd.MM.yyyy HH:mm}";
        return new ReportTable(Title, subtitle, Columns, rows, BuildTotals(data, rows), BuildSummary(data, rows).ToList(),
            Notes.ToList());
    }

    /// <summary>Рядки таблиці.</summary>
    /// <param name="data">Вхідні дані.</param>
    /// <returns>Рядки.</returns>
    protected abstract IEnumerable<IReadOnlyList<object?>> BuildRows(ReportData data);

    /// <summary>Рядок «Разом» (за замовчуванням немає).</summary>
    /// <param name="data">Вхідні дані.</param>
    /// <param name="rows">Побудовані рядки.</param>
    /// <returns>Підсумок або <c>null</c>.</returns>
    protected virtual IReadOnlyList<object?>? BuildTotals(ReportData data, IReadOnlyList<IReadOnlyList<object?>> rows) => null;

    /// <summary>Показники над таблицею (за замовчуванням немає).</summary>
    /// <param name="data">Вхідні дані.</param>
    /// <param name="rows">Побудовані рядки.</param>
    /// <returns>Показники.</returns>
    protected virtual IEnumerable<ReportSummaryItem> BuildSummary(ReportData data, IReadOnlyList<IReadOnlyList<object?>> rows) => [];

    /// <summary>Як рахується звіт — пояснення під таблицею.</summary>
    protected virtual IEnumerable<string> Notes => [];

    /// <summary>
    /// Сума колонки грошей.
    /// </summary>
    /// <param name="rows">Рядки.</param>
    /// <param name="column">Номер колонки.</param>
    /// <returns>Сума.</returns>
    protected static Money SumMoney(IEnumerable<IReadOnlyList<object?>> rows, int column) =>
        ReportData.Sum(rows.Select(row => row[column] is Money money ? money : Money.Zero));

    /// <summary>
    /// Сума колонки цілих чисел.
    /// </summary>
    /// <param name="rows">Рядки.</param>
    /// <param name="column">Номер колонки.</param>
    /// <returns>Сума.</returns>
    protected static int SumInt(IEnumerable<IReadOnlyList<object?>> rows, int column) =>
        rows.Sum(row => row[column] is int value ? value : 0);

    /// <summary>
    /// Середнє значення суми з округленням до копійки.
    /// </summary>
    /// <param name="total">Сума.</param>
    /// <param name="count">Кількість (більше нуля).</param>
    /// <returns>Середнє.</returns>
    public static Money Average(Money total, int count) =>
        new((long)Math.Round((decimal)total.Kopecks / count, MidpointRounding.AwayFromZero));

    /// <summary>
    /// Частка у відсотках з одним знаком після коми або <c>null</c>, якщо знаменник нуль.
    /// </summary>
    /// <param name="part">Частина.</param>
    /// <param name="total">Ціле.</param>
    /// <returns>Відсоток.</returns>
    protected static decimal? Percent(int part, int total) =>
        total == 0 ? null : Math.Round(part * 100m / total, 1, MidpointRounding.AwayFromZero);
}

/// <summary>
/// Виручка за оплаченими замовленнями по днях і способах оплати (D-09): оплати «оплачено» за датою оплати.
/// </summary>
public sealed class RevenueReport : ReportBase
{
    /// <inheritdoc />
    public override ReportKind Kind => ReportKind.Revenue;

    /// <inheritdoc />
    public override string Title => "Виручка";

    /// <inheritdoc />
    protected override IReadOnlyList<ReportColumn> Columns { get; } =
    [
        new("Дата", ReportValueKind.Date),
        new("Оплат", ReportValueKind.Integer),
        new("Готівка", ReportValueKind.Money),
        new("Картка", ReportValueKind.Money),
        new("Безготівково", ReportValueKind.Money),
        new("Разом", ReportValueKind.Money),
    ];

    /// <inheritdoc />
    protected override IEnumerable<string> Notes =>
    [
        "Виручка — оплати зі статусом «оплачено» за датою оплати.",
        "Оплати, повернені під час скасування замовлення, у виручку не входять.",
    ];

    /// <inheritdoc />
    protected override IEnumerable<IReadOnlyList<object?>> BuildRows(ReportData data) =>
        data.PaidInPeriod
            .GroupBy(o => o.PaidAt!.Value.Date)
            .OrderBy(day => day.Key)
            .Select(day => (IReadOnlyList<object?>)
            [
                day.Key,
                day.Count(),
                ByMethod(day, PaymentMethod.Cash),
                ByMethod(day, PaymentMethod.Card),
                ByMethod(day, PaymentMethod.BankTransfer),
                ReportData.Sum(day.Select(o => o.Price)),
            ]);

    /// <inheritdoc />
    protected override IReadOnlyList<object?>? BuildTotals(ReportData data, IReadOnlyList<IReadOnlyList<object?>> rows) =>
        ["Разом", SumInt(rows, 1), SumMoney(rows, 2), SumMoney(rows, 3), SumMoney(rows, 4), SumMoney(rows, 5)];

    /// <inheritdoc />
    protected override IEnumerable<ReportSummaryItem> BuildSummary(ReportData data, IReadOnlyList<IReadOnlyList<object?>> rows)
    {
        Money revenue = data.Revenue;
        int payments = SumInt(rows, 1);
        yield return new("Виручка за період", ReportValueKind.Money, revenue);
        yield return new("Оплачених замовлень", ReportValueKind.Integer, payments);
        yield return new("Середній чек", ReportValueKind.Money, payments == 0 ? null : Average(revenue, payments));
        yield return new("Середня виручка за день", ReportValueKind.Money, Average(revenue, data.Period.Days));
    }

    private static Money ByMethod(IEnumerable<ReportOrderRow> orders, PaymentMethod method) =>
        ReportData.Sum(orders.Where(o => o.Method == method).Select(o => o.Price));
}

/// <summary>
/// Неоплачені замовлення на поточний момент: оплати «очікує» (D-09).
/// </summary>
public sealed class UnpaidReport : ReportBase
{
    /// <inheritdoc />
    public override ReportKind Kind => ReportKind.Unpaid;

    /// <inheritdoc />
    public override string Title => "Неоплачені замовлення";

    /// <inheritdoc />
    public override bool UsesPeriod => false;

    /// <inheritdoc />
    protected override IReadOnlyList<ReportColumn> Columns { get; } =
    [
        new("Трек-номер", ReportValueKind.Text),
        new("Оформлено", ReportValueKind.DateTime),
        new("Статус", ReportValueKind.Text),
        new("Відправник", ReportValueKind.Text),
        new("Платить", ReportValueKind.Text),
        new("Спосіб", ReportValueKind.Text),
        new("Сума", ReportValueKind.Money),
        new("Днів очікування", ReportValueKind.Integer),
    ];

    /// <inheritdoc />
    protected override IEnumerable<string> Notes =>
    [
        "Відправник розраховується до забору посилки, одержувач — під час доставки.",
        "Скасовані й повернені замовлення не враховуються: їхні оплати скасовано або повернено.",
    ];

    /// <inheritdoc />
    protected override IEnumerable<IReadOnlyList<object?>> BuildRows(ReportData data) =>
        data.Orders
            .Where(o => o.PaymentStatus == PaymentStatus.Pending)
            .OrderBy(o => o.CreatedAt)
            .Select(o => (IReadOnlyList<object?>)
            [
                o.TrackingNumber,
                o.CreatedAt,
                o.Status.ToDisplayName(),
                o.ClientName,
                o.Payer.ToDisplayName(),
                o.Method.ToDisplayName(),
                o.Price,
                (int)(data.Now.Date - o.CreatedAt.Date).TotalDays,
            ]);

    /// <inheritdoc />
    protected override IReadOnlyList<object?>? BuildTotals(ReportData data, IReadOnlyList<IReadOnlyList<object?>> rows) =>
        ["Разом", null, null, null, null, null, SumMoney(rows, 6), null];

    /// <inheritdoc />
    protected override IEnumerable<ReportSummaryItem> BuildSummary(ReportData data, IReadOnlyList<IReadOnlyList<object?>> rows)
    {
        yield return new("Неоплачених замовлень", ReportValueKind.Integer, rows.Count);
        yield return new("Очікується до оплати", ReportValueKind.Money, SumMoney(rows, 6));
    }
}

/// <summary>
/// Ефективність кур'єрів за доставками в періоді: кількість, частка вчасних (D-31), невдалі спроби, середній час.
/// </summary>
public sealed class CourierPerformanceReport : ReportBase
{
    /// <inheritdoc />
    public override ReportKind Kind => ReportKind.CourierPerformance;

    /// <inheritdoc />
    public override string Title => "Ефективність кур'єрів";

    /// <inheritdoc />
    protected override IReadOnlyList<ReportColumn> Columns { get; } =
    [
        new("Кур'єр", ReportValueKind.Text),
        new("Доставлено", ReportValueKind.Integer),
        new("Вчасно", ReportValueKind.Integer),
        new("Частка вчасних", ReportValueKind.Percent),
        new("Невдалих спроб", ReportValueKind.Integer),
        new("Сер. час доставки", ReportValueKind.Hours),
        new("Вартість доставлених", ReportValueKind.Money),
    ];

    /// <inheritdoc />
    protected override IEnumerable<string> Notes =>
    [
        "Доставки враховуються за датою доставки; «вчасно» — не пізніше дедлайну.",
        "Середній час — від оформлення до доставки в робочих годинах служби 09:00–21:00.",
    ];

    /// <inheritdoc />
    protected override IEnumerable<IReadOnlyList<object?>> BuildRows(ReportData data)
    {
        var delivered = data.DeliveredInPeriod.ToLookup(o => o.CourierId);
        var failures = data.FailedAttempts.Where(f => data.Period.Contains(f.At)).ToLookup(f => f.CourierId);

        return data.Couriers
            .Where(c => c.IsActive || delivered[c.Id].Any() || failures[c.Id].Any())
            .Select(courier =>
            {
                List<ReportOrderRow> orders = delivered[courier.Id].ToList();
                int onTime = orders.Count(o => SlaEvaluator.IsDeliveredOnTime(o.Deadline, o.DeliveredAt!.Value));
                return new
                {
                    Courier = courier,
                    Row = (IReadOnlyList<object?>)
                    [
                        courier.FullName,
                        orders.Count,
                        onTime,
                        Percent(onTime, orders.Count),
                        failures[courier.Id].Count(),
                        AverageHours(orders),
                        ReportData.Sum(orders.Select(o => o.Price)),
                    ],
                };
            })
            .OrderByDescending(x => (int)x.Row[1]!)
            .ThenBy(x => x.Courier.FullName, TextSearch.UkrainianComparer)
            .Select(x => x.Row);
    }

    /// <inheritdoc />
    protected override IReadOnlyList<object?>? BuildTotals(ReportData data, IReadOnlyList<IReadOnlyList<object?>> rows)
    {
        int delivered = SumInt(rows, 1);
        int onTime = SumInt(rows, 2);
        return ["Разом", delivered, onTime, Percent(onTime, delivered), SumInt(rows, 4),
            AverageHours(data.DeliveredInPeriod.ToList()), SumMoney(rows, 6)];
    }

    private static decimal? AverageHours(IReadOnlyCollection<ReportOrderRow> orders) => orders.Count == 0
        ? null
        : Math.Round((decimal)orders.Average(o => DeadlineCalculator.WorkingTimeBetween(o.CreatedAt, o.DeliveredAt!.Value).TotalHours),
            1, MidpointRounding.AwayFromZero);
}

/// <summary>
/// Нарахування кур'єрам за доставки в періоді й валовий дохід служби (D-15). Лише для адміністратора.
/// </summary>
public sealed class CourierPayoutReport : ReportBase
{
    /// <inheritdoc />
    public override ReportKind Kind => ReportKind.CourierPayout;

    /// <inheritdoc />
    public override string Title => "Нарахування кур'єрам";

    /// <inheritdoc />
    public override IReadOnlyList<UserRole> AllowedRoles { get; } = [UserRole.Admin];

    /// <inheritdoc />
    protected override IReadOnlyList<ReportColumn> Columns { get; } =
    [
        new("Кур'єр", ReportValueKind.Text),
        new("Доставок", ReportValueKind.Integer),
        new("Вартість доставок", ReportValueKind.Money),
        new("Нараховано", ReportValueKind.Money),
    ];

    /// <inheritdoc />
    protected override IEnumerable<string> Notes =>
    [
        "Нарахування фіксується в момент доставки (за відсотком кур'єра на той момент) і враховується за датою доставки.",
        "Валовий дохід = виручка за період (за датою оплати) − нарахування за період (за датою доставки).",
    ];

    /// <inheritdoc />
    protected override IEnumerable<IReadOnlyList<object?>> BuildRows(ReportData data)
    {
        var delivered = data.DeliveredInPeriod.ToLookup(o => o.CourierId);
        return data.Couriers
            .Where(c => delivered[c.Id].Any())
            .OrderBy(c => c.FullName, TextSearch.UkrainianComparer)
            .Select(courier =>
            {
                List<ReportOrderRow> orders = delivered[courier.Id].ToList();
                return (IReadOnlyList<object?>)
                [
                    courier.FullName,
                    orders.Count,
                    ReportData.Sum(orders.Select(o => o.Price)),
                    ReportData.Sum(orders.Select(o => o.CourierPayout ?? Money.Zero)),
                ];
            });
    }

    /// <inheritdoc />
    protected override IReadOnlyList<object?>? BuildTotals(ReportData data, IReadOnlyList<IReadOnlyList<object?>> rows) =>
        ["Разом", SumInt(rows, 1), SumMoney(rows, 2), SumMoney(rows, 3)];

    /// <inheritdoc />
    protected override IEnumerable<ReportSummaryItem> BuildSummary(ReportData data, IReadOnlyList<IReadOnlyList<object?>> rows)
    {
        Money revenue = data.Revenue;
        Money payouts = data.Payouts;
        Money gross = revenue - payouts;
        yield return new("Виручка за період", ReportValueKind.Money, revenue);
        yield return new("Нараховано кур'єрам", ReportValueKind.Money, payouts);
        yield return new("Валовий дохід", ReportValueKind.Money, gross);
        yield return new("Валова маржа", ReportValueKind.Percent,
            revenue.Kopecks == 0 ? null : Math.Round(gross.Kopecks * 100m / revenue.Kopecks, 1, MidpointRounding.AwayFromZero));
    }
}

/// <summary>
/// Каталог звітів: за видом повертає об'єкт звіту (поліморфізм — далі всі звіти обробляються через <see cref="ReportBase"/>).
/// </summary>
public static class ReportCatalog
{
    /// <summary>Усі звіти програми.</summary>
    public static IReadOnlyList<ReportBase> All { get; } =
        [new RevenueReport(), new UnpaidReport(), new CourierPerformanceReport(), new CourierPayoutReport()];

    /// <summary>
    /// Звіт за видом.
    /// </summary>
    /// <param name="kind">Вид звіту.</param>
    /// <returns>Звіт.</returns>
    public static ReportBase Get(ReportKind kind) => All.Single(report => report.Kind == kind);
}
