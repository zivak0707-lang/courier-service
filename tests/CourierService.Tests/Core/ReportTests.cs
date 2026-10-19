using CourierService.Core;
using CourierService.Core.Enums;
using CourierService.Core.Reports;

namespace CourierService.Tests.Core;

/// <summary>
/// Звіти на відомих даних: період 01–03.10.2026, «зараз» — 03.10.2026 18:00.
/// </summary>
public class ReportTests
{
    private static readonly ReportPeriod Period = new(new DateTime(2026, 10, 1), new DateTime(2026, 10, 3));

    private static ReportOrderRow Row(
        int id, decimal price, OrderStatus status, string created, string deadline, string? delivered = null,
        int? courierId = null, decimal? payout = null, PaymentPayer payer = PaymentPayer.Sender,
        PaymentMethod method = PaymentMethod.Cash, PaymentStatus payment = PaymentStatus.Paid, string? paidAt = null) => new(
        id, $"KV{id:D10}-0", status, DateTime.Parse(created), DateTime.Parse(deadline),
        delivered is null ? null : DateTime.Parse(delivered), Money.FromHryvnias(price), "Клієнт " + id, courierId,
        payout is null ? null : Money.FromHryvnias(payout.Value), payer, method, payment,
        paidAt is null ? null : DateTime.Parse(paidAt));

    internal static ReportData Data() => new(
        Period,
        new DateTime(2026, 10, 3, 18, 0, 0),
        [
            // A: готівка 01.10, доставлено вчасно кур'єром 1 (2 роб. год)
            Row(1, 157m, OrderStatus.Delivered, "2026-10-01 09:00", "2026-10-01 21:00", "2026-10-01 11:00", 1, 78.50m,
                paidAt: "2026-10-01 10:00"),
            // B: картка 01.10, доставлено із запізненням кур'єром 1 (14 роб. год)
            Row(2, 200m, OrderStatus.Delivered, "2026-10-01 10:00", "2026-10-02 10:00", "2026-10-02 12:00", 1, 100m,
                method: PaymentMethod.Card, paidAt: "2026-10-01 15:00"),
            // C: безготівково 03.10, ще в дорозі
            Row(3, 300m, OrderStatus.Assigned, "2026-10-03 09:30", "2026-10-03 21:00", courierId: 2,
                method: PaymentMethod.BankTransfer, paidAt: "2026-10-03 09:40"),
            // D: оплачено до періоду, доставлено в періоді кур'єром 2 вчасно (1 роб. год)
            Row(4, 120m, OrderStatus.Delivered, "2026-10-01 12:00", "2026-10-01 20:00", "2026-10-01 13:00", 2, 60m,
                paidAt: "2026-09-30 12:00"),
            // E: одержувач заплатить під час доставки
            Row(5, 180m, OrderStatus.PickedUp, "2026-09-28 11:00", "2026-09-29 11:00", courierId: 1,
                payer: PaymentPayer.Recipient, payment: PaymentStatus.Pending),
            // F: скасоване з поверненням оплати — не виручка
            Row(6, 90m, OrderStatus.Cancelled, "2026-10-02 09:00", "2026-10-02 21:00", method: PaymentMethod.Card,
                payment: PaymentStatus.Refunded, paidAt: "2026-10-02 09:30"),
        ],
        [new FailedAttempt(2, new DateTime(2026, 10, 2, 14, 0, 0)), new FailedAttempt(1, new DateTime(2026, 9, 29, 14, 0, 0))],
        [
            new ReportCourier(1, "Андрій Ткаченко", true),
            new ReportCourier(2, "Марія Лисенко", true),
            new ReportCourier(3, "Дмитро Кравчук", false),
            new ReportCourier(4, "Оксана Мельник", true),
        ]);

    private static string Text(ReportTable table, int row, int column) =>
        ReportFormat.Display(table.Columns[column].Kind, table.Rows[row][column]);

    [Fact]
    public void Revenue_ByPaymentDate_ExcludesRefundedAndEarlierPayments()
    {
        ReportTable table = new RevenueReport().Build(Data());

        Assert.Equal(2, table.Rows.Count);
        Assert.Equal(new DateTime(2026, 10, 1), table.Rows[0][0]);
        Assert.Equal(2, table.Rows[0][1]);
        Assert.Equal(Money.FromHryvnias(157m), table.Rows[0][2]);
        Assert.Equal(Money.FromHryvnias(200m), table.Rows[0][3]);
        Assert.Equal(Money.FromHryvnias(357m), table.Rows[0][5]);
        Assert.Equal(Money.FromHryvnias(300m), table.Rows[1][4]);
        Assert.Equal(["Разом", 3, Money.FromHryvnias(157m), Money.FromHryvnias(200m), Money.FromHryvnias(300m), Money.FromHryvnias(657m)],
            table.Totals);
        Assert.Equal(Money.FromHryvnias(657m), table.Summary.Single(s => s.Label == "Виручка за період").Value);
        Assert.Equal(Money.FromHryvnias(219m), table.Summary.Single(s => s.Label == "Середній чек").Value);
        Assert.Equal(Money.FromHryvnias(219m), table.Summary.Single(s => s.Label == "Середня виручка за день").Value);
        Assert.Equal("Період: 01.10.2026 — 03.10.2026", table.Subtitle);
    }

