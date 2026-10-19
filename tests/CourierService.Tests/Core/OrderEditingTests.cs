using CourierService.Core.Entities;
using CourierService.Core.Enums;
using CourierService.Core.Exceptions;
using CourierService.Core.Orders;

namespace CourierService.Tests.Core;

public class OrderEditingTests
{
    private static Order Order(OrderStatus status = OrderStatus.New)
    {
        Order order = WorkflowTestData.InStatus(status);
        order.RecipientName = "Оксана Мельник";
        order.RecipientPhone = "+380501234567";
        order.PickupAddress = "вул. Липова, 5";
        order.DeliveryAddress = "вул. Каштанова, 15";
        order.Comment = null;
        return order;
    }

    private static OrderDetailsEdit Edit(Order order) =>
        new(order.Id, order.RecipientName, order.RecipientPhone, order.PickupAddress, order.DeliveryAddress, order.Comment);

    [Theory]
    [InlineData(OrderStatus.New, UserRole.Operator, true)]
    [InlineData(OrderStatus.Assigned, UserRole.Operator, true)]
    [InlineData(OrderStatus.PickedUp, UserRole.Operator, false)]
    [InlineData(OrderStatus.Delivered, UserRole.Operator, false)]
    [InlineData(OrderStatus.Cancelled, UserRole.Operator, false)]
    [InlineData(OrderStatus.New, UserRole.Admin, false)]
    [InlineData(OrderStatus.Assigned, UserRole.Courier, false)]
    public void CanEdit_OnlyOperatorBeforePickup(OrderStatus status, UserRole role, bool expected)
    {
        var user = role switch
        {
            UserRole.Operator => WorkflowTestData.Operator,
            UserRole.Admin => WorkflowTestData.Admin,
            _ => WorkflowTestData.Courier1,
        };

        Assert.Equal(expected, OrderEditing.CanEdit(status, user));
    }

    [Fact]
    public void Apply_ChangesFieldsAndWritesHistoryWithoutChangingStatus()
    {
        Order order = Order(OrderStatus.Assigned);
        var edit = Edit(order) with { RecipientPhone = "067 000 11 22", DeliveryAddress = " вул. Каштанова, 17 ", Comment = " Дзвінок за 30 хв " };

        bool changed = OrderEditing.Apply(order, edit, WorkflowTestData.Operator.UserId, WorkflowTestData.Now);

        Assert.True(changed);
        Assert.Equal("+380670001122", order.RecipientPhone);
        Assert.Equal("вул. Каштанова, 17", order.DeliveryAddress);
        Assert.Equal("Дзвінок за 30 хв", order.Comment);
        Assert.Equal(OrderStatus.Assigned, order.Status);

        OrderStatusChange entry = Assert.Single(order.StatusChanges);
        Assert.Equal(OrderStatus.Assigned, entry.FromStatus);
        Assert.Equal(OrderStatus.Assigned, entry.ToStatus);
        Assert.Equal(WorkflowTestData.Operator.UserId, entry.ChangedByUserId);
        Assert.Equal(
            "Змінено: телефон одержувача «+380 (50) 123-45-67» → «+380 (67) 000-11-22»; "
            + "куди доставити «вул. Каштанова, 15» → «вул. Каштанова, 17»; коментар",
            entry.Reason);
    }

    [Fact]
    public void Apply_SameDataInOtherFormat_ChangesNothing()
    {
        Order order = Order();
        var edit = Edit(order) with { RecipientPhone = "050 123 45 67", PickupAddress = " вул. Липова, 5 ", Comment = "  " };

        Assert.False(OrderEditing.Apply(order, edit, WorkflowTestData.Operator.UserId, WorkflowTestData.Now));
        Assert.Empty(order.StatusChanges);
    }

    [Fact]
    public void Apply_AfterPickup_IsRejected()
    {
        Order order = Order(OrderStatus.PickedUp);
        var edit = Edit(order) with { DeliveryAddress = "вул. Інша, 1" };

        var exception = Assert.Throws<InvalidStatusTransitionException>(() =>
            OrderEditing.Apply(order, edit, WorkflowTestData.Operator.UserId, WorkflowTestData.Now));

        Assert.Contains("Забрано", exception.UserMessage);
        Assert.Equal("вул. Каштанова, 15", order.DeliveryAddress);
    }

    [Fact]
    public void Describe_LongText_IsCutToReasonLength()
    {
        string text = OrderEditing.Describe([new string('а', 300), new string('б', 300)]);

        Assert.Equal(OrderWorkflow.ReasonMaxLength, text.Length);
        Assert.EndsWith("…", text);
    }

    [Fact]
    public void Validator_ReportsEveryInvalidField()
    {
        var edit = new OrderDetailsEdit(1, " ", "123", "", new string('x', 201), new string('x', 501));

        var errors = new OrderDetailsEditValidator().Validate(edit);

        string[] expected =
        [
            nameof(edit.RecipientName), nameof(edit.RecipientPhone), nameof(edit.PickupAddress),
            nameof(edit.DeliveryAddress), nameof(edit.Comment),
        ];
        Assert.Equal(expected.Order(), errors.PropertyNames.Order());
    }
}
