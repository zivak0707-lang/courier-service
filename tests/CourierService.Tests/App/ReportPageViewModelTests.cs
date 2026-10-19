using System.IO;
using CourierService.App.Services;
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

/// <summary>Заглушка експорту: запам'ятовує збережені звіти або кидає помилку запису.</summary>
internal sealed class FakeReportExporter : IReportExporter
{
    public List<(ReportTable Table, string Path)> Exported { get; } = [];
    public bool FileLocked { get; set; }

    public void Export(ReportTable table, string path)
    {
        if (FileLocked)
        {
            throw new IOException("The process cannot access the file because it is being used by another process.");
        }

        Exported.Add((table, path));
    }
}

/// <summary>Заглушка вибору файлу: повертає заданий шлях і запам'ятовує відкриті файли.</summary>
internal sealed class FakeFileDialogService : IFileDialogService
{
    public string? PathToReturn { get; set; } = @"C:\Звіти\звіт.xlsx";
    public List<string> Suggested { get; } = [];
    public List<string> Opened { get; } = [];

    public string? AskExcelSavePath(string suggestedName)
    {
        Suggested.Add(suggestedName);
        return PathToReturn;
    }

    public void OpenFile(string path) => Opened.Add(path);

    public string? BackupPathToReturn { get; set; }

    public string? AskBackupOpenPath(string initialDirectory) => BackupPathToReturn;
}

public class ReportPageViewModelTests
{
    private static readonly DateTime Now = new(2026, 10, 19, 15, 0, 0);

    private readonly FakeReportService _reports = new();
    private readonly FakeDialogService _dialogs = new();
    private readonly FakeReportExporter _exporter = new();
    private readonly FakeFileDialogService _files = new();

    private RevenueReportViewModel CreatePage() => new(_reports, _dialogs, new FixedClock(Now), _exporter, _files);

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
        var page = new CourierPayoutReportViewModel(_reports, _dialogs, new FixedClock(Now), _exporter, _files);

        await page.LoadCommand.ExecuteAsync(null);

        Assert.Contains("Недостатньо прав", Assert.Single(_dialogs.Errors));
        Assert.False(page.HasReport);
    }

    [Fact]
    public async Task Export_AvailableAfterBuild_SuggestsNameWithPeriod_AndOffersToOpen()
    {
        var page = CreatePage();
        Assert.False(page.ExportCommand.CanExecute(null));
        await page.LoadCommand.ExecuteAsync(null);
        _dialogs.ConfirmResult = true;

        page.ExportCommand.Execute(null);

        Assert.Equal("Виручка 2026-09-20 — 2026-10-19.xlsx", Assert.Single(_files.Suggested));
        var exported = Assert.Single(_exporter.Exported);
        Assert.Same(page.Table, exported.Table);
        Assert.Equal([_files.PathToReturn!], _files.Opened);
    }

    [Fact]
    public async Task Export_CancelledDialog_SavesNothing()
    {
        var page = CreatePage();
        await page.LoadCommand.ExecuteAsync(null);
        _files.PathToReturn = null;

        page.ExportCommand.Execute(null);

        Assert.Empty(_exporter.Exported);
        Assert.Empty(_dialogs.Questions);
    }

    [Fact]
    public async Task Export_LockedFile_ShowsUnderstandableError()
    {
        var page = CreatePage();
        await page.LoadCommand.ExecuteAsync(null);
        _exporter.FileLocked = true;

        page.ExportCommand.Execute(null);

        Assert.Contains("відкрито в Excel", Assert.Single(_dialogs.Errors));
        Assert.Empty(_files.Opened);
    }

    [Fact]
    public async Task UnpaidPage_HasNoPeriod()
    {
        var page = new UnpaidReportViewModel(_reports, _dialogs, new FixedClock(Now), _exporter, _files);

        await page.LoadCommand.ExecuteAsync(null);

        Assert.False(page.UsesPeriod);
        Assert.Equal("Неоплачені замовлення", page.Title);
    }
}

public class DashboardViewModelTests
{
    private static readonly DateTime Now = new(2026, 10, 19, 15, 0, 0);

    [Fact]
    public async Task Load_CalculatesSnapshotForThirtyDays()
    {
        var page = new DashboardViewModel(new FakeReportService(), new FakeDialogService(), new FixedClock(Now));

        await page.LoadCommand.ExecuteAsync(null);

        Assert.NotNull(page.Snapshot);
        Assert.Equal(ReportPeriod.LastDays(Now, 30), page.Snapshot.Period);
        Assert.Equal(30, page.Snapshot.Daily.Count);
        Assert.Equal(6, page.Snapshot.Tiles.Count);
        Assert.Equal("Дашборд", page.Title);
    }

    [Fact]
    public void ChangingPeriod_Reloads()
    {
        var page = new DashboardViewModel(new FakeReportService(), new FakeDialogService(), new FixedClock(Now));

        page.Days = 7;

        Assert.Equal(7, page.Snapshot!.Daily.Count);
    }

    [Fact]
    public async Task AccessDenied_ShowsError()
    {
        var dialogs = new FakeDialogService();
        var page = new DashboardViewModel(new DeniedReportService(), dialogs, new FixedClock(Now));

        await page.LoadCommand.ExecuteAsync(null);

        Assert.Null(page.Snapshot);
        Assert.Single(dialogs.Errors);
    }

    private sealed class DeniedReportService : IReportService
    {
        public Task<ReportTable> BuildAsync(ReportKind kind, ReportPeriod period, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ReportData> GetDataAsync(ReportPeriod period, CancellationToken cancellationToken = default) =>
            Task.FromException<ReportData>(new AccessDeniedException("Дія недоступна."));
    }
}