    [Fact]
    public void Unpaid_ShowsPendingOnly_WithDaysWaiting()
    {
        ReportTable table = new UnpaidReport().Build(Data());

        var row = Assert.Single(table.Rows);
        Assert.Equal("KV0000000005-0", row[0]);
        Assert.Equal("Забрано", row[2]);
        Assert.Equal("Одержувач", row[4]);
        Assert.Equal(5, row[7]);
        Assert.Equal(Money.FromHryvnias(180m), table.Totals![6]);
        Assert.StartsWith("Стан на 03.10.2026", table.Subtitle);
    }

    [Fact]
    public void CourierPerformance_CountsOnTimeFailuresAndAverageWorkingHours()
    {
        ReportTable table = new CourierPerformanceReport().Build(Data());

        // Неактивний кур'єр без доставок не показується; активний без доставок — так
        Assert.Equal(["Андрій Ткаченко", "Марія Лисенко", "Оксана Мельник"], table.Rows.Select(r => (string)r[0]!));

        Assert.Equal([2, 1, 50.0m, 0, 8.0m, Money.FromHryvnias(357m)], table.Rows[0].Skip(1));   // (2 + 14) / 2 = 8 год
        Assert.Equal([1, 1, 100.0m, 1, 1.0m, Money.FromHryvnias(120m)], table.Rows[1].Skip(1));
        Assert.Equal([0, 0, null, 0, null, Money.Zero], table.Rows[2].Skip(1));
        Assert.Equal(["Разом", 3, 2, 66.7m, 1, 5.7m, Money.FromHryvnias(477m)], table.Totals);
        Assert.Equal("66,7 %", ReportFormat.Display(ReportValueKind.Percent, table.Totals![3]));
        Assert.Equal("64,0 %", ReportFormat.Display(ReportValueKind.Percent, 64m));
        Assert.Equal("8 год", Text(table, 0, 5));
        Assert.Equal("—", Text(table, 2, 3));
    }

    [Fact]
    public void CourierPayout_AndGrossProfit()
    {
        ReportTable table = new CourierPayoutReport().Build(Data());

        Assert.Equal(["Андрій Ткаченко", 2, Money.FromHryvnias(357m), Money.FromHryvnias(178.50m)], table.Rows[0]);
        Assert.Equal(["Марія Лисенко", 1, Money.FromHryvnias(120m), Money.FromHryvnias(60m)], table.Rows[1]);
        Assert.Equal(["Разом", 3, Money.FromHryvnias(477m), Money.FromHryvnias(238.50m)], table.Totals);

        Assert.Equal(Money.FromHryvnias(657m), table.Summary[0].Value);
        Assert.Equal(Money.FromHryvnias(238.50m), table.Summary[1].Value);
        Assert.Equal(Money.FromHryvnias(418.50m), table.Summary[2].Value);
        Assert.Equal(63.7m, table.Summary[3].Value);                     // 418,50 / 657
        Assert.Equal([UserRole.Admin], new CourierPayoutReport().AllowedRoles);
    }

    [Fact]
    public void EmptyPeriod_GivesEmptyTablesWithoutErrors()
    {
        var empty = new ReportData(Period, new DateTime(2026, 10, 3), [], [], [new ReportCourier(1, "Андрій Ткаченко", true)]);

        foreach (ReportBase report in ReportCatalog.All)
        {
            ReportTable table = report.Build(empty);
            Assert.NotEmpty(table.Columns);
            Assert.NotEmpty(table.Notes);
        }

        Assert.Null(new RevenueReport().Build(empty).Summary.Single(s => s.Label == "Середній чек").Value);
    }

    [Fact]
    public void Catalog_HasEveryReportOnce()
    {
        Assert.Equal(Enum.GetValues<ReportKind>(), ReportCatalog.All.Select(r => r.Kind));
        Assert.IsType<UnpaidReport>(ReportCatalog.Get(ReportKind.Unpaid));
        Assert.False(ReportCatalog.Get(ReportKind.Unpaid).UsesPeriod);
    }

    [Fact]
    public void Period_IncludesBothDays()
    {
        Assert.Equal(3, Period.Days);
        Assert.True(Period.Contains(new DateTime(2026, 10, 3, 23, 59, 0)));
        Assert.False(Period.Contains(new DateTime(2026, 10, 4)));
        Assert.False(Period.Contains(null));
        Assert.Equal(new ReportPeriod(new DateTime(2026, 9, 20), new DateTime(2026, 10, 19)),
            ReportPeriod.LastDays(new DateTime(2026, 10, 19, 15, 0, 0), 30));
    }

    [Theory]
    [InlineData(ReportValueKind.Date, "2026-10-01", "01.10.2026")]
    [InlineData(ReportValueKind.DateTime, "2026-10-01 14:05", "01.10.2026 14:05")]
    public void Format_Dates(ReportValueKind kind, string value, string expected)
    {
        Assert.Equal(expected, ReportFormat.Display(kind, DateTime.Parse(value)));
    }

    [Fact]
    public void Format_NumbersMoneyAndEmpty()
    {
        Assert.Equal("12", ReportFormat.Display(ReportValueKind.Integer, 12));
        Assert.Equal("5,7 год", ReportFormat.Display(ReportValueKind.Hours, 5.7m));
        Assert.Equal(Money.FromHryvnias(157m).ToString(), ReportFormat.Display(ReportValueKind.Money, Money.FromHryvnias(157m)));
        Assert.Equal("—", ReportFormat.Display(ReportValueKind.Money, null));
    }
}
