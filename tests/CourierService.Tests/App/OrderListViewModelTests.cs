using CourierService.App.ViewModels.Orders;
using CourierService.Core;
using CourierService.Core.Directories;
using CourierService.Core.Entities;
using CourierService.Core.Enums;
using CourierService.Core.Exceptions;
using CourierService.Core.Orders;
using CourierService.Core.Security;
using CourierService.Tests.Data;

namespace CourierService.Tests.App;

/// <summary>Заглушка форми замовлення з заданим результатом.</summary>
internal sealed class StubOrderFormLauncher(OrderCreated? result) : IOrderFormLauncher
{
    public int Calls { get; private set; }

    public Task<OrderCreated?> OpenNewOrderAsync()
    {
        Calls++;
        return Task.FromResult(result);
    }
}

internal static class OrderListTestData
{
    public static OrderListItem Item(string trackingNumber, OrderStatus status = OrderStatus.New) => new(
        1, trackingNumber, new DateTime(2026, 10, 19, 10, 0, 0), status, "Наталія Руденко", "+380930000204",
        "Оксана Мельник", "+380501234567", "Шевченківський", "Дарницький", "Стандарт", Money.FromHryvnias(157m),
        PaymentStatus.Pending, PaymentPayer.Sender, PaymentMethod.Cash, null, new DateTime(2026, 10, 20, 12, 0, 0), null,
        "вул. Яблунева, 3, кв. 17", "вул. Харківське шосе, 10");
}

public class OrderListViewModelTests
{
    private static readonly DateTime Now = new(2026, 10, 19, 12, 0, 0);

    private readonly FakeOrderService _orders = new();
    private readonly FakeDialogService _dialogs = new();
    private readonly FakeOrderDetailsDialog _details = new();
    private readonly FakeClipboardService _clipboard = new();
    private readonly FakeClientService _clients = new()
    {
        Items = [new Client { Id = 7, Name = "Наталія Руденко", Phone = "+380930000204" }],
    };
    private readonly FakeCourierService _couriers = new()
    {
        Items = [new CourierListItem(3, 10, "Андрій Ткаченко", "courier1", "+380500000101", TransportType.Car, 30_000, 1, "Шевченківський", 50, true)],
    };

    private OrderListViewModel CreateList(UserRole role = UserRole.Operator, IOrderFormLauncher? launcher = null)
    {
        var currentUser = new CurrentUser();
        currentUser.SignIn(new UserSession(1, "user", "Користувач", role, null));
        return new OrderListViewModel(_orders, _clients, _couriers, launcher ?? new FakeOrderFormLauncher(), _details,
            _dialogs, currentUser, new FixedClock(Now), _clipboard);
    }

    [Fact]
    public async Task Load_SearchesLastThirtyDaysWithoutOtherConditions()
    {
        var list = CreateList();

        await list.LoadCommand.ExecuteAsync(null);

        OrderFilter filter = Assert.Single(_orders.Searches);
        Assert.Equal(OrderFilter.LastDays(Now), filter with { Query = null });
        Assert.True(string.IsNullOrEmpty(filter.Query));
        Assert.Equal(["Усі", "Наталія Руденко"], list.Clients.Select(o => o.Display));
        Assert.Equal(["Усі", "Андрій Ткаченко"], list.Couriers.Select(o => o.Display));
    }

    [Fact]
    public async Task ChangingFilter_SearchesImmediatelyWithThatCondition()
    {
        var list = CreateList();
        await list.LoadCommand.ExecuteAsync(null);

        list.StatusFilter = list.Statuses.Single(o => o.Value == OrderStatus.Assigned);
        list.CourierFilter = list.Couriers[1];
        list.ClientFilter = list.Clients[1];
        list.PaymentFilter = list.PaymentStatuses.Single(o => o.Value == PaymentStatus.Paid);

        OrderFilter last = _orders.Searches[^1];
        Assert.Equal(5, _orders.Searches.Count);
        Assert.Equal(OrderStatus.Assigned, last.Status);
        Assert.Equal(3, last.CourierId);
        Assert.Equal(7, last.ClientId);
        Assert.Equal(PaymentStatus.Paid, last.PaymentStatus);
    }

