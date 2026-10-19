using CourierService.Core;
using CourierService.Core.Enums;
using CourierService.Core.Reports;

namespace CourierService.Tests.Core;

/// <summary>Дашборд на тих самих відомих даних, що й звіти (див. <see cref="ReportTests"/>).</summary>
public class DashboardCalculatorTests
{
    private readonly DashboardSnapshot _snapshot = DashboardCalculator.Calculate(ReportTests.Data());

    private object? Tile(string label) => _snapshot.Tiles.Single(t => t.Label == label).Value;

    [Fact]
    public void Tiles_SummarizePeriod()
    {
        Assert.Equal(5, Tile("Замовлень оформлено"));                    // A, B, C, D, F (E — до періоду)
        Assert.Equal(Money.FromHryvnias(657m), Tile("Виручка"));
        Assert.Equal(Money.FromHryvnias(418.50m), Tile("Валовий дохід"));   // 657 − 238,50
        Assert.Equal(Money.FromHryvnias(219m), Tile("Середній чек"));
        Assert.Equal(66.7m, Tile("Доставлено вчасно"));                    // 2 з 3
        Assert.Equal(1, Tile("Прострочено зараз"));                        // E: дедлайн 29.09, ще в дорозі
    }

    [Fact]
    public void Daily_HasEveryDayOfPeriod()
    {
        Assert.Equal([new DateTime(2026, 10, 1), new DateTime(2026, 10, 2), new DateTime(2026, 10, 3)],
            _snapshot.Daily.Select(p => p.Day));

        // 01.10: оплати 357, доставки A і D — нарахування 78,50 + 60
        Assert.Equal(Money.FromHryvnias(357m), _snapshot.Daily[0].Revenue);
        Assert.Equal(Money.FromHryvnias(218.50m), _snapshot.Daily[0].GrossProfit);

        // 02.10: оплат немає, доставка B — нарахування 100, тож валовий дохід дня від'ємний
        Assert.Equal(Money.Zero, _snapshot.Daily[1].Revenue);
        Assert.Equal(Money.FromHryvnias(-100m), _snapshot.Daily[1].GrossProfit);
        Assert.Equal(Money.FromHryvnias(300m), _snapshot.Daily[2].Revenue);
    }

    [Fact]
    public void Statuses_OfOrdersCreatedInPeriod()
    {
        Assert.Equal(
            [new StatusCount(OrderStatus.Assigned, 1), new StatusCount(OrderStatus.Delivered, 3), new StatusCount(OrderStatus.Cancelled, 1)],
            _snapshot.Statuses);
    }

    [Fact]
    public void TopCouriers_ByDeliveries()
    {
        Assert.Equal([new CourierScore("Андрій Ткаченко", 2, 50.0m), new CourierScore("Марія Лисенко", 1, 100.0m)],
            _snapshot.TopCouriers);
    }

    [Fact]
    public void EmptyPeriod_HasZeroesAndNoErrors()
    {
        var empty = new ReportData(ReportTests.Data().Period, new DateTime(2026, 10, 3), [], [], []);

        DashboardSnapshot snapshot = DashboardCalculator.Calculate(empty);

        Assert.Equal(3, snapshot.Daily.Count);
        Assert.Empty(snapshot.Statuses);
        Assert.Empty(snapshot.TopCouriers);
        Assert.Null(snapshot.Tiles.Single(t => t.Label == "Середній чек").Value);
    }
}
