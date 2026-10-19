using CourierService.Core;
using CourierService.Core.Entities;
using CourierService.Core.Enums;
using CourierService.Core.Orders;
using CourierService.Data;
using CourierService.Data.Seed;
using Microsoft.EntityFrameworkCore;

namespace CourierService.Tests.Data;

public class DemoOrderGeneratorTests : IAsyncLifetime
{
    private static readonly DateTime Now = new(2026, 10, 19, 15, 0, 0);

    private SqliteTestDatabase _db = null!;
    private List<Order> _orders = [];

    public async Task InitializeAsync()
    {
        _db = await CreateSeededDatabaseAsync();
        await using var context = _db.CreateDbContext();
        _orders = await LoadOrdersAsync(context);
    }

    public async Task DisposeAsync() => await _db.DisposeAsync();

    private static async Task<SqliteTestDatabase> CreateSeededDatabaseAsync()
    {
        var db = await SqliteTestDatabase.CreateAsync();
        await using var context = db.CreateDbContext();
        await new DatabaseSeeder(new FakePasswordHasher(), new FixedClock(Now)).SeedAsync(context);
        await new DemoOrderGenerator(new FixedClock(Now)).SeedAsync(context);
        return db;
    }

    private static Task<List<Order>> LoadOrdersAsync(AppDbContext context) => context.Orders
        .Include(o => o.Payment)
        .Include(o => o.Courier)
        .Include(o => o.StatusChanges)
        .AsSplitQuery()
        .OrderBy(o => o.Id)
        .ToListAsync();

    [Fact]
    public void CreatesEnoughOrdersForReportsAndDashboard()
    {
        Assert.InRange(_orders.Count, 50, 120);
        Assert.True(_orders.Min(o => o.CreatedAt) < Now.AddDays(-25));
        Assert.All(_orders, o => Assert.True(o.CreatedAt <= Now));
    }

    [Fact]
    public void CoversFinalAndInProgressStatuses()
    {
        var statuses = _orders.Select(o => o.Status).ToHashSet();

        Assert.Contains(OrderStatus.Delivered, statuses);
        Assert.Contains(OrderStatus.Cancelled, statuses);
        Assert.Contains(OrderStatus.Returned, statuses);
        Assert.True(statuses.Overlaps([OrderStatus.New, OrderStatus.Assigned, OrderStatus.PickedUp]),
            "Мають бути замовлення «в роботі» для екрана кур'єра.");
        Assert.True(_orders.Count(o => o.Status == OrderStatus.Delivered) > _orders.Count / 2);
    }

    [Fact]
    public void PricesPaymentsAndPayoutsFollowTheRules()
    {
        Assert.All(_orders, order =>
        {
            Assert.Equal(order.BaseCost + order.WeightCost + order.CrossRiverCost + order.InsuranceCost + order.FragileCost, order.Price);
            Assert.Equal(order.Price, order.Payment!.Amount);
            Assert.Equal(TrackingNumberCheck.Valid, TrackingNumber.Check(order.TrackingNumber, out _));

            switch (order.Status)
            {
                case OrderStatus.Delivered:
                    Assert.Equal(PaymentStatus.Paid, order.Payment.Status);
                    Assert.Equal(OrderWorkflow.CourierPayout(order.Price, order.Courier!.PayoutPercent), order.CourierPayout);
                    Assert.NotNull(order.DeliveredAt);
                    break;
                case OrderStatus.Cancelled:
                    Assert.Contains(order.Payment.Status, new[] { PaymentStatus.Cancelled, PaymentStatus.Refunded });
                    break;
                case OrderStatus.PickedUp when order.Payment.Payer == PaymentPayer.Sender:
                    Assert.Equal(PaymentStatus.Paid, order.Payment.Status);   // D-26
                    break;
                default:
                    Assert.Null(order.CourierPayout);
                    break;
            }
        });
        Assert.Equal(_orders.Count, _orders.Select(o => o.TrackingNumber).Distinct().Count());
    }

    [Fact]
    public void HistoryIsChronologicalAndStartsWithCreation()
    {
        Assert.All(_orders, order =>
        {
            var history = order.StatusChanges.OrderBy(c => c.Id).ToList();
            Assert.Null(history[0].FromStatus);
            Assert.Equal(order.CreatedAt, history[0].ChangedAt);
            Assert.Equal(order.Status, history[^1].ToStatus);
            Assert.True(history.Zip(history.Skip(1)).All(pair => pair.First.ChangedAt <= pair.Second.ChangedAt));
            Assert.True(history.Zip(history.Skip(1)).All(pair => pair.First.ToStatus == pair.Second.FromStatus));
            Assert.All(history, change => Assert.True(change.ChangedAt <= Now));
        });
    }

    [Fact]
    public void SomeDeliveriesAreLate_ForDeadlineControl()
    {
        var delivered = _orders.Where(o => o.Status == OrderStatus.Delivered).ToList();

        Assert.Contains(delivered, o => o.DeliveredAt > o.Deadline);
        Assert.True(delivered.Count(o => o.DeliveredAt <= o.Deadline) > delivered.Count / 2);
    }

    [Fact]
    public void FailedAttemptsHaveReasons()
    {
        var failures = _orders.SelectMany(o => o.StatusChanges).Where(c => c.ToStatus == OrderStatus.DeliveryFailed).ToList();

        Assert.NotEmpty(failures);
        Assert.All(failures, change => Assert.False(string.IsNullOrWhiteSpace(change.Reason)));
    }

    [Fact]
    public async Task SameDay_GivesSameData()
    {
        await using var other = await CreateSeededDatabaseAsync();
        await using var context = other.CreateDbContext();
        var again = await LoadOrdersAsync(context);

        Assert.Equal(_orders.Select(o => (o.TrackingNumber, o.Status, o.Price)), again.Select(o => (o.TrackingNumber, o.Status, o.Price)));
    }

    [Theory]
    [InlineData(8, 30)]     // до відкриття служби
    [InlineData(12, 0)]
    [InlineData(22, 45)]    // після закриття
    public async Task AnyStartTime_GivesValidDataWithWorkInProgress(int hour, int minute)
    {
        var now = new DateTime(2026, 10, 19, hour, minute, 0);
        await using var db = await SqliteTestDatabase.CreateAsync();
        await using var context = db.CreateDbContext();
        await new DatabaseSeeder(new FakePasswordHasher(), new FixedClock(now)).SeedAsync(context);

        int added = await new DemoOrderGenerator(new FixedClock(now)).SeedAsync(context);

        var orders = await LoadOrdersAsync(context);
        Assert.Equal(added, orders.Count);
        Assert.All(orders.SelectMany(o => o.StatusChanges), change => Assert.True(change.ChangedAt <= now));
        Assert.Contains(orders, o => o.Status is OrderStatus.New or OrderStatus.Assigned or OrderStatus.PickedUp);
    }

    [Fact]
    public void Afternoon_CouriersHaveActiveDeliveries()
    {
        Assert.Contains(_orders, o => o.Status == OrderStatus.Assigned);
        Assert.Contains(_orders, o => o.Status == OrderStatus.PickedUp);
        Assert.Contains(_orders, o => o.Status == OrderStatus.New && o.Payment!.Status == PaymentStatus.Pending);
    }

    [Fact]
    public async Task ExistingOrders_AreNotDuplicated()
    {
        await using var context = _db.CreateDbContext();

        int added = await new DemoOrderGenerator(new FixedClock(Now.AddDays(1))).SeedAsync(context);

        Assert.Equal(0, added);
        Assert.Equal(_orders.Count, await context.Orders.CountAsync());
    }
}
