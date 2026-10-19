using CourierService.Core;
using CourierService.Core.Entities;
using CourierService.Core.Enums;
using CourierService.Core.Exceptions;
using CourierService.Core.Orders;
using CourierService.Core.Security;

namespace CourierService.Tests.Core;

/// <summary>Замовлення в потрібному стані для тестів правил — проходить лише дозволені переходи.</summary>
internal static class WorkflowTestData
{
    public static readonly DateTime Now = new(2026, 10, 19, 12, 0, 0);

    public static readonly UserSession Operator = new(2, "operator", "Ігор Бондар", UserRole.Operator, null);
    public static readonly UserSession Admin = new(1, "admin", "Олена Марченко", UserRole.Admin, null);
    public static readonly UserSession Courier1 = new(3, "courier1", "Андрій Ткаченко", UserRole.Courier, 1);
    public static readonly UserSession Courier2 = new(4, "courier2", "Марія Коваль", UserRole.Courier, 2);

    public static Courier Courier(int id = 1, int maxKg = 30, bool active = true, int payoutPercent = 50) =>
        new() { Id = id, MaxWeightGrams = maxKg * 1000, IsActive = active, PayoutPercent = payoutPercent };

    public static Order NewOrder(
        PaymentPayer payer = PaymentPayer.Sender, PaymentMethod method = PaymentMethod.Cash, int chargeableGrams = 1_500) => new()
    {
        Id = 10,
        TrackingNumber = "KV2610190001-2",
        ChargeableWeightGrams = chargeableGrams,
        Price = Money.FromHryvnias(157m),
        Payment = new Payment { Payer = payer, Method = method, Amount = Money.FromHryvnias(157m) },
    };

    /// <summary>Переводить замовлення в статус найкоротшим дозволеним шляхом (оплата не змінюється).</summary>
    public static Order InStatus(OrderStatus status, PaymentPayer payer = PaymentPayer.Recipient, PaymentMethod method = PaymentMethod.Cash)
    {
        Order order = NewOrder(payer, method);
        OrderStatus[] path = status switch
        {
            OrderStatus.New => [],
            OrderStatus.Assigned => [OrderStatus.Assigned],
            OrderStatus.PickedUp => [OrderStatus.Assigned, OrderStatus.PickedUp],
            OrderStatus.Delivered => [OrderStatus.Assigned, OrderStatus.PickedUp, OrderStatus.Delivered],
            OrderStatus.DeliveryFailed => [OrderStatus.Assigned, OrderStatus.PickedUp, OrderStatus.DeliveryFailed],
            OrderStatus.Returned => [OrderStatus.Assigned, OrderStatus.PickedUp, OrderStatus.DeliveryFailed, OrderStatus.Returned],
            OrderStatus.Cancelled => [OrderStatus.Cancelled],
            _ => throw new ArgumentOutOfRangeException(nameof(status)),
        };

        if (path.Length > 0)
        {
            order.CourierId = 1;
            order.Courier = Courier();
        }

        foreach (OrderStatus next in path)
        {
            order.ChangeStatus(next, Operator.UserId, Now);
        }

        order.StatusChanges.Clear();
        return order;
    }
}

public class OrderWorkflowTests
{
    private static readonly DateTime Now = WorkflowTestData.Now;
    private readonly OrderWorkflow _workflow = new();

    // Таблиця з розділу 5 плану
    private static readonly HashSet<(OrderStatus, OrderStatus)> Allowed =
    [
        (OrderStatus.New, OrderStatus.Assigned),
        (OrderStatus.Assigned, OrderStatus.New),
        (OrderStatus.Assigned, OrderStatus.PickedUp),
        (OrderStatus.PickedUp, OrderStatus.Delivered),
        (OrderStatus.PickedUp, OrderStatus.DeliveryFailed),
        (OrderStatus.DeliveryFailed, OrderStatus.Assigned),
        (OrderStatus.DeliveryFailed, OrderStatus.Returned),
        (OrderStatus.New, OrderStatus.Cancelled),
        (OrderStatus.Assigned, OrderStatus.Cancelled),
    ];

