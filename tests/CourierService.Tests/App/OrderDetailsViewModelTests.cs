using CourierService.App.Services;
using CourierService.App.ViewModels.Orders;
using CourierService.Core;
using CourierService.Core.Assignment;
using CourierService.Core.Entities;
using CourierService.Core.Enums;
using CourierService.Core.Exceptions;
using CourierService.Core.Orders;
using CourierService.Core.Security;
using CourierService.Core.Sla;
using CourierService.Tests.Core;
using CourierService.Tests.Data;

namespace CourierService.Tests.App;

/// <summary>Заглушка сервісу дій: запам'ятовує виклики, може кинути виняток.</summary>
internal sealed class FakeOrderActionService : IOrderActionService
{
    public List<(int OrderId, OrderAction Action, int? CourierId, string? Reason)> Executed { get; } = [];
    public Exception? ThrowOnExecute { get; set; }
    public AssignmentOptions Options { get; set; } = new([], new AssignmentSuggestion(null, "Немає активних кур'єрів."));

    public Task ExecuteAsync(int orderId, OrderAction action, int? courierId = null, string? reason = null, CancellationToken cancellationToken = default)
    {
        if (ThrowOnExecute is not null)
        {
            return Task.FromException(ThrowOnExecute);
        }

        Executed.Add((orderId, action, courierId, reason));
        return Task.CompletedTask;
    }

    public Task<AssignmentOptions> GetAssignmentOptionsAsync(int orderId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Options);
}

public class OrderDetailsViewModelTests
{
    private readonly FakeOrderService _orders = new();
    private readonly FakeOrderActionService _actions = new();
    private readonly FakeDialogService _dialogs = new();
    private readonly FakeEditDialogService _editDialogs = new();
    private readonly CurrentUser _currentUser = new();
    private readonly FakeWaybillPrinter _waybills = new();
    private readonly FakeClipboardService _clipboard = new();

    private OrderDetailsViewModel CreateCard(Order order, UserSession user)
    {
        _orders.Orders[order.Id] = order;
        _currentUser.SignIn(user);
        return new OrderDetailsViewModel(order.Id, _orders, _actions, new OrderWorkflow(), _currentUser, _dialogs, _editDialogs, new SlaEvaluator(new FixedClock(WorkflowTestData.Now)), _waybills, _clipboard);
    }

    private static Order NewOrderWithHistory()
    {
        Order order = WorkflowTestData.NewOrder(PaymentPayer.Sender, PaymentMethod.Card);
        order.StatusChanges.Add(new OrderStatusChange
        {
            Id = 1, ToStatus = OrderStatus.New, ChangedAt = WorkflowTestData.Now,
            ChangedBy = new User { FullName = "Ігор Бондар" },
        });
        return order;
    }

    [Fact]
    public async Task Load_ShowsHistoryAndOperatorActions()
    {
        Order order = NewOrderWithHistory();
        order.ChangeStatus(OrderStatus.Assigned, 2, WorkflowTestData.Now.AddMinutes(5));
        order.StatusChanges.Last().ChangedBy = new User { FullName = "Ігор Бондар" };
        order.CourierId = 1;
        var card = CreateCard(order, WorkflowTestData.Operator);

        await card.LoadCommand.ExecuteAsync(null);

        Assert.Equal("Замовлення KV2610190001-2", card.Title);
        Assert.Equal(["Нове → Призначено", "Оформлено"], card.History.Select(h => h.Change));
        Assert.Equal("Ігор Бондар", card.History[0].ChangedBy);
        Assert.Equal(
            [OrderAction.Unassign, OrderAction.ConfirmPayment, OrderAction.Cancel],
            card.Actions.Select(a => a.Action));
        Assert.True(card.HasActions);
    }

    [Fact]
    public async Task Features_DashWhenNothingDeclared_ListWhenPresent()
    {
        Order order = NewOrderWithHistory();
        order.DeclaredValue = Money.Zero;
        order.IsFragile = false;
        var card = CreateCard(order, WorkflowTestData.Operator);
        await card.LoadCommand.ExecuteAsync(null);
        Assert.Equal("—", card.FeaturesText);

        order.DeclaredValue = Money.FromHryvnias(500m);
        order.IsFragile = true;
        await card.LoadCommand.ExecuteAsync(null);
        Assert.Equal($"цінність {Money.FromHryvnias(500m)}, крихка", card.FeaturesText);
    }

