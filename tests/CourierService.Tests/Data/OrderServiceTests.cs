using CourierService.Core;
using CourierService.Core.Enums;
using CourierService.Core.Exceptions;
using CourierService.Core.Orders;
using CourierService.Core.Pricing;
using CourierService.Core.Sla;
using CourierService.Data.Services;
using CourierService.Tests.Core;
using Microsoft.EntityFrameworkCore;

namespace CourierService.Tests.Data;

/// <summary>
/// Спільна підготовка тестів замовлень: сервіс і дані форми з ідентифікаторами з початкових даних.
/// </summary>
public abstract class OrderServiceTestBase : DirectoryServiceTestBase
{
    private protected OrderService Orders => new(
        Db, CurrentUser, new OrderValidator(), new PriceCalculator(), new DeadlineCalculator(), Clock);

    /// <summary>Стандарт, 1,5 кг, Шевченківський → Дарницький: 157 грн (приклад з плану).</summary>
    protected async Task<OrderInput> InputAsync(
        string tariff = "Стандарт", string from = "Шевченківський", string to = "Дарницький", string client = "Наталія Руденко")
    {
        await using var context = Db.CreateDbContext();
        int clientId = await context.Clients.Where(c => c.Name == client).Select(c => c.Id).SingleAsync();
        int tariffId = await context.Tariffs.Where(t => t.Name == tariff).Select(t => t.Id).SingleAsync();

        return OrderValidatorTests.ValidInput() with
        {
            ClientId = clientId,
            PickupZoneId = await ZoneIdAsync(from),
            DeliveryZoneId = await ZoneIdAsync(to),
            TariffId = tariffId,
            WeightGrams = 1_500,
            DeclaredValue = Money.Zero,
        };
    }
}

public class OrderCreationTests : OrderServiceTestBase
{
    [Fact]
    public async Task Operator_CreatesOrderWithPriceSnapshotPaymentAndHistory()
    {
        await SignInAsync(UserRole.Operator);
        OrderInput input = await InputAsync() with { RecipientPhone = "(050) 123-45-67", Comment = "  Дзвонити заздалегідь " };

        OrderCreated created = await Orders.CreateAsync(input);

        Assert.Equal("KV2610190001-2", created.TrackingNumber);
        Assert.Equal(Money.FromHryvnias(157m), created.Price);
        // 12:00 + 12 робочих год: 9 год сьогодні до 21:00 + 3 год завтра
        Assert.Equal(new DateTime(2026, 10, 20, 12, 0, 0), created.Deadline);

        await using var context = Db.CreateDbContext();
        var order = await context.Orders
            .Include(o => o.Payment)
            .Include(o => o.StatusChanges)
            .SingleAsync(o => o.Id == created.Id);
        Assert.Equal(OrderStatus.New, order.Status);
        Assert.Equal("+380501234567", order.RecipientPhone);
        Assert.Equal("Дзвонити заздалегідь", order.Comment);
        Assert.Equal(Now, order.CreatedAt);
        Assert.Equal(CurrentUser.Session!.UserId, order.CreatedByUserId);
        Assert.Null(order.CourierId);

        // Знімок ціни: складові сходяться з підсумком
        Assert.Equal(120, order.ZoneCoefficientPercent);
        Assert.Equal(1_500, order.ChargeableWeightGrams);
        Assert.Equal(Money.FromHryvnias(132m), order.BaseCost);
        Assert.Equal(Money.FromHryvnias(25m), order.CrossRiverCost);
        Assert.Equal(order.BaseCost + order.WeightCost + order.CrossRiverCost + order.InsuranceCost + order.FragileCost,
            order.Price);

        // Оплата очікує (D-09)
        Assert.NotNull(order.Payment);
        Assert.Equal(PaymentStatus.Pending, order.Payment.Status);
        Assert.Equal(order.Price, order.Payment.Amount);
        Assert.Equal(PaymentPayer.Sender, order.Payment.Payer);
        Assert.Equal(PaymentMethod.Cash, order.Payment.Method);
        Assert.Null(order.Payment.PaidAt);

        // Перший запис історії
        var change = Assert.Single(order.StatusChanges);
        Assert.Null(change.FromStatus);
        Assert.Equal(OrderStatus.New, change.ToStatus);
        Assert.Equal(Now, change.ChangedAt);
        Assert.Equal(CurrentUser.Session.UserId, change.ChangedByUserId);
    }