    public static TheoryData<OrderStatus, OrderStatus> AllPairs()
    {
        var data = new TheoryData<OrderStatus, OrderStatus>();
        foreach (OrderStatus from in Enum.GetValues<OrderStatus>())
        {
            foreach (OrderStatus to in Enum.GetValues<OrderStatus>())
            {
                data.Add(from, to);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(AllPairs))]
    public void EveryPairOfStatuses_IsAllowedOnlyIfInTable(OrderStatus from, OrderStatus to)
    {
        bool expected = Allowed.Contains((from, to));
        Order order = WorkflowTestData.InStatus(from);

        Assert.Equal(expected, OrderWorkflow.IsTransitionAllowed(from, to));
        if (expected)
        {
            order.ChangeStatus(to, 2, Now);
            Assert.Equal(to, order.Status);
        }
        else
        {
            Assert.Throws<InvalidStatusTransitionException>(() => order.ChangeStatus(to, 2, Now));
            Assert.Equal(from, order.Status);
            Assert.Empty(order.StatusChanges);
        }
    }

    [Theory]
    [InlineData(OrderStatus.Delivered)]
    [InlineData(OrderStatus.Cancelled)]
    [InlineData(OrderStatus.Returned)]
    public void FinalStatuses_HaveNoActionsForAnyone(OrderStatus status)
    {
        Order order = WorkflowTestData.InStatus(status);

        Assert.True(OrderWorkflow.IsFinal(status));
        Assert.Empty(_workflow.GetActions(order, WorkflowTestData.Operator));
        Assert.Empty(_workflow.GetActions(order, WorkflowTestData.Courier1));
        Assert.Empty(_workflow.GetActions(order, WorkflowTestData.Admin));
    }

    [Theory]
    [InlineData(OrderStatus.New, "Assign,Cancel")]
    [InlineData(OrderStatus.Assigned, "Unassign,Cancel")]
    [InlineData(OrderStatus.PickedUp, "")]
    [InlineData(OrderStatus.DeliveryFailed, "RetryDelivery,Return")]
    public void Operator_SeesActionsOfStatus(OrderStatus status, string expected)
    {
        var actions = _workflow.GetActions(WorkflowTestData.InStatus(status), WorkflowTestData.Operator);

        Assert.Equal(expected, string.Join(",", actions.Select(a => a.Action)));
    }

    [Theory]
    [InlineData(OrderStatus.Assigned, "PickUp")]
    [InlineData(OrderStatus.PickedUp, "Deliver,ReportFailure")]
    [InlineData(OrderStatus.DeliveryFailed, "")]
    public void OwnCourier_SeesDeliveryActions(OrderStatus status, string expected)
    {
        var actions = _workflow.GetActions(WorkflowTestData.InStatus(status), WorkflowTestData.Courier1);

        Assert.Equal(expected, string.Join(",", actions.Select(a => a.Action)));
    }

    [Theory]
    [InlineData(OrderStatus.Assigned)]
    [InlineData(OrderStatus.PickedUp)]
    public void OtherCourierAndAdmin_SeeNoActions(OrderStatus status)
    {
        Order order = WorkflowTestData.InStatus(status);

        Assert.Empty(_workflow.GetActions(order, WorkflowTestData.Courier2));
        Assert.Empty(_workflow.GetActions(order, WorkflowTestData.Admin));
    }

    [Fact]
    public void OtherCourier_CannotChangeOrder()
    {
        Order order = WorkflowTestData.InStatus(OrderStatus.Assigned);

        Assert.Throws<AccessDeniedException>(() =>
            _workflow.Execute(order, OrderAction.PickUp, WorkflowTestData.Courier2, Now));
        Assert.Equal(OrderStatus.Assigned, order.Status);
    }

    [Theory]
    [InlineData(OrderAction.PickUp)]
    [InlineData(OrderAction.Deliver)]
    public void Operator_CannotDoCourierActions(OrderAction action)
    {
        Order order = WorkflowTestData.InStatus(OrderStatus.PickedUp);

        Assert.Throws<AccessDeniedException>(() => _workflow.Execute(order, action, WorkflowTestData.Operator, Now));
    }

    [Fact]
    public void Admin_CannotAssign()
    {
        Assert.Throws<AccessDeniedException>(() => _workflow.Execute(
            WorkflowTestData.NewOrder(), OrderAction.Assign, WorkflowTestData.Admin, Now, WorkflowTestData.Courier()));
    }

    [Fact]
    public void ActionInWrongStatus_IsRejected()
    {
        Order order = WorkflowTestData.InStatus(OrderStatus.Assigned);

        var exception = Assert.Throws<InvalidStatusTransitionException>(() =>
            _workflow.Execute(order, OrderAction.Deliver, WorkflowTestData.Courier1, Now));
        Assert.Contains("Призначено", exception.UserMessage);
    }

    [Fact]
    public void Assign_SetsCourierAndWritesHistory()
    {
        Order order = WorkflowTestData.NewOrder();

        _workflow.Execute(order, OrderAction.Assign, WorkflowTestData.Operator, Now, WorkflowTestData.Courier(id: 2));

        Assert.Equal(OrderStatus.Assigned, order.Status);
        Assert.Equal(2, order.CourierId);
        var change = Assert.Single(order.StatusChanges);
        Assert.Equal((OrderStatus?)OrderStatus.New, change.FromStatus);
        Assert.Equal(OrderStatus.Assigned, change.ToStatus);
        Assert.Equal(WorkflowTestData.Operator.UserId, change.ChangedByUserId);
        Assert.Equal(Now, change.ChangedAt);
    }

    [Fact]
    public void Assign_RequiresActiveCourierThatCarriesTheWeight()
    {
        Order heavy = WorkflowTestData.NewOrder(chargeableGrams: 12_000);

        Assert.Throws<DomainValidationException>(() =>
            _workflow.Execute(heavy, OrderAction.Assign, WorkflowTestData.Operator, Now, courier: null));
        Assert.Throws<DomainValidationException>(() =>
            _workflow.Execute(heavy, OrderAction.Assign, WorkflowTestData.Operator, Now, WorkflowTestData.Courier(active: false)));
        var tooWeak = Assert.Throws<DomainValidationException>(() =>
            _workflow.Execute(heavy, OrderAction.Assign, WorkflowTestData.Operator, Now, WorkflowTestData.Courier(maxKg: 10)));
        Assert.Contains("12 кг", tooWeak.Message);
        Assert.Equal(OrderStatus.New, heavy.Status);

        // Рівно на межі вантажопідйомності — можна
        _workflow.Execute(heavy, OrderAction.Assign, WorkflowTestData.Operator, Now, WorkflowTestData.Courier(maxKg: 12));
        Assert.Equal(OrderStatus.Assigned, heavy.Status);
    }

    [Fact]
    public void Unassign_ClearsCourier()
    {
        Order order = WorkflowTestData.InStatus(OrderStatus.Assigned);

        _workflow.Execute(order, OrderAction.Unassign, WorkflowTestData.Operator, Now);

        Assert.Equal(OrderStatus.New, order.Status);
        Assert.Null(order.CourierId);
    }

    [Fact]
    public void PickUp_SenderCash_CourierConfirmsPayment()
    {
        Order order = WorkflowTestData.InStatus(OrderStatus.Assigned, PaymentPayer.Sender, PaymentMethod.Cash);

        OrderActionOption option = Assert.Single(_workflow.GetActions(order, WorkflowTestData.Courier1));
        Assert.True(option.IsEnabled);
        Assert.Contains("отримано 157,00 грн (готівка)", option.Title);

        _workflow.Execute(order, OrderAction.PickUp, WorkflowTestData.Courier1, Now);

        Assert.Equal(OrderStatus.PickedUp, order.Status);
        Assert.Equal(PaymentStatus.Paid, order.Payment!.Status);
        Assert.Equal(Now, order.Payment.PaidAt);
        Assert.Equal(WorkflowTestData.Courier1.UserId, order.Payment.ConfirmedByUserId);
    }

    [Theory]
    [InlineData(PaymentMethod.Card)]
    [InlineData(PaymentMethod.BankTransfer)]
    public void PickUp_SenderNonCash_BlockedUntilOperatorConfirmsPayment(PaymentMethod method)
    {
        Order order = WorkflowTestData.InStatus(OrderStatus.Assigned, PaymentPayer.Sender, method);

        OrderActionOption option = Assert.Single(_workflow.GetActions(order, WorkflowTestData.Courier1));
        Assert.False(option.IsEnabled);
        Assert.Contains("Оператор позначає оплату", option.BlockedReason);
        Assert.Throws<PaymentRequiredException>(() =>
            _workflow.Execute(order, OrderAction.PickUp, WorkflowTestData.Courier1, Now));

        // Оператор бачить і виконує позначку оплати; статус не змінюється
        Assert.Contains(_workflow.GetActions(order, WorkflowTestData.Operator), a => a.Action == OrderAction.ConfirmPayment);
        _workflow.Execute(order, OrderAction.ConfirmPayment, WorkflowTestData.Operator, Now);
        Assert.Equal(OrderStatus.Assigned, order.Status);
        Assert.Equal(PaymentStatus.Paid, order.Payment!.Status);
        Assert.Equal(WorkflowTestData.Operator.UserId, order.Payment.ConfirmedByUserId);
        Assert.Empty(order.StatusChanges);
        Assert.DoesNotContain(_workflow.GetActions(order, WorkflowTestData.Operator), a => a.Action == OrderAction.ConfirmPayment);

        _workflow.Execute(order, OrderAction.PickUp, WorkflowTestData.Courier1, Now);
        Assert.Equal(OrderStatus.PickedUp, order.Status);
    }

    [Theory]
    [InlineData(PaymentPayer.Sender, PaymentMethod.Cash)]
    [InlineData(PaymentPayer.Recipient, PaymentMethod.Cash)]
    [InlineData(PaymentPayer.Recipient, PaymentMethod.Card)]
    public void ConfirmPayment_NotOfferedWhenCourierCollects(PaymentPayer payer, PaymentMethod method)
    {
        Order order = WorkflowTestData.InStatus(OrderStatus.New, payer, method);

        Assert.DoesNotContain(_workflow.GetActions(order, WorkflowTestData.Operator), a => a.Action == OrderAction.ConfirmPayment);
        Assert.Throws<InvalidStatusTransitionException>(() =>
            _workflow.Execute(order, OrderAction.ConfirmPayment, WorkflowTestData.Operator, Now));
    }

    [Fact]
    public void Deliver_RecipientPays_CourierConfirmsPaymentAndPayoutIsFixed()
    {
        Order order = WorkflowTestData.InStatus(OrderStatus.PickedUp, PaymentPayer.Recipient, PaymentMethod.Card);
        DateTime deliveredAt = Now.AddHours(3);

        Assert.Contains("отримано 157,00 грн (картка)",
            _workflow.GetActions(order, WorkflowTestData.Courier1).Single(a => a.Action == OrderAction.Deliver).Title);
        _workflow.Execute(order, OrderAction.Deliver, WorkflowTestData.Courier1, deliveredAt);

        Assert.Equal(OrderStatus.Delivered, order.Status);
        Assert.Equal(deliveredAt, order.DeliveredAt);
        Assert.Equal(PaymentStatus.Paid, order.Payment!.Status);
        Assert.Equal(deliveredAt, order.Payment.PaidAt);
        Assert.Equal(Money.FromHryvnias(78.50m), order.CourierPayout);   // 50 % від 157 грн (D-15)
    }

    [Fact]
    public void Deliver_SenderAlreadyPaid_PaymentUnchanged()
    {
        Order order = WorkflowTestData.InStatus(OrderStatus.Assigned, PaymentPayer.Sender, PaymentMethod.Cash);
        _workflow.Execute(order, OrderAction.PickUp, WorkflowTestData.Courier1, Now);

        _workflow.Execute(order, OrderAction.Deliver, WorkflowTestData.Courier1, Now.AddHours(1));

        Assert.Equal(Now, order.Payment!.PaidAt);
        Assert.Equal(WorkflowTestData.Courier1.UserId, order.Payment.ConfirmedByUserId);
    }

    [Fact]
    public void Deliver_WhenNothingToCollect_TitleHasNoAmount()
    {
        Order order = WorkflowTestData.InStatus(OrderStatus.PickedUp, PaymentPayer.Sender);
        order.Payment!.Status = PaymentStatus.Paid;

        Assert.Equal("Доставив",
            _workflow.GetActions(order, WorkflowTestData.Courier1).Single(a => a.Action == OrderAction.Deliver).Title);
    }

    [Theory]
    [InlineData(40, 6280)]
    [InlineData(50, 7850)]
    [InlineData(35, 5495)]    // 54,95 грн
    public void CourierPayout_IsPercentOfPrice(int percent, long expectedKopecks)
    {
        Assert.Equal(expectedKopecks, OrderWorkflow.CourierPayout(Money.FromHryvnias(157m), percent).Kopecks);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    [InlineData("аб")]
    public void ReportFailure_RequiresReason(string? reason)
    {
        Order order = WorkflowTestData.InStatus(OrderStatus.PickedUp);

        Assert.Throws<DomainValidationException>(() =>
            _workflow.Execute(order, OrderAction.ReportFailure, WorkflowTestData.Courier1, Now, reason: reason));
        Assert.Equal(OrderStatus.PickedUp, order.Status);
    }

    [Fact]
    public void ReportFailure_StoresReasonInHistory()
    {
        Order order = WorkflowTestData.InStatus(OrderStatus.PickedUp);

        _workflow.Execute(order, OrderAction.ReportFailure, WorkflowTestData.Courier1, Now, reason: "  Одержувач не відповідає ");

        Assert.Equal(OrderStatus.DeliveryFailed, order.Status);
        Assert.Equal("Одержувач не відповідає", Assert.Single(order.StatusChanges).Reason);
        Assert.Null(order.CourierPayout);
    }

    [Fact]
    public void RetryDelivery_CanChooseAnotherCourier()
    {
        Order order = WorkflowTestData.InStatus(OrderStatus.DeliveryFailed);

        _workflow.Execute(order, OrderAction.RetryDelivery, WorkflowTestData.Operator, Now, WorkflowTestData.Courier(id: 2));

        Assert.Equal(OrderStatus.Assigned, order.Status);
        Assert.Equal(2, order.CourierId);
    }

    [Theory]
    [InlineData(PaymentPayer.Recipient, PaymentStatus.Pending, PaymentStatus.Cancelled)]
    [InlineData(PaymentPayer.Sender, PaymentStatus.Paid, PaymentStatus.Paid)]
    public void Return_CancelsOnlyUnpaidPayment(PaymentPayer payer, PaymentStatus before, PaymentStatus after)
    {
        Order order = WorkflowTestData.InStatus(OrderStatus.DeliveryFailed, payer);
        order.Payment!.Status = before;

        _workflow.Execute(order, OrderAction.Return, WorkflowTestData.Operator, Now);

        Assert.Equal(OrderStatus.Returned, order.Status);
        Assert.Equal(after, order.Payment.Status);
    }

    [Theory]
    [InlineData(OrderStatus.New, PaymentStatus.Pending, PaymentStatus.Cancelled)]
    [InlineData(OrderStatus.Assigned, PaymentStatus.Pending, PaymentStatus.Cancelled)]
    [InlineData(OrderStatus.Assigned, PaymentStatus.Paid, PaymentStatus.Refunded)]
    public void Cancel_CancelsOrRefundsPayment(OrderStatus status, PaymentStatus before, PaymentStatus after)
    {
        Order order = WorkflowTestData.InStatus(status, PaymentPayer.Sender, PaymentMethod.Card);
        order.Payment!.Status = before;

        _workflow.Execute(order, OrderAction.Cancel, WorkflowTestData.Operator, Now);

        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.Equal(after, order.Payment.Status);
    }

    [Fact]
    public void Cancel_AfterPickUp_IsRejected()
    {
        Order order = WorkflowTestData.InStatus(OrderStatus.PickedUp);

        Assert.Throws<InvalidStatusTransitionException>(() =>
            _workflow.Execute(order, OrderAction.Cancel, WorkflowTestData.Operator, Now));
    }
}