    [Fact]
    public async Task CancelledOrder_HidesDeadlineBadge()
    {
        Order order = NewOrderWithHistory();
        order.ChangeStatus(OrderStatus.Cancelled, 2, WorkflowTestData.Now.AddMinutes(5));
        var card = CreateCard(order, WorkflowTestData.Operator);

        await card.LoadCommand.ExecuteAsync(null);

        Assert.False(card.IsSlaTracked);
    }

    [Fact]
    public async Task Admin_SeesCardWithoutActions()
    {
        var card = CreateCard(NewOrderWithHistory(), WorkflowTestData.Admin);

        await card.LoadCommand.ExecuteAsync(null);

        Assert.NotNull(card.Order);
        Assert.False(card.HasActions);
    }

    [Fact]
    public async Task NotFound_ShowsError()
    {
        _currentUser.SignIn(WorkflowTestData.Operator);
        var card = new OrderDetailsViewModel(404, _orders, _actions, new OrderWorkflow(), _currentUser, _dialogs, _editDialogs, new SlaEvaluator(new FixedClock(WorkflowTestData.Now)), _waybills, _clipboard);

        await card.LoadCommand.ExecuteAsync(null);

        Assert.Contains("не знайдено", Assert.Single(_dialogs.Errors));
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 1)]
    public async Task Cancel_AsksConfirmationFirst(bool confirmed, int expectedCalls)
    {
        var card = CreateCard(NewOrderWithHistory(), WorkflowTestData.Operator);
        await card.LoadCommand.ExecuteAsync(null);
        _dialogs.ConfirmResult = confirmed;

        await card.ExecuteActionCommand.ExecuteAsync(card.Actions.Single(a => a.Action == OrderAction.Cancel));

        Assert.Contains("Скасувати замовлення KV2610190001-2", Assert.Single(_dialogs.Questions));
        Assert.Equal(expectedCalls, _actions.Executed.Count);
        Assert.Equal(confirmed, card.HasChanges);
    }

    [Fact]
    public void ConfirmationText_PaidOrderCancel_MentionsRefund()
    {
        Order order = WorkflowTestData.NewOrder(PaymentPayer.Sender, PaymentMethod.Card);
        order.Payment!.Status = PaymentStatus.Paid;

        string text = OrderDetailsViewModel.ConfirmationText(order, new OrderActionOption(OrderAction.Cancel, "Скасувати замовлення", null));

        Assert.Contains("157,00 грн буде позначено як повернену", text);
    }

    [Fact]
    public async Task Assign_OpensCourierChoiceWithSuggestion()
    {
        var suggested = new CourierCandidate(3, "Андрій Ткаченко", 1, "Шевченківський", RiverBank.Right, 30_000, 2, true);
        _actions.Options = new AssignmentOptions(
            [suggested, new CourierCandidate(4, "Марія Лисенко", 6, "Оболонський", RiverBank.Right, 10_000, 8, true),
             new CourierCandidate(5, "Звільнений Кур'єр", 6, "Оболонський", RiverBank.Right, 10_000, 0, false)],
            new AssignmentSuggestion(suggested, "Андрій Ткаченко: та сама зона (Шевченківський), 2 активні доставки."));
        _editDialogs.Result = true;
        var card = CreateCard(NewOrderWithHistory(), WorkflowTestData.Operator);
        await card.LoadCommand.ExecuteAsync(null);

        await card.ExecuteActionCommand.ExecuteAsync(card.Actions.Single(a => a.Action == OrderAction.Assign));

        var editor = Assert.IsType<AssignCourierViewModel>(Assert.Single(_editDialogs.Editors));
        Assert.Equal(3, editor.CourierId);
        Assert.Contains("та сама зона", editor.Suggestion);
        Assert.Equal(2, editor.Couriers.Count);   // неактивного не пропонуємо
        Assert.EndsWith("перевантажений", editor.Couriers[1].Display);
        Assert.True(card.HasChanges);
    }

    [Fact]
    public async Task ReportFailure_OpensReasonForm()
    {
        Order order = WorkflowTestData.InStatus(OrderStatus.PickedUp);
        var card = CreateCard(order, WorkflowTestData.Courier1);
        await card.LoadCommand.ExecuteAsync(null);

        await card.ExecuteActionCommand.ExecuteAsync(card.Actions.Single(a => a.Action == OrderAction.ReportFailure));

        Assert.IsType<FailureReasonViewModel>(Assert.Single(_editDialogs.Editors));
        Assert.False(card.HasChanges);   // форму закрито без збереження
    }

    [Fact]
    public async Task BlockedAction_ExplainsWithoutExecuting()
    {
        Order order = WorkflowTestData.InStatus(OrderStatus.Assigned, PaymentPayer.Sender, PaymentMethod.Card);
        var card = CreateCard(order, WorkflowTestData.Courier1);
        await card.LoadCommand.ExecuteAsync(null);

        await card.ExecuteActionCommand.ExecuteAsync(Assert.Single(card.Actions));

        Assert.Contains("Відправник ще не оплатив", Assert.Single(_dialogs.Warnings));
        Assert.Empty(_actions.Executed);
    }

    [Fact]
    public async Task ServiceRejection_IsShownAndCardReloaded()
    {
        Order order = WorkflowTestData.InStatus(OrderStatus.Assigned, PaymentPayer.Sender, PaymentMethod.Cash);
        var card = CreateCard(order, WorkflowTestData.Courier1);
        await card.LoadCommand.ExecuteAsync(null);
        _dialogs.ConfirmResult = true;
        _actions.ThrowOnExecute = new InvalidStatusTransitionException("Статус уже змінено.");

        await card.ExecuteActionCommand.ExecuteAsync(card.Actions.Single());

        Assert.Equal("Статус уже змінено.", Assert.Single(_dialogs.Warnings));
        Assert.False(card.HasChanges);
    }

    [Fact]
    public void PriceLines_FromSnapshot_OnlyNonZeroComponents()
    {
        var order = new Order
        {
            ZoneCoefficientPercent = 120,
            ChargeableWeightGrams = 6_000,
            DeclaredValue = Money.FromHryvnias(2000m),
            BaseCost = Money.FromHryvnias(132m),
            WeightCost = Money.FromHryvnias(57.60m),
            CrossRiverCost = Money.FromHryvnias(25m),
            InsuranceCost = Money.FromHryvnias(7.50m),
        };

        Assert.Equal(
            ["Базова ціна × 1,20", "Додаткова вага (розрахункова 6 кг) × 1,20", "Переїзд через Дніпро", $"Страхування (цінність {Money.FromHryvnias(2000m)})"],
            OrderDetailsViewModel.PriceLinesFor(order).Select(line => line.Label));
    }
}

