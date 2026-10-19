using CourierService.App.ViewModels.Orders;
using CourierService.Core.Enums;

namespace CourierService.Tests.App;

public class MyDeliveriesViewModelTests
{
    private readonly FakeOrderService _orders = new()
    {
        MyActive = [OrderListTestData.Item("KV2610190001-2", OrderStatus.Assigned)],
        MyCompleted = [OrderListTestData.Item("KV2610180007-1", OrderStatus.Delivered)],
    };

    private readonly FakeOrderDetailsDialog _details = new();
    private readonly FakeDialogService _dialogs = new();

    [Fact]
    public async Task ActivePage_LoadsActiveDeliveries()
    {
        var page = new MyActiveDeliveriesViewModel(_orders, _details, _dialogs);

        await page.LoadCommand.ExecuteAsync(null);

        Assert.Equal([true], _orders.MyDeliveryRequests);
        Assert.Equal("KV2610190001-2", Assert.Single(page.Items).TrackingNumber);
        Assert.False(page.IsEmpty);
        Assert.Equal("Мої доставки: активні", page.Title);
    }

    [Fact]
    public async Task CompletedPage_LoadsHistory()
    {
        var page = new MyCompletedDeliveriesViewModel(_orders, _details, _dialogs);

        await page.LoadCommand.ExecuteAsync(null);

        Assert.Equal([false], _orders.MyDeliveryRequests);
        Assert.Equal(OrderStatus.Delivered, Assert.Single(page.Items).Status);
    }

    [Fact]
    public async Task EmptyList_ShowsHint()
    {
        _orders.MyActive = [];
        var page = new MyActiveDeliveriesViewModel(_orders, _details, _dialogs);

        await page.LoadCommand.ExecuteAsync(null);

        Assert.True(page.IsEmpty);
        Assert.Contains("немає", page.EmptyText);
    }

    [Fact]
    public async Task ActionInCard_ReloadsList()
    {
        var page = new MyActiveDeliveriesViewModel(_orders, _details, _dialogs);
        await page.LoadCommand.ExecuteAsync(null);
        Assert.False(page.OpenOrderCommand.CanExecute(null));

        page.SelectedOrder = page.Items[0];
        _details.Result = true;
        await page.OpenOrderCommand.ExecuteAsync(null);

        Assert.Equal([1], _details.Opened);
        Assert.Equal([true, true], _orders.MyDeliveryRequests);
    }

    [Fact]
    public async Task CardClosedWithoutChanges_NoReload()
    {
        var page = new MyActiveDeliveriesViewModel(_orders, _details, _dialogs);
        await page.LoadCommand.ExecuteAsync(null);
        page.SelectedOrder = page.Items[0];

        await page.OpenOrderCommand.ExecuteAsync(null);

        Assert.Single(_orders.MyDeliveryRequests);
    }
}
