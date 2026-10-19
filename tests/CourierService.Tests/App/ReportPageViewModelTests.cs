using CourierService.App.ViewModels.Reports;
using CourierService.Core.Exceptions;
using CourierService.Core.Reports;
using CourierService.Tests.Core;
using CourierService.Tests.Data;

namespace CourierService.Tests.App;

/// <summary>Заглушка сервісу звітів: будує справжній звіт на відомих даних і запам'ятовує запити.</summary>
internal sealed class FakeReportService : IReportService
{
    public List<(ReportKind Kind, ReportPeriod Period)> Requests { get; } = [];
    public Exception? ThrowOnBuild { get; set; }

    public Task<ReportTable> BuildAsync(ReportKind kind, ReportPeriod period, CancellationToken cancellationToken = default)
    {
        Requests.Add((kind, period));
        return ThrowOnBuild is null
            ? Task.FromResult(ReportCatalog.Get(kind).Build(ReportTests.Data()))
            : Task.FromException<ReportTable>(ThrowOnBuild);
    }

    public Task<ReportData> GetDataAsync(ReportPeriod period, CancellationToken cancellationToken = default) =>
        Task.FromResult(ReportTests.Data() with { Period = period });
}

public class ReportPageViewModelTests
{
    private static readonly DateTime Now = new(2026, 10, 19, 15, 0, 0);

    private readonly FakeReportService _reports = new();
    private readonly FakeDialogService _dialogs = new();

    private RevenueReportViewModel CreatePage() => new(_reports, _dialogs, new FixedClock(Now));

    [Fact]
    public async Task Load_BuildsReportForLastThirtyDays()
    {
        var page = CreatePage();

        await page.LoadCommand.ExecuteAsync(null);

        var request = Assert.Single(_reports.Requests);
        Assert.Equal(ReportKind.Revenue, request.Kind);
        Assert.Equal(ReportPeriod.LastDays(Now, 30), request.Period);
        Assert.Equal("Виручка", page.Title);
        Assert.True(page.HasReport);
        Assert.True(page.UsesPeriod);
    }

    [Fact]
    public async Task DisplayRows_AreTextsWithTotalLast()
    {
        var page = CreatePage();

        await page.LoadCommand.ExecuteAsync(null);

        Assert.Equal(3, page.Rows.Count);                 // 2 дні + «Разом»
        Assert.Equal("01.10.2026", page.Rows[0].Cells[0]);
        Assert.Equal("2", page.Rows[0].Cells[1]);
        Assert.True(page.Rows[^1].IsTotal);
        Assert.Equal("Разом", page.Rows[^1].Cells[0]);
        Assert.False(page.IsEmpty);
    }

    [Fact]
    public async Task ChangingDates_RebuildsAfterLoad()
    {
        var page = CreatePage();
        page.From = new DateTime(2026, 10, 1);   // до завантаження — без запиту
        Assert.Empty(_reports.Requests);

        await page.LoadCommand.ExecuteAsync(null);
        page.To = new DateTime(2026, 10, 10);

        Assert.Equal(2, _reports.Requests.Count);
        Assert.Equal(new ReportPeriod(new DateTime(2026, 10, 1), new DateTime(2026, 10, 10)), _reports.Requests[^1].Period);
    }

    [Fact]
    public async Task QuickPeriod_SetsBothDatesWithSingleRequest()
    {
        var page = CreatePage();
        await page.LoadCommand.ExecuteAsync(null);

        page.UseLastDaysCommand.Execute(7);

        Assert.Equal(2, _reports.Requests.Count);
        Assert.Equal(ReportPeriod.LastDays(Now, 7), _reports.Requests[^1].Period);
        Assert.Equal(new DateTime(2026, 10, 13), page.From);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task InvalidPeriod_IsExplainedWithoutRequest(bool reversed)
    {
        var page = CreatePage();
        await page.LoadCommand.ExecuteAsync(null);
        int before = _reports.Requests.Count;

        if (reversed)
        {
            page.From = new DateTime(2026, 10, 20);
        }
        else
        {
            page.To = null;
        }

        Assert.Equal(before, _reports.Requests.Count);
        Assert.NotNull(page.Notice);
    }

    [Fact]
    public async Task AccessDenied_ShowsError()
    {
        _reports.ThrowOnBuild = new AccessDeniedException("Дія недоступна для ролі «Оператор».");
        var page = new CourierPayoutReportViewModel(_reports, _dialogs, new FixedClock(Now));

        await page.LoadCommand.ExecuteAsync(null);

        Assert.Contains("Недостатньо прав", Assert.Single(_dialogs.Errors));
        Assert.False(page.HasReport);
    }

    [Fact]
    public async Task UnpaidPage_HasNoPeriod()
    {
        var page = new UnpaidReportViewModel(_reports, _dialogs, new FixedClock(Now));

        await page.LoadCommand.ExecuteAsync(null);

        Assert.False(page.UsesPeriod);
        Assert.Equal("Неоплачені замовлення", page.Title);
    }
}
