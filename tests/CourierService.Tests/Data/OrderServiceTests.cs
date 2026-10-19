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
        Db, CurrentUser, new OrderValidator(), new PriceCalculator(), new DeadlineCalculator(), Clock, new SlaEvaluator(Clock));

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

public class OrderSearchTests : OrderServiceTestBase
{
    private string _rudenkoOld = null!;
    private string _rudenkoToday = null!;
    private string _flowersToday = null!;

    /// <summary>
    /// Три замовлення: Руденко 40 днів тому, Руденко сьогодні (одержувач 050…), «Квітковий двір» сьогодні
    /// (одержувач 067…, призначено кур'єра, оплачено).
    /// </summary>
    private async Task SeedOrdersAsync()
    {
        await SignInAsync(UserRole.Operator);
        Clock.Now = Now.AddDays(-40);
        _rudenkoOld = (await Orders.CreateAsync(await InputAsync())).TrackingNumber;
        Clock.Now = Now;
        _rudenkoToday = (await Orders.CreateAsync(await InputAsync() with { RecipientPhone = "050 777 11 22" })).TrackingNumber;
        _flowersToday = (await Orders.CreateAsync(
            await InputAsync(client: "ТОВ «Квітковий двір»") with { RecipientPhone = "067 555 33 44" })).TrackingNumber;

        Db.ExecuteScalar($"""
            UPDATE Orders SET Status = 'Assigned', CourierId = (SELECT MIN(Id) FROM Couriers)
            WHERE TrackingNumber = '{_flowersToday}';
            UPDATE Payments SET Status = 'Paid', PaidAt = '2026-10-19 12:30:00'
            WHERE OrderId = (SELECT Id FROM Orders WHERE TrackingNumber = '{_flowersToday}');
            """);
    }

    private async Task<string[]> FoundAsync(OrderFilter filter) =>
        (await Orders.SearchAsync(filter)).Items.Select(o => o.TrackingNumber).ToArray();

    private async Task<int> IdAsync(string sql) => Convert.ToInt32(Db.ExecuteScalar(sql), System.Globalization.CultureInfo.InvariantCulture);

    [Fact]
    public async Task NoConditions_ReturnsAllNewestFirst()
    {
        await SeedOrdersAsync();

        Assert.Equal([_flowersToday, _rudenkoToday, _rudenkoOld], await FoundAsync(new OrderFilter()));
    }

    [Fact]
    public async Task DefaultPeriod_HidesOlderOrders()
    {
        await SeedOrdersAsync();

        Assert.Equal([_flowersToday, _rudenkoToday], await FoundAsync(OrderFilter.LastDays(Now)));
    }

    [Fact]
    public async Task Period_IncludesBothBoundaryDays()
    {
        await SeedOrdersAsync();
        DateTime oldDay = Now.AddDays(-40).Date;

        Assert.Equal([_rudenkoOld], await FoundAsync(new OrderFilter(From: oldDay, To: oldDay)));
        Assert.Equal([_flowersToday, _rudenkoToday], await FoundAsync(new OrderFilter(From: Now.Date, To: Now.Date)));
        Assert.Empty(await FoundAsync(new OrderFilter(From: oldDay.AddDays(1), To: Now.Date.AddDays(-1))));
    }

    [Fact]
    public async Task ReversedPeriod_IsExplained()
    {
        await SeedOrdersAsync();

        var result = await Orders.SearchAsync(new OrderFilter(From: Now, To: Now.AddDays(-1)));

        Assert.Empty(result.Items);
        Assert.Contains("пізніша", result.Notice);
    }

    [Fact]
    public async Task TrackingNumber_FindsOrderOutsidePeriodAndIgnoresOtherFilters()
    {
        await SeedOrdersAsync();
        string typed = _rudenkoOld.ToLowerInvariant().Replace("-", " ", StringComparison.Ordinal);

        var filter = OrderFilter.LastDays(Now) with { Query = typed, Status = OrderStatus.Delivered };

        Assert.Equal([_rudenkoOld], await FoundAsync(filter));
    }

    [Fact]
    public async Task UnknownTrackingNumber_IsReported()
    {
        await SeedOrdersAsync();
        string unknown = TrackingNumber.Create(Now, 77);

        var result = await Orders.SearchAsync(new OrderFilter(Query: unknown));

        Assert.Empty(result.Items);
        Assert.Contains("не знайдено", result.Notice);
    }

    [Fact]
    public async Task TrackingNumberWithTypo_ReturnsHintInsteadOfResults()
    {
        await SeedOrdersAsync();
        char last = _rudenkoToday[^1];
        string typo = _rudenkoToday[..^1] + (last == '9' ? '0' : (char)(last + 1));

        var result = await Orders.SearchAsync(new OrderFilter(Query: typo));

        Assert.Empty(result.Items);
        Assert.Contains("контрольна цифра", result.Notice);
    }

