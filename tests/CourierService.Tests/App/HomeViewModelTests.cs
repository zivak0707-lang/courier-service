using CourierService.App.ViewModels;
using CourierService.Core.Backups;
using CourierService.Core.Enums;
using CourierService.Core.Orders;
using CourierService.Core.Reports;
using CourierService.Core.Security;
using CourierService.Tests.Core;
using CourierService.Tests.Data;

namespace CourierService.Tests.App;

public class HomeViewModelTests
{
    private readonly FakeOrderService _orders = new();
    private readonly FakeBackupService _backups = new();
    private readonly FakeOrderDetailsDialog _details = new();
    private readonly CurrentUser _currentUser = new();

    private HomeViewModel CreatePage(UserSession user)
    {
        _currentUser.SignIn(user);
        return new HomeViewModel(_currentUser, _orders, new FakeReportService(), _backups, _details, new FakeDialogService(),
            new FixedClock(WorkflowTestData.Now));
    }

    [Fact]
    public async Task Operator_SeesNewOrdersAndUnpaid_FromSearchesByStatusAndPayment()
    {
        _orders.Respond = filter => filter switch
        {
            { Status: OrderStatus.New } => new([OrderListTestData.Item("KV-NEW")], false, null),
            { PaymentStatus: PaymentStatus.Pending } => new([OrderListTestData.Item("KV-NEW"), OrderListTestData.Item("KV-2")], false, null),
            _ => new([], false, null),
        };
        HomeViewModel page = CreatePage(WorkflowTestData.Operator);

        await page.LoadCommand.ExecuteAsync(null);

        Assert.True(page.IsOperator);
        Assert.False(page.IsCourier);
        Assert.Equal("KV-NEW", Assert.Single(page.Operator!.NewOrders).TrackingNumber);
        Assert.Equal(2, page.Operator.UnpaidCount);
        Assert.Equal(OrderWorkflow.OpenStatuses.Count + 1, _orders.Searches.Count);
        Assert.Equal("Вітаємо, Ігор Бондар!", page.Greeting);
    }

    [Fact]
    public async Task Courier_SeesNearestDelivery_AndOpensItsCard()
    {
        _orders.MyActive = [OrderListTestData.Item("KV-MY", OrderStatus.Assigned)];
        HomeViewModel page = CreatePage(WorkflowTestData.Courier1);

        await page.LoadCommand.ExecuteAsync(null);
        await page.OpenOrderCommand.ExecuteAsync(page.Courier!.Nearest);

        Assert.Equal(1, page.Courier.ActiveCount);
        Assert.StartsWith("Забрати:", page.Courier.NextStep);
        Assert.Equal([1], _details.Opened);
        Assert.Null(page.Operator);
    }

    [Fact]
    public async Task Admin_SeesTodayTilesAndLastBackup()
    {
        _backups.Entries.Add(new BackupEntry(new BackupFile("b.db", new DateTime(2026, 10, 18, 21, 5, 0), BackupKind.Manual), @"C:\b.db", 1));
        HomeViewModel page = CreatePage(WorkflowTestData.Admin);

        await page.LoadCommand.ExecuteAsync(null);

        ReportSummaryItem backup = page.AdminTiles[^1];
        Assert.Equal("Остання резервна копія", backup.Label);
        Assert.Equal("18.10.2026 21:05", backup.Text);
        Assert.Contains(page.AdminTiles, t => t.Label == "Замовлень оформлено");
    }

    [Fact]
    public async Task Admin_WithoutBackups_ShowsDash()
    {
        HomeViewModel page = CreatePage(WorkflowTestData.Admin);

        await page.LoadCommand.ExecuteAsync(null);

        Assert.Equal("—", page.AdminTiles[^1].Text);
    }
}