    [Fact]
    public async Task TypingSearchText_DoesNotQueryUntilSearchCommand()
    {
        var list = CreateList();
        await list.LoadCommand.ExecuteAsync(null);

        list.SearchText = "050 12";
        Assert.Single(_orders.Searches);

        await list.SearchCommand.ExecuteAsync(null);
        Assert.Equal("050 12", _orders.Searches[^1].Query);
    }

    [Fact]
    public async Task Result_ShowsItemsNoticeAndCount()
    {
        _orders.SearchResult = new([OrderListTestData.Item("KV2610190001-2")], true, "Уточніть фільтр");
        var list = CreateList();

        await list.LoadCommand.ExecuteAsync(null);

        Assert.Single(list.Items);
        Assert.Equal("Уточніть фільтр", list.Notice);
        Assert.Contains("більше", list.CountText);
    }

    [Fact]
    public async Task ResetFilters_RestoresDefaultsWithSingleQuery()
    {
        var list = CreateList();
        await list.LoadCommand.ExecuteAsync(null);
        list.SearchText = "KV2610190001-2";
        list.StatusFilter = list.Statuses[2];
        list.From = null;
        int before = _orders.Searches.Count;

        await list.ResetFiltersCommand.ExecuteAsync(null);

        Assert.Equal(before + 1, _orders.Searches.Count);
        Assert.Equal(OrderFilter.LastDays(Now) with { Query = string.Empty }, _orders.Searches[^1]);
    }

    [Theory]
    [InlineData(UserRole.Operator, true)]
    [InlineData(UserRole.Admin, false)]
    public void OnlyOperator_CanCreateOrders(UserRole role, bool expected)
    {
        var list = CreateList(role);

        Assert.Equal(expected, list.CanCreate);
        Assert.Equal(expected, list.NewOrderCommand.CanExecute(null));
    }

    [Fact]
    public async Task NewOrder_Saved_RefreshesList()
    {
        var launcher = new StubOrderFormLauncher(new OrderCreated(1, "KV2610190001-2", Money.FromHryvnias(157m), Now));
        var list = CreateList(launcher: launcher);
        await list.LoadCommand.ExecuteAsync(null);

        await list.NewOrderCommand.ExecuteAsync(null);

        Assert.Equal(1, launcher.Calls);
        Assert.Equal(2, _orders.Searches.Count);
    }

    [Fact]
    public async Task NewOrder_Cancelled_DoesNotRefresh()
    {
        var list = CreateList(launcher: new StubOrderFormLauncher(null));
        await list.LoadCommand.ExecuteAsync(null);

        await list.NewOrderCommand.ExecuteAsync(null);

        Assert.Single(_orders.Searches);
    }

    [Fact]
    public async Task AccessDenied_ShowsErrorInsteadOfCrash()
    {
        var list = new OrderListViewModel(new ThrowingOrderService(), _clients, _couriers, new FakeOrderFormLauncher(), _details,
            _dialogs, new CurrentUser(), new FixedClock(Now), _clipboard);

        await list.SearchCommand.ExecuteAsync(null);

        Assert.Contains("Недостатньо прав", Assert.Single(_dialogs.Errors));
    }

