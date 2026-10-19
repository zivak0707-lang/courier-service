using CourierService.Core.Directories;
using CourierService.Core.Enums;
using CourierService.Core.Orders;
using CourierService.Core.Sla;

namespace CourierService.Core.Reports;

/// <summary>Точка графіка «виручка й валовий дохід по днях».</summary>
/// <param name="Day">День.</param>
/// <param name="Revenue">Виручка за датою оплати.</param>
/// <param name="GrossProfit">Виручка мінус нарахування за доставки цього дня.</param>
/// <param name="IsIncomplete">Чи день ще триває (сьогодні): його суми ще зростатимуть, тож на графіку він позначається окремо.</param>
public sealed record DailyPoint(DateTime Day, Money Revenue, Money GrossProfit, bool IsIncomplete);

/// <summary>Кількість замовлень у статусі.</summary>
/// <param name="Status">Статус.</param>
/// <param name="Count">Кількість.</param>
public sealed record StatusCount(OrderStatus Status, int Count);

/// <summary>Кур'єр у рейтингу дашборда.</summary>
/// <param name="FullName">Ім'я.</param>
/// <param name="Delivered">Доставлено за період.</param>
/// <param name="OnTimePercent">Частка вчасних, %.</param>
public sealed record CourierScore(string FullName, int Delivered, decimal? OnTimePercent);

/// <summary>
/// Дані дашборда (рішення D-33): плитки показників і ряди для графіків.
/// </summary>
/// <param name="Period">Період.</param>
/// <param name="Tiles">Плитки.</param>
/// <param name="Daily">Виручка й валовий дохід по днях (кожен день періоду, навіть без оплат).</param>
/// <param name="Statuses">Оформлені в періоді замовлення за поточними статусами.</param>
/// <param name="TopCouriers">Найкращі кур'єри за кількістю доставок.</param>
public sealed record DashboardSnapshot(
    ReportPeriod Period,
    IReadOnlyList<ReportSummaryItem> Tiles,
    IReadOnlyList<DailyPoint> Daily,
    IReadOnlyList<StatusCount> Statuses,
    IReadOnlyList<CourierScore> TopCouriers);

/// <summary>
/// Розрахунок дашборда з тих самих даних і правил, що й звіти (D-29), — графіки лише малюють готові числа.
/// </summary>
public static class DashboardCalculator
{
    /// <summary>Скільки кур'єрів показувати в рейтингу.</summary>
    public const int TopCount = 5;

    /// <summary>
    /// Обчислює показники й ряди для графіків.
    /// </summary>
    /// <param name="data">Дані за період.</param>
    /// <returns>Дані дашборда.</returns>
    public static DashboardSnapshot Calculate(ReportData data)
    {
        ArgumentNullException.ThrowIfNull(data);

        List<ReportOrderRow> paid = data.PaidInPeriod.ToList();
        List<ReportOrderRow> delivered = data.DeliveredInPeriod.ToList();
        Money revenue = data.Revenue;
        Money gross = revenue - data.Payouts;
        int onTime = delivered.Count(o => SlaEvaluator.IsDeliveredOnTime(o.Deadline, o.DeliveredAt!.Value));

        // «Прострочено зараз» — незавершені замовлення з дедлайном, що минув
        int overdueNow = data.Orders.Count(o => !OrderWorkflow.IsFinal(o.Status) && o.Deadline < data.Now);

        IReadOnlyList<ReportSummaryItem> tiles =
        [
            new("Замовлень оформлено", ReportValueKind.Integer, data.CreatedInPeriod.Count()),
            new("Виручка", ReportValueKind.Money, revenue),
            new("Валовий дохід", ReportValueKind.Money, gross),
            new("Середній чек", ReportValueKind.Money, paid.Count == 0 ? null : ReportBase.Average(revenue, paid.Count)),
            new("Доставлено вчасно", ReportValueKind.Percent,
                delivered.Count == 0 ? null : Math.Round(onTime * 100m / delivered.Count, 1, MidpointRounding.AwayFromZero)),
            new("Прострочено зараз", ReportValueKind.Integer, overdueNow),
        ];

        List<DailyPoint> daily = Enumerable.Range(0, data.Period.Days)
            .Select(offset => data.Period.Start.AddDays(offset))
            .Select(day =>
            {
                Money dayRevenue = ReportData.Sum(paid.Where(o => o.PaidAt!.Value.Date == day).Select(o => o.Price));
                Money dayPayouts = ReportData.Sum(delivered.Where(o => o.DeliveredAt!.Value.Date == day)
                    .Select(o => o.CourierPayout ?? Money.Zero));
                return new DailyPoint(day, dayRevenue, dayRevenue - dayPayouts, IsIncomplete: day == data.Now.Date);
            })
            .ToList();

        List<StatusCount> statuses = data.CreatedInPeriod
            .GroupBy(o => o.Status)
            .OrderBy(group => group.Key)
            .Select(group => new StatusCount(group.Key, group.Count()))
            .ToList();

        var names = data.Couriers.ToDictionary(c => c.Id, c => c.FullName);
        List<CourierScore> top = delivered
            .Where(o => o.CourierId is not null && names.ContainsKey(o.CourierId.Value))
            .GroupBy(o => o.CourierId!.Value)
            .Select(group => new CourierScore(
                names[group.Key],
                group.Count(),
                Math.Round(group.Count(o => SlaEvaluator.IsDeliveredOnTime(o.Deadline, o.DeliveredAt!.Value)) * 100m / group.Count(),
                    1, MidpointRounding.AwayFromZero)))
            .OrderByDescending(score => score.Delivered)
            .ThenBy(score => score.FullName, TextSearch.UkrainianComparer)
            .Take(TopCount)
            .ToList();

        return new DashboardSnapshot(data.Period, tiles, daily, statuses, top);
    }
}
