using CourierService.Core;
using CourierService.Core.Enums;
using CourierService.Core.Exceptions;
using CourierService.Core.Reports;
using CourierService.Core.Security;
using CourierService.Data.Seed;
using CourierService.Data.Services;
using Microsoft.EntityFrameworkCore;

namespace CourierService.Tests.Data;

/// <summary>
/// Сервіс звітів на справжній SQLite з демо-замовленнями: підсумки звітів збігаються з прямими запитами до БД.
/// </summary>
public class ReportServiceTests : DirectoryServiceTestBase
{
    private static readonly DateTime Afternoon = new(2026, 10, 19, 15, 0, 0);

    private ReportService Reports => new(Db, CurrentUser, new FixedClock(Afternoon));

    private static ReportPeriod LastWeek => ReportPeriod.LastDays(Afternoon, 7);

    private async Task SeedDemoAsync()
    {
        await using var context = Db.CreateDbContext();
        await new DemoOrderGenerator(new FixedClock(Afternoon)).SeedAsync(context);
    }

    [Fact]
    public async Task Dashboard_OverdueNow_CountsAllUnfinishedOrders_WhateverThePeriod()
    {
        await SeedDemoAsync();
        await SignInAsync(UserRole.Admin);

        // Наступного дня опівдні, період — лише цей день: замовлення, оформлені напередодні, у період не входять,
        // але незавершені з них мають потрапити в показник
        DateTime nextNoon = Afternoon.Date.AddDays(1).AddHours(12);
        ReportData data = await new ReportService(Db, CurrentUser, new FixedClock(nextNoon)).GetDataAsync(new ReportPeriod(nextNoon, nextNoon));
        ReportSummaryItem tile = DashboardCalculator.Calculate(data).Tiles.Single(t => t.Label == "Прострочено зараз");

        await using var context = Db.CreateDbContext();
        OrderStatus[] open = [.. global::CourierService.Core.Orders.OrderWorkflow.OpenStatuses];
        int expected = await context.Orders.CountAsync(o => open.Contains(o.Status) && o.Deadline < nextNoon);
        Assert.True(expected > 0);
        Assert.Equal(expected, tile.Value);
    }

    [Fact]
    public async Task Revenue_MatchesPaidPaymentsInPeriod()
    {
        await SeedDemoAsync();
        await SignInAsync(UserRole.Operator);

        ReportTable table = await Reports.BuildAsync(ReportKind.Revenue, LastWeek);

        await using var context = Db.CreateDbContext();
        DateTime start = LastWeek.Start;
        DateTime end = LastWeek.EndExclusive;
        var paid = await context.Payments
            .Where(p => p.Status == PaymentStatus.Paid && p.PaidAt >= start && p.PaidAt < end)
            .Select(p => p.Amount)
            .ToListAsync();
        Assert.NotEmpty(paid);
        Assert.Equal(ReportData.Sum(paid), table.Totals![5]);
        Assert.Equal(paid.Count, table.Totals[1]);
    }

    [Fact]
    public async Task Unpaid_ListsEveryPendingPayment()
    {
        await SeedDemoAsync();
        await SignInAsync(UserRole.Operator);

        ReportTable table = await Reports.BuildAsync(ReportKind.Unpaid, LastWeek);

        await using var context = Db.CreateDbContext();
        int pending = await context.Payments.CountAsync(p => p.Status == PaymentStatus.Pending);
        Assert.Equal(pending, table.Rows.Count);
    }

    [Fact]
    public async Task Performance_AttributesFailedAttemptsToReportingCourier()
    {
        await SeedDemoAsync();
        await SignInAsync(UserRole.Operator);
        var period = ReportPeriod.LastDays(Afternoon, 30);

        ReportTable table = await Reports.BuildAsync(ReportKind.CourierPerformance, period);

        await using var context = Db.CreateDbContext();
        int failures = await context.OrderStatusChanges.CountAsync(c => c.ToStatus == OrderStatus.DeliveryFailed);
        int delivered = await context.Orders.CountAsync(o => o.Status == OrderStatus.Delivered);
        Assert.Equal(failures, table.Totals![4]);
        Assert.Equal(delivered, table.Totals[1]);
    }

    [Fact]
    public async Task Payout_OnlyForAdmin_AndGrossIsRevenueMinusPayouts()
    {
        await SeedDemoAsync();
        await SignInAsync(UserRole.Operator);
        await Assert.ThrowsAsync<AccessDeniedException>(() => Reports.BuildAsync(ReportKind.CourierPayout, LastWeek));

        await SignInAsync(UserRole.Admin);
        ReportTable table = await Reports.BuildAsync(ReportKind.CourierPayout, LastWeek);

        var revenue = (Money)table.Summary[0].Value!;
        var payouts = (Money)table.Summary[1].Value!;
        Assert.Equal(revenue - payouts, table.Summary[2].Value);
        Assert.Equal(payouts, table.Totals![3]);
    }

    [Fact]
    public async Task Courier_CannotSeeReports()
    {
        await SignInAsync(UserRole.Courier);

        await Assert.ThrowsAsync<AccessDeniedException>(() => Reports.BuildAsync(ReportKind.Revenue, LastWeek));
        await Assert.ThrowsAsync<AccessDeniedException>(() => Reports.GetDataAsync(LastWeek));
    }

    [Fact]
    public async Task Data_ContainsOrdersOfPeriodAndAllPending()
    {
        await SeedDemoAsync();
        await SignInAsync(UserRole.Admin);

        ReportData data = await Reports.GetDataAsync(ReportPeriod.LastDays(Afternoon, 1));

        Assert.Equal(Afternoon, data.Now);
        Assert.Equal(4, data.Couriers.Count);
        Assert.All(data.Orders, o => Assert.True(
            data.Period.Contains(o.CreatedAt) || data.Period.Contains(o.DeliveredAt) || data.Period.Contains(o.PaidAt)
            || o.PaymentStatus == PaymentStatus.Pending));
        await using var context = Db.CreateDbContext();
        Assert.Equal(await context.Payments.CountAsync(p => p.Status == PaymentStatus.Pending),
            data.Orders.Count(o => o.PaymentStatus == PaymentStatus.Pending));
    }

    [Fact]
    public async Task Data_IncludesOldUnpaidOrders()
    {
        await SeedDemoAsync();
        Db.ExecuteScalar("UPDATE Orders SET CreatedAt = '2026-08-01 10:00:00' WHERE Id = (SELECT MIN(OrderId) FROM Payments WHERE Status = 'Pending')");
        await SignInAsync(UserRole.Operator);

        ReportData data = await Reports.GetDataAsync(ReportPeriod.LastDays(Afternoon, 1));

        Assert.Contains(data.Orders, o => o.CreatedAt == new DateTime(2026, 8, 1, 10, 0, 0) && o.PaymentStatus == PaymentStatus.Pending);
    }
}