public class OrderActionEditorTests
{
    private readonly FakeOrderActionService _actions = new();

    [Fact]
    public async Task AssignCourier_SavesChosenCourier_AndShowsServiceErrorNextToField()
    {
        var options = new AssignmentOptions(
            [new CourierCandidate(4, "Марія Лисенко", 6, "Оболонський", RiverBank.Right, 10_000, 0, true)],
            new AssignmentSuggestion(null, "Жоден активний кур'єр не перевозить 12 кг."));
        var editor = new AssignCourierViewModel(_actions, 10, OrderAction.Assign, "KV2610190001-2", options);

        await editor.SaveCommand.ExecuteAsync(null);
        Assert.NotEmpty(editor.GetErrors(nameof(AssignCourierViewModel.CourierId)).Cast<string>());
        Assert.Contains("Автопризначення", editor.Suggestion);

        _actions.ThrowOnExecute = new DomainValidationException("Кур'єр перевозить до 10 кг.", "CourierId");
        editor.CourierId = 4;
        await editor.SaveCommand.ExecuteAsync(null);
        Assert.Equal("Кур'єр перевозить до 10 кг.", editor.GetErrors("CourierId").Cast<string>().Single());

        _actions.ThrowOnExecute = null;
        await editor.SaveCommand.ExecuteAsync(null);
        Assert.Equal((10, OrderAction.Assign, (int?)4, (string?)null), Assert.Single(_actions.Executed));
    }

    [Fact]
    public async Task FailureReason_RequiredAndQuickChoiceWorks()
    {
        var editor = new FailureReasonViewModel(_actions, 10, "KV2610190001-2");

        await editor.SaveCommand.ExecuteAsync(null);
        Assert.NotEmpty(editor.GetErrors(nameof(FailureReasonViewModel.Reason)).Cast<string>());
        Assert.Empty(_actions.Executed);

        editor.UseReasonCommand.Execute(editor.TypicalReasons[0]);
        await editor.SaveCommand.ExecuteAsync(null);

        var call = Assert.Single(_actions.Executed);
        Assert.Equal(OrderAction.ReportFailure, call.Action);
        Assert.Equal("Одержувач не відповідає на дзвінки", call.Reason);
    }
}