    private sealed class ThrowingOrderService : IOrderService
    {
        public Task<OrderCreated> CreateAsync(OrderInput input, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<OrderSearchResult> SearchAsync(OrderFilter filter, CancellationToken cancellationToken = default) =>
            Task.FromException<OrderSearchResult>(new AccessDeniedException("Дія недоступна."));

        public Task<Order> GetOrderAsync(int orderId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<OrderListItem>> GetMyDeliveriesAsync(bool active, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> UpdateDetailsAsync(OrderDetailsEdit edit, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}

public class TrackingSearchViewModelTests
{
    private readonly FakeOrderService _orders = new();
    private readonly FakeOrderDetailsDialog _details = new();
    private readonly TrackingSearchViewModel _page;

    public TrackingSearchViewModelTests()
    {
        _page = new TrackingSearchViewModel(_orders, new FakeDialogService(), _details, new FakeClipboardService());
    }

    [Fact]
    public void EmptyNumber_SearchDisabled()
    {
        Assert.False(_page.SearchCommand.CanExecute(null));

        _page.TrackingNumber = "kv";
        Assert.True(_page.SearchCommand.CanExecute(null));
    }

    [Fact]
    public async Task ValidNumber_SearchesNormalizedNumberOnly()
    {
        _orders.SearchResult = new([OrderListTestData.Item("KV2610190042-6")], false, null);
        _page.TrackingNumber = "kv 2610190042 6";

        await _page.SearchCommand.ExecuteAsync(null);

        OrderFilter filter = Assert.Single(_orders.Searches);
        Assert.Equal(new OrderFilter(Query: "KV2610190042-6"), filter);
        Assert.Equal("KV2610190042-6", _page.Result?.TrackingNumber);
        Assert.Null(_page.Notice);
    }

    [Fact]
    public async Task NumberWithTypo_ExplainsWithoutQueryingDatabase()
    {
        _page.TrackingNumber = "KV2610190042-7";

        await _page.SearchCommand.ExecuteAsync(null);

        Assert.Empty(_orders.Searches);
        Assert.Null(_page.Result);
        Assert.Contains("контрольна цифра", _page.Notice);
    }

    [Fact]
    public async Task Phone_IsRedirectedToOrderList()
    {
        _page.TrackingNumber = "050 123 45 67";

        await _page.SearchCommand.ExecuteAsync(null);

        Assert.Empty(_orders.Searches);
        Assert.Contains("Списку замовлень", _page.Notice);
    }

    [Fact]
    public async Task NotFound_ShowsServiceNotice()
    {
        _orders.SearchResult = new([], false, "Замовлення з трек-номером KV2610190042-6 не знайдено.");
        _page.TrackingNumber = "KV2610190042-6";

        await _page.SearchCommand.ExecuteAsync(null);

        Assert.Null(_page.Result);
        Assert.Contains("не знайдено", _page.Notice);
    }
}

public class OrderDetailsOpeningTests
{
    private readonly FakeOrderService _orders = new();
    private readonly FakeOrderDetailsDialog _details = new();

    [Fact]
    public async Task OrderList_OpenSelected_RefreshesOnlyAfterChanges()
    {
        var currentUser = new CurrentUser();
        currentUser.SignIn(new UserSession(1, "operator", "Оператор", UserRole.Operator, null));
        var list = new OrderListViewModel(_orders, new FakeClientService(), new FakeCourierService(), new FakeOrderFormLauncher(),
            _details, new FakeDialogService(), currentUser, new FixedClock(new DateTime(2026, 10, 19)), new FakeClipboardService());
        await list.LoadCommand.ExecuteAsync(null);

        Assert.False(list.OpenOrderCommand.CanExecute(null));
        list.SelectedOrder = OrderListTestData.Item("KV2610190001-2");
        Assert.True(list.OpenOrderCommand.CanExecute(null));

        await list.OpenOrderCommand.ExecuteAsync(null);
        Assert.Equal([1], _details.Opened);
        Assert.Single(_orders.Searches);

        _details.Result = true;
        await list.OpenOrderCommand.ExecuteAsync(null);
        Assert.Equal(2, _orders.Searches.Count);
    }

    [Fact]
    public async Task TrackingSearch_OpenFoundOrder()
    {
        _orders.SearchResult = new([OrderListTestData.Item("KV2610190042-6")], false, null);
        var page = new TrackingSearchViewModel(_orders, new FakeDialogService(), _details, new FakeClipboardService()) { TrackingNumber = "KV2610190042-6" };
        Assert.False(page.OpenOrderCommand.CanExecute(null));

        await page.SearchCommand.ExecuteAsync(null);
        _details.Result = true;
        await page.OpenOrderCommand.ExecuteAsync(null);

        Assert.Equal([1], _details.Opened);
        Assert.Equal(2, _orders.Searches.Count);   // повторний пошук показує новий статус
    }
}
