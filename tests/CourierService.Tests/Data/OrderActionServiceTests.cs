using CourierService.Core;
using CourierService.Core.Assignment;
using CourierService.Core.Enums;
using CourierService.Core.Exceptions;
using CourierService.Core.Orders;
using CourierService.Core.Security;
using CourierService.Data.Services;
using Microsoft.EntityFrameworkCore;

namespace CourierService.Tests.Data;

public class OrderActionServiceTests : OrderServiceTestBase
{
    private OrderActionService Actions => new(Db, CurrentUser, new OrderWorkflow(), new CourierAssignment(), Clock);

    private async Task SignInAsAsync(string login)
    {
        await using var context = Db.CreateDbContext();
        var user = await context.Users.Include(u => u.Courier).SingleAsync(u => u.Login == login);
        CurrentUser.SignIn(new UserSession(user.Id, user.Login, user.FullName, user.Role, user.Courier?.Id));
    }

    private async Task<int> CourierIdAsync(string login)
    {
        await using var context = Db.CreateDbContext();
        return await context.Couriers.Where(c => c.User.Login == login).Select(c => c.Id).SingleAsync();
    }

    /// <summary>Оператор оформлює замовлення (Шевченківський → Дарницький, 157 грн) і за потреби призначає кур'єра.</summary>
    private async Task<int> CreateOrderAsync(
        PaymentPayer payer = PaymentPayer.Sender, PaymentMethod method = PaymentMethod.Cash, string? courierLogin = null, int weightGrams = 1_500)
    {
        await SignInAsAsync("operator");
        var input = await InputAsync() with { Payer = payer, Method = method, WeightGrams = weightGrams };
        int orderId = (await Orders.CreateAsync(input)).Id;
        if (courierLogin is not null)
        {
            await Actions.ExecuteAsync(orderId, OrderAction.Assign, await CourierIdAsync(courierLogin));
        }

        return orderId;
    }

    [Fact]
    public async Task FullDelivery_SenderCash_WritesHistoryPaymentAndPayout()
    {
        int orderId = await CreateOrderAsync(courierLogin: "courier1");
        await SignInAsAsync("courier1");
        int courierUserId = CurrentUser.Session!.UserId;

        Clock.Now = Now.AddHours(1);
        await Actions.ExecuteAsync(orderId, OrderAction.PickUp);
        Clock.Now = Now.AddHours(3);
        await Actions.ExecuteAsync(orderId, OrderAction.Deliver);

        var order = await Orders.GetOrderAsync(orderId);
        Assert.Equal(OrderStatus.Delivered, order.Status);
        Assert.Equal(Now.AddHours(3), order.DeliveredAt);
        Assert.Equal(Money.FromHryvnias(78.50m), order.CourierPayout);
        Assert.Equal(PaymentStatus.Paid, order.Payment!.Status);
        Assert.Equal(Now.AddHours(1), order.Payment.PaidAt);
        Assert.Equal("Андрій Ткаченко", order.Payment.ConfirmedBy?.FullName);
        Assert.Equal(
            [OrderStatus.New, OrderStatus.Assigned, OrderStatus.PickedUp, OrderStatus.Delivered],
            order.StatusChanges.OrderBy(c => c.Id).Select(c => c.ToStatus));
        Assert.Equal(courierUserId, order.StatusChanges.OrderBy(c => c.Id).Last().ChangedByUserId);
        Assert.Equal("Андрій Ткаченко", order.Courier?.User.FullName);
    }

    [Fact]
    public async Task OtherCourier_CannotSeeOrChangeOrder()
    {
        int orderId = await CreateOrderAsync(courierLogin: "courier1");
        await SignInAsAsync("courier2");

        await Assert.ThrowsAsync<AccessDeniedException>(() => Orders.GetOrderAsync(orderId));
        await Assert.ThrowsAsync<AccessDeniedException>(() => Actions.ExecuteAsync(orderId, OrderAction.PickUp));

        await SignInAsAsync("courier1");
        Assert.Equal(orderId, (await Orders.GetOrderAsync(orderId)).Id);
    }

    [Fact]
    public async Task SenderCard_PickUpWaitsForOperatorPayment()
    {
        int orderId = await CreateOrderAsync(PaymentPayer.Sender, PaymentMethod.Card, courierLogin: "courier1");
        await SignInAsAsync("courier1");

        await Assert.ThrowsAsync<PaymentRequiredException>(() => Actions.ExecuteAsync(orderId, OrderAction.PickUp));

        await SignInAsAsync("operator");
        await Actions.ExecuteAsync(orderId, OrderAction.ConfirmPayment);
        await SignInAsAsync("courier1");
        await Actions.ExecuteAsync(orderId, OrderAction.PickUp);

        var order = await Orders.GetOrderAsync(orderId);
        Assert.Equal(OrderStatus.PickedUp, order.Status);
        Assert.Equal("Ігор Бондар", order.Payment!.ConfirmedBy?.FullName);
    }