public class OrderDetailsSlaTests
{
    [Fact]
    public async Task Card_ShowsSlaOfOrder()
    {
        var orders = new FakeOrderService();
        Order order = WorkflowTestData.NewOrder();
        order.CreatedAt = WorkflowTestData.Now;
        order.Deadline = WorkflowTestData.Now.AddHours(4);
        orders.Orders[order.Id] = order;
        var currentUser = new CurrentUser();
        currentUser.SignIn(WorkflowTestData.Operator);
        var clock = new FixedClock(WorkflowTestData.Now.AddHours(3.5));   // лишилося 30 хв із 4 год
        var card = new OrderDetailsViewModel(order.Id, orders, new FakeOrderActionService(), new OrderWorkflow(), currentUser,
            new FakeDialogService(), new FakeEditDialogService(), new SlaEvaluator(clock), new FakeWaybillPrinter(), new FakeClipboardService());

        await card.LoadCommand.ExecuteAsync(null);

        Assert.Equal(SlaState.AtRisk, card.Sla.State);
        Assert.Equal("Під загрозою: лишилося 30 хв", card.Sla.Text);
    }
}

/// <summary>Заглушка друку накладної: запам'ятовує показані накладні.</summary>
internal sealed class FakeWaybillPrinter : IWaybillPrinter
{
    public List<Waybill> Shown { get; } = [];

    public void ShowPreview(Waybill waybill) => Shown.Add(waybill);
}

public class OrderDetailsWaybillTests
{
    private readonly FakeOrderService _orders = new();
    private readonly FakeWaybillPrinter _waybills = new();
    private readonly FakeClipboardService _clipboard = new();
    private readonly CurrentUser _currentUser = new();

    private async Task<OrderDetailsViewModel> OpenCardAsync(Order order, UserSession user)
    {
        _orders.Orders[order.Id] = order;
        _currentUser.SignIn(user);
        var card = new OrderDetailsViewModel(order.Id, _orders, new FakeOrderActionService(), new OrderWorkflow(), _currentUser,
            new FakeDialogService(), new FakeEditDialogService(), new SlaEvaluator(new FixedClock(WorkflowTestData.Now)), _waybills, _clipboard);
        await card.LoadCommand.ExecuteAsync(null);
        return card;
    }

    [Fact]
    public async Task PrintWaybill_Operator_ShowsPreviewOfThisOrder()
    {
        var card = await OpenCardAsync(WaybillTestData.FullOrder(), WorkflowTestData.Operator);

        Assert.True(card.CanPrintWaybill);
        card.PrintWaybillCommand.Execute(null);

        Assert.Equal("KV2610190001-2", Assert.Single(_waybills.Shown).TrackingNumber);
    }

    [Fact]
    public async Task PrintWaybill_Admin_IsAvailable()
    {
        var card = await OpenCardAsync(WaybillTestData.FullOrder(OrderStatus.Delivered), WorkflowTestData.Admin);

        Assert.True(card.PrintWaybillCommand.CanExecute(null));
    }

    [Fact]
    public async Task PrintWaybill_Courier_IsHidden()
    {
        Order order = WaybillTestData.FullOrder(OrderStatus.Assigned);
        var card = await OpenCardAsync(order, WorkflowTestData.Courier1);

        Assert.False(card.CanPrintWaybill);
        Assert.False(card.PrintWaybillCommand.CanExecute(null));
    }

    [Fact]
    public async Task PrintWaybill_CancelledOrder_IsHidden()
    {
        var card = await OpenCardAsync(WaybillTestData.FullOrder(OrderStatus.Cancelled), WorkflowTestData.Operator);

        Assert.False(card.CanPrintWaybill);
        Assert.False(card.PrintWaybillCommand.CanExecute(null));
    }

    [Fact]
    public void PrintWaybill_BeforeLoad_IsUnavailable()
    {
        _currentUser.SignIn(WorkflowTestData.Operator);
        var card = new OrderDetailsViewModel(1, _orders, new FakeOrderActionService(), new OrderWorkflow(), _currentUser,
            new FakeDialogService(), new FakeEditDialogService(), new SlaEvaluator(new FixedClock(WorkflowTestData.Now)), _waybills, _clipboard);

        Assert.False(card.PrintWaybillCommand.CanExecute(null));
    }
}