    [Fact]
    public async Task TrackingNumbers_IncreaseWithinDay_AndRestartNextDay()
    {
        await SignInAsync(UserRole.Operator);
        OrderInput input = await InputAsync();

        string first = (await Orders.CreateAsync(input)).TrackingNumber;
        string second = (await Orders.CreateAsync(input)).TrackingNumber;
        Clock.Now = Now.AddDays(1);
        string nextDay = (await Orders.CreateAsync(input)).TrackingNumber;

        Assert.Equal(TrackingNumber.Create(Now, 1), first);
        Assert.Equal(TrackingNumber.Create(Now, 2), second);
        Assert.Equal(TrackingNumber.Create(Now.AddDays(1), 1), nextDay);
    }

    [Fact]
    public async Task TrackingNumber_ContinuesAfterLargestNumberOfDay()
    {
        await SignInAsync(UserRole.Operator);
        OrderInput input = await InputAsync();
        await Orders.CreateAsync(input);
        Db.ExecuteScalar($"UPDATE Orders SET TrackingNumber = '{TrackingNumber.Create(Now, 41)}'");

        OrderCreated created = await Orders.CreateAsync(input);

        Assert.Equal(TrackingNumber.Create(Now, 42), created.TrackingNumber);
    }

    [Fact]
    public async Task DailyLimit_IsReportedClearly()
    {
        await SignInAsync(UserRole.Operator);
        OrderInput input = await InputAsync();
        await Orders.CreateAsync(input);
        Db.ExecuteScalar($"UPDATE Orders SET TrackingNumber = '{TrackingNumber.Create(Now, TrackingNumber.MaxSequence)}'");

        var exception = await Assert.ThrowsAsync<DomainValidationException>(() => Orders.CreateAsync(input));

        Assert.Contains("9999", exception.UserMessage);
    }

    [Theory]
    [InlineData(UserRole.Admin)]
    [InlineData(UserRole.Courier)]
    public async Task OnlyOperator_CanCreateOrders(UserRole role)
    {
        await SignInAsync(UserRole.Operator);
        OrderInput input = await InputAsync();
        await SignInAsync(role);

        await Assert.ThrowsAsync<AccessDeniedException>(() => Orders.CreateAsync(input));
    }

    [Fact]
    public async Task InvalidInput_IsRejectedWithFieldErrors()
    {
        await SignInAsync(UserRole.Operator);
        OrderInput input = await InputAsync() with { RecipientPhone = "123", LengthCm = 121 };

        var exception = await Assert.ThrowsAsync<DomainValidationException>(() => Orders.CreateAsync(input));

        Assert.NotEmpty(exception.Errors[nameof(OrderInput.RecipientPhone)]);
        Assert.NotEmpty(exception.Errors[nameof(OrderInput.LengthCm)]);
        await using var context = Db.CreateDbContext();
        Assert.False(await context.Orders.AnyAsync());
    }

    [Fact]
    public async Task WeightAboveTariffMaximum_IsReportedOnWeightField()
    {
        await SignInAsync(UserRole.Operator);
        OrderInput input = await InputAsync(tariff: "Термінова") with { WeightGrams = 10_001 };

        var exception = await Assert.ThrowsAsync<DomainValidationException>(() => Orders.CreateAsync(input));

        Assert.NotEmpty(exception.Errors[nameof(OrderInput.WeightGrams)]);
    }

    [Theory]
    [InlineData("Tariffs", "Name = 'Стандарт'", nameof(OrderInput.TariffId))]
    [InlineData("Zones", "Name = 'Дарницький'", nameof(OrderInput.DeliveryZoneId))]
    [InlineData("Zones", "Name = 'Шевченківський'", nameof(OrderInput.PickupZoneId))]
    [InlineData("Clients", "Name = 'Наталія Руденко'", nameof(OrderInput.ClientId))]
    public async Task InactiveReferences_AreRejected(string table, string condition, string property)
    {
        await SignInAsync(UserRole.Operator);
        OrderInput input = await InputAsync();
        Db.ExecuteScalar($"UPDATE {table} SET IsActive = 0 WHERE {condition}");

        var exception = await Assert.ThrowsAsync<DomainValidationException>(() => Orders.CreateAsync(input));

        Assert.Contains("деактивовано", exception.Errors[property].Single());
    }

    [Fact]
    public async Task MissingReference_IsReported()
    {
        await SignInAsync(UserRole.Operator);
        OrderInput input = await InputAsync() with { TariffId = 999 };

        var exception = await Assert.ThrowsAsync<DomainValidationException>(() => Orders.CreateAsync(input));

        Assert.NotEmpty(exception.Errors[nameof(OrderInput.TariffId)]);
    }
}