    [Theory]
    [InlineData("050 777 11 22", 1)]     // одержувач
    [InlineData("0930000204", 1)]        // відправник (Руденко); старе замовлення поза періодом
    [InlineData("555-33", 1)]
    [InlineData("0999999999", 0)]
    public async Task Phone_MatchesSenderOrRecipientInAnyFormat(string phone, int expected)
    {
        await SeedOrdersAsync();

        var found = await FoundAsync(new OrderFilter(Query: phone, From: Now.Date, To: Now.Date));

        Assert.Equal(expected, found.Length);
    }

    [Fact]
    public async Task StatusCourierClientAndPayment_Filters()
    {
        await SeedOrdersAsync();
        int courierId = await IdAsync("SELECT MIN(Id) FROM Couriers");
        int flowersId = await IdAsync("SELECT Id FROM Clients WHERE Name = 'ТОВ «Квітковий двір»'");

        Assert.Equal([_flowersToday], await FoundAsync(new OrderFilter(Status: OrderStatus.Assigned)));
        Assert.Equal([_rudenkoToday, _rudenkoOld], await FoundAsync(new OrderFilter(Status: OrderStatus.New)));
        Assert.Equal([_flowersToday], await FoundAsync(new OrderFilter(CourierId: courierId)));
        Assert.Equal([_flowersToday], await FoundAsync(new OrderFilter(ClientId: flowersId)));
        Assert.Equal([_flowersToday], await FoundAsync(new OrderFilter(PaymentStatus: PaymentStatus.Paid)));
        Assert.Equal([_rudenkoToday, _rudenkoOld], await FoundAsync(new OrderFilter(PaymentStatus: PaymentStatus.Pending)));
        Assert.Empty(await FoundAsync(new OrderFilter(Status: OrderStatus.Assigned, ClientId: flowersId,
            PaymentStatus: PaymentStatus.Pending)));
    }

    [Fact]
    public async Task ListItem_ContainsDisplayFields()
    {
        await SeedOrdersAsync();

        var item = (await Orders.SearchAsync(new OrderFilter(Query: _flowersToday))).Items.Single();

        Assert.Equal("ТОВ «Квітковий двір»", item.ClientName);
        Assert.Equal("+380675553344", item.RecipientPhone);
        Assert.Equal("Шевченківський", item.PickupZone);
        Assert.Equal("Дарницький", item.DeliveryZone);
        Assert.Equal("Стандарт", item.TariffName);
        Assert.Equal(Money.FromHryvnias(157m), item.Price);
        Assert.Equal(OrderStatus.Assigned, item.Status);
        Assert.Equal(PaymentStatus.Paid, item.PaymentStatus);
        Assert.False(string.IsNullOrEmpty(item.CourierName));
        Assert.Equal(new DateTime(2026, 10, 20, 12, 0, 0), item.Deadline);
    }

    [Fact]
    public async Task InvalidQuery_ReturnsExplanation()
    {
        await SeedOrdersAsync();

        var result = await Orders.SearchAsync(new OrderFilter(Query: "Руденко"));

        Assert.Empty(result.Items);
        Assert.NotNull(result.Notice);
    }

    [Fact]
    public async Task Courier_CannotSearchAllOrders()
    {
        await SignInAsync(UserRole.Courier);

        await Assert.ThrowsAsync<AccessDeniedException>(() => Orders.SearchAsync(new OrderFilter()));
    }

    [Fact]
    public async Task Admin_CanSearch()
    {
        await SeedOrdersAsync();
        await SignInAsync(UserRole.Admin);

        Assert.Equal(3, (await Orders.SearchAsync(new OrderFilter())).Items.Count);
    }

    [Fact]
    public async Task ResultAboveLimit_IsTruncatedWithNotice()
    {
        await SignInAsync(UserRole.Operator);
        OrderInput input = await InputAsync();
        for (int i = 0; i < OrderFilter.MaxResults + 1; i++)
        {
            await Orders.CreateAsync(input);
        }

        var result = await Orders.SearchAsync(new OrderFilter());

        Assert.True(result.IsTruncated);
        Assert.Equal(OrderFilter.MaxResults, result.Items.Count);
        Assert.Contains("Уточніть фільтр", result.Notice);
        // Обрізаються найстаріші: першого замовлення в результаті немає
        Assert.DoesNotContain(TrackingNumber.Create(Now, 1), result.Items.Select(o => o.TrackingNumber));
    }
}

public class OrderListSlaTests : OrderServiceTestBase
{
    [Fact]
    public async Task ListItems_CarrySlaAtQueryTime()
    {
        await SignInAsync(UserRole.Operator);
        await Orders.CreateAsync(await InputAsync(tariff: "Термінова"));   // 12:00 + 2 год → 14:00

        Clock.Now = Now.AddMinutes(100);   // лишилося 20 хв із 2 год
        var atRisk = (await Orders.SearchAsync(new OrderFilter())).Items.Single();
        Clock.Now = Now.AddHours(3);
        var overdue = (await Orders.SearchAsync(new OrderFilter())).Items.Single();

        Assert.Equal(SlaState.AtRisk, atRisk.Sla.State);
        Assert.Equal(SlaState.Overdue, overdue.Sla.State);
        Assert.Equal("Прострочено на 1 год робочого часу", overdue.Sla.Text);
        Assert.Null(overdue.DeliveredAt);
    }
}