    [Fact]
    public async Task FailedAttempt_RetryWithAnotherCourier_ThenReturn()
    {
        int orderId = await CreateOrderAsync(PaymentPayer.Recipient, PaymentMethod.Cash, courierLogin: "courier1");
        await SignInAsAsync("courier1");
        await Actions.ExecuteAsync(orderId, OrderAction.PickUp);
        await Actions.ExecuteAsync(orderId, OrderAction.ReportFailure, reason: "Одержувач не відповідає на дзвінки");

        await SignInAsAsync("operator");
        await Actions.ExecuteAsync(orderId, OrderAction.RetryDelivery, await CourierIdAsync("courier4"));
        await SignInAsAsync("courier4");
        await Actions.ExecuteAsync(orderId, OrderAction.PickUp);
        await Actions.ExecuteAsync(orderId, OrderAction.ReportFailure, reason: "Адреса не існує");
        await SignInAsAsync("operator");
        await Actions.ExecuteAsync(orderId, OrderAction.Return);

        var order = await Orders.GetOrderAsync(orderId);
        Assert.Equal(OrderStatus.Returned, order.Status);
        Assert.Equal(PaymentStatus.Cancelled, order.Payment!.Status);
        Assert.Equal(
            ["Одержувач не відповідає на дзвінки", "Адреса не існує"],
            order.StatusChanges.Where(c => c.Reason is not null).OrderBy(c => c.Id).Select(c => c.Reason!));
        Assert.Null(order.CourierPayout);
    }

    [Fact]
    public async Task CancelPaidOrder_RefundsPayment()
    {
        int orderId = await CreateOrderAsync(PaymentPayer.Sender, PaymentMethod.BankTransfer);
        await Actions.ExecuteAsync(orderId, OrderAction.ConfirmPayment);

        await Actions.ExecuteAsync(orderId, OrderAction.Cancel);

        var order = await Orders.GetOrderAsync(orderId);
        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.Equal(PaymentStatus.Refunded, order.Payment!.Status);
    }

    [Fact]
    public async Task AssignTooHeavyParcel_IsRejectedOnCourierField_AndNothingSaved()
    {
        int orderId = await CreateOrderAsync(weightGrams: 12_000);
        int bicycle = await CourierIdAsync("courier2");   // 10 кг

        var exception = await Assert.ThrowsAsync<DomainValidationException>(() =>
            Actions.ExecuteAsync(orderId, OrderAction.Assign, bicycle));

        Assert.Contains("10 кг", exception.Errors["CourierId"].Single());
        var order = await Orders.GetOrderAsync(orderId);
        Assert.Equal(OrderStatus.New, order.Status);
        Assert.Null(order.CourierId);
    }

    [Fact]
    public async Task UnknownOrderOrCourier_IsReported()
    {
        int orderId = await CreateOrderAsync();

        await Assert.ThrowsAsync<EntityNotFoundException>(() => Actions.ExecuteAsync(999, OrderAction.Cancel));
        await Assert.ThrowsAsync<DomainValidationException>(() => Actions.ExecuteAsync(orderId, OrderAction.Assign, 999));
        await Assert.ThrowsAsync<EntityNotFoundException>(() => Orders.GetOrderAsync(999));
    }

    [Fact]
    public async Task Admin_CanViewButNotChange()
    {
        int orderId = await CreateOrderAsync();
        await SignInAsAsync("admin");

        Assert.Equal(orderId, (await Orders.GetOrderAsync(orderId)).Id);
        await Assert.ThrowsAsync<AccessDeniedException>(() => Actions.ExecuteAsync(orderId, OrderAction.Cancel));
    }

    [Fact]
    public async Task AssignmentOptions_SuggestCourierFromPickupZone_WithActiveCounts()
    {
        await CreateOrderAsync(courierLogin: "courier1");
        int orderId = await CreateOrderAsync();

        AssignmentOptions options = await Actions.GetAssignmentOptionsAsync(orderId);

        // Забір у Шевченківському — домашня зона courier1
        Assert.Equal("Андрій Ткаченко", options.Suggestion.Courier?.FullName);
        Assert.Contains("та сама зона (Шевченківський), 1 активна доставка", options.Suggestion.Explanation);
        Assert.Equal(4, options.Couriers.Count);
        Assert.Equal(1, options.Couriers.Single(c => c.FullName == "Андрій Ткаченко").ActiveDeliveries);
        Assert.Equal(0, options.Couriers.Single(c => c.FullName == "Марія Лисенко").ActiveDeliveries);
    }

    [Fact]
    public async Task AssignmentOptions_SkipCouriersThatCannotCarryWeight()
    {
        int orderId = await CreateOrderAsync(weightGrams: 20_000);

        AssignmentOptions options = await Actions.GetAssignmentOptionsAsync(orderId);

        Assert.Contains(options.Suggestion.Courier!.FullName, new[] { "Андрій Ткаченко", "Оксана Мельник" });
    }

    [Fact]
    public async Task MyDeliveries_SplitActiveAndHistory_ForCurrentCourierOnly()
    {
        int first = await CreateOrderAsync(courierLogin: "courier1");
        int second = await CreateOrderAsync(courierLogin: "courier1");
        await CreateOrderAsync(courierLogin: "courier2");
        await SignInAsAsync("courier1");
        await Actions.ExecuteAsync(first, OrderAction.PickUp);
        await Actions.ExecuteAsync(first, OrderAction.Deliver);

        var active = await Orders.GetMyDeliveriesAsync(active: true);
        var history = await Orders.GetMyDeliveriesAsync(active: false);

        Assert.Equal([second], active.Select(o => o.Id));
        Assert.Equal([first], history.Select(o => o.Id));
    }

    [Fact]
    public async Task MyDeliveries_OnlyForCourier()
    {
        await SignInAsAsync("operator");

        await Assert.ThrowsAsync<AccessDeniedException>(() => Orders.GetMyDeliveriesAsync(active: true));
    }
}
