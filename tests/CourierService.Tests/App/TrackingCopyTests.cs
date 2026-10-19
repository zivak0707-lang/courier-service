using CourierService.App.Services;
using CourierService.App.ViewModels.Orders;
using CourierService.Core.Enums;
using CourierService.Core.Orders;
using CourierService.Core.Pricing;
using CourierService.Core.Security;
using CourierService.Core.Sla;
using CourierService.Tests.Core;
using CourierService.Tests.Data;

namespace CourierService.Tests.App;

public class TrackingCopyTests
{
    private readonly FakeOrderService _orders = new();
    private readonly FakeDialogService _dialogs = new();
    private readonly FakeClipboardService _clipboard = new();
    private readonly FakeOrderDetailsDialog _details = new();
    private readonly CurrentUser _currentUser = new();

    private async Task<OrderDetailsViewModel> OpenCardAsync()
    {
        var order = WaybillTestData.FullOrder();
        _orders.Orders[order.Id] = order;
        _currentUser.SignIn(WorkflowTestData.Operator);
        var card = new OrderDetailsViewModel(order.Id, _orders, new FakeOrderActionService(), new OrderWorkflow(), _currentUser,
            _dialogs, new FakeEditDialogService(), new SlaEvaluator(new FixedClock(WorkflowTestData.Now)), new FakeWaybillPrinter(),
            _clipboard);
        await card.LoadCommand.ExecuteAsync(null);
        return card;
    }

    [Fact]
    public async Task Card_Copy_PutsTrackingNumberIntoClipboard_AndShowsCopied()
    {
        var card = await OpenCardAsync();
        Assert.False(card.IsTrackingNumberCopied);

        card.CopyTrackingNumberCommand.Execute(null);

        Assert.Equal(["KV2610190001-2"], _clipboard.Copied);
        Assert.True(card.IsTrackingNumberCopied);
    }

    [Fact]
    public async Task Card_ClipboardBusy_ExplainsAndDoesNotClaimCopied()
    {
        var card = await OpenCardAsync();
        _clipboard.IsBusy = true;

        card.CopyTrackingNumberCommand.Execute(null);

        Assert.False(card.IsTrackingNumberCopied);
        Assert.Equal(TrackingNumberCopy.ClipboardBusyMessage, Assert.Single(_dialogs.Warnings));
    }

    [Fact]
    public void Card_BeforeLoad_CopyIsUnavailable()
    {
        _currentUser.SignIn(WorkflowTestData.Operator);
        var card = new OrderDetailsViewModel(1, _orders, new FakeOrderActionService(), new OrderWorkflow(), _currentUser,
            _dialogs, new FakeEditDialogService(), new SlaEvaluator(new FixedClock(WorkflowTestData.Now)), new FakeWaybillPrinter(),
            _clipboard);

        Assert.False(card.CopyTrackingNumberCommand.CanExecute(null));
    }

    [Fact]
    public async Task Search_CopiesFoundNumber_AndNewSearchResetsCopiedMark()
    {
        _orders.SearchResult = new([OrderListItemFor("KV2610190001-2")], false, null);
        var page = new TrackingSearchViewModel(_orders, _dialogs, _details, _clipboard) { TrackingNumber = "kv2610190001-2" };
        Assert.False(page.CopyTrackingNumberCommand.CanExecute(null));
        await page.SearchCommand.ExecuteAsync(null);

        page.CopyTrackingNumberCommand.Execute(null);

        Assert.Equal(["KV2610190001-2"], _clipboard.Copied);
        Assert.True(page.IsTrackingNumberCopied);

        await page.SearchCommand.ExecuteAsync(null);
        Assert.False(page.IsTrackingNumberCopied);
    }

    [Fact]
    public void List_CopiesClickedOrSelectedOrder()
    {
        var page = new MyActiveDeliveriesViewModel(_orders, _details, _dialogs, _clipboard);
        var first = OrderListItemFor("KV2610190001-2");
        var second = OrderListItemFor("KV2610190002-0");

        page.CopyTrackingNumberCommand.Execute(null);   // нічого не виділено
        page.CopyTrackingNumberCommand.Execute(first);  // меню правої кнопки на рядку
        page.SelectedOrder = second;
        page.CopyTrackingNumberCommand.Execute(null);   // Ctrl+C на виділеному рядку

        Assert.Equal(["KV2610190001-2", "KV2610190002-0"], _clipboard.Copied);
    }

    [Fact]
    public async Task NewOrderMessage_OffersToCopyTrackingNumber()
    {
        var data = OrderTestData.Create();
        var editDialogs = new FakeEditDialogService { Result = true };
        editDialogs.OnShow = form =>
        {
            var editor = (OrderEditorViewModel)form;
            editor.ClientId = 1;
            editor.RecipientName = "Оксана Мельник";
            editor.RecipientPhone = "050 123 45 67";
            editor.DeliveryAddress = "просп. Умовний, 10";
            editor.PickupZoneId = OrderTestData.ZoneId(data, "Шевченківський");
            editor.DeliveryZoneId = OrderTestData.ZoneId(data, "Дарницький");
            editor.WeightGrams = "1,5";
            editor.LengthCm = "30";
            editor.WidthCm = "20";
            editor.HeightCm = "10";
            editor.SaveCommand.ExecuteAsync(null).GetAwaiter().GetResult();
        };
        var launcher = new OrderFormLauncher(
            new FakeClientService { Items = [.. data.Clients] }, new FakeZoneService { Items = [.. data.Zones] },
            new FakeTariffService { Items = [.. data.Tariffs] }, new FakeOrderService(), new OrderValidator(),
            new PriceCalculator(), new DeadlineCalculator(), new FixedClock(new DateTime(2026, 10, 19, 12, 0, 0)), editDialogs, _dialogs);

        await launcher.OpenNewOrderAsync();

        var (message, textToCopy) = Assert.Single(_dialogs.InfosWithCopy);
        Assert.Contains("Трек-номер: KV2610190001-2", message);
        Assert.Equal("KV2610190001-2", textToCopy);
    }

    private static OrderListItem OrderListItemFor(string trackingNumber) =>
        new(1, trackingNumber, WorkflowTestData.Now, OrderStatus.Assigned, "Клієнт", "+380501234567", "Одержувач", "+380501112233",
            "Шевченківський", "Дарницький", "Стандарт", global::CourierService.Core.Money.FromHryvnias(157m),
            PaymentStatus.Pending, PaymentPayer.Sender, PaymentMethod.Cash, null, WorkflowTestData.Now.AddHours(4), null,
            "вул. Липова, 5", "вул. Каштанова, 12");
}
