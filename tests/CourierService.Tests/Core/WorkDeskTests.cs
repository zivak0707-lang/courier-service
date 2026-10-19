using CourierService.Core;
using CourierService.Core.Enums;
using CourierService.Core.Orders;
using CourierService.Core.Sla;
using CourierService.Tests.App;

namespace CourierService.Tests.Core;

public class WorkDeskTests
{
    private static readonly SlaResult Overdue = new(SlaState.Overdue, "Прострочено на 1 год робочого часу");
    private static readonly SlaResult OnTime = new(SlaState.OnTime, "Вчасно: лишилося 3 год");

    private static OrderListItem Item(string tracking, OrderStatus status, int createdHour, SlaResult sla) =>
        OrderListTestData.Item(tracking, status) with
        {
            CreatedAt = new DateTime(2026, 10, 19, createdHour, 0, 0),
            Deadline = new DateTime(2026, 10, 19, createdHour + 4, 0, 0),
            Sla = sla,
        };

    [Fact]
    public void Operator_NewOldestFirst_OverdueByDeadline_UnpaidSum()
    {
        OrderListItem[] unfinished =
        [
            Item("KV-NEW-LATE", OrderStatus.New, 11, OnTime),
            Item("KV-NEW-EARLY", OrderStatus.New, 9, Overdue),
            Item("KV-ASSIGNED", OrderStatus.Assigned, 10, Overdue),
            Item("KV-PICKED", OrderStatus.PickedUp, 10, OnTime),
        ];
        OrderListItem[] unpaid =
        [
            Item("KV-A", OrderStatus.New, 9, OnTime) with { Price = Money.FromHryvnias(100m) },
            Item("KV-B", OrderStatus.Delivered, 9, OnTime) with { Price = Money.FromHryvnias(57.50m) },
            Item("KV-PAID", OrderStatus.Delivered, 9, OnTime) with { PaymentStatus = PaymentStatus.Paid },
        ];

        OperatorDesk desk = WorkDesk.ForOperator(unfinished, unpaid);

        Assert.Equal(["KV-NEW-EARLY", "KV-NEW-LATE"], desk.NewOrders.Select(o => o.TrackingNumber));
        Assert.Equal(2, desk.NewCount);
        Assert.Equal(["KV-NEW-EARLY", "KV-ASSIGNED"], desk.Overdue.Select(o => o.TrackingNumber));
        Assert.True(desk.HasOverdue);
        Assert.Equal(2, desk.UnpaidCount);
        Assert.Equal(Money.FromHryvnias(157.50m), desk.UnpaidSum);
    }

    [Fact]
    public void Operator_LongLists_AreCutButCountsAreFull()
    {
        var unfinished = Enumerable.Range(0, WorkDesk.ListSize + 3)
            .Select(i => Item($"KV-{i}", OrderStatus.New, 8 + i % 5, OnTime))
            .ToList();

        OperatorDesk desk = WorkDesk.ForOperator(unfinished, []);

        Assert.Equal(WorkDesk.ListSize, desk.NewOrders.Count);
        Assert.Equal(WorkDesk.ListSize + 3, desk.NewCount);
        Assert.False(desk.HasOverdue);
        Assert.Equal(Money.Zero, desk.UnpaidSum);
    }

    [Fact]
    public void Courier_NearestDeadlineAndNextStep()
    {
        OrderListItem[] active =
        [
            Item("KV-LATER", OrderStatus.Assigned, 12, OnTime),
            Item("KV-SOON", OrderStatus.PickedUp, 9, Overdue),
        ];

        CourierDesk desk = WorkDesk.ForCourier(active);

        Assert.Equal(2, desk.ActiveCount);
        Assert.Equal(1, desk.OverdueCount);
        Assert.Equal("KV-SOON", desk.Nearest!.TrackingNumber);
        Assert.Equal("Доставити: вул. Харківське шосе, 10 (Дарницький)", desk.NextStep);
    }

    [Fact]
    public void Courier_AssignedNext_ShowsPickupAddress_EmptyListHasNoNearest()
    {
        CourierDesk desk = WorkDesk.ForCourier([Item("KV-1", OrderStatus.Assigned, 9, OnTime)]);
        Assert.Equal("Забрати: вул. Яблунева, 3, кв. 17 (Шевченківський)", desk.NextStep);

        CourierDesk empty = WorkDesk.ForCourier([]);
        Assert.False(empty.HasActive);
        Assert.Null(empty.Nearest);
        Assert.Null(empty.NextStep);
    }
}
