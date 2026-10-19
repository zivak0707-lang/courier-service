using System.Diagnostics.CodeAnalysis;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CourierService.App.Services;
using CourierService.Core.Abstractions;
using CourierService.Core.Exceptions;
using CourierService.Core.Reports;

namespace CourierService.App.ViewModels.Reports;

/// <summary>Рядок таблиці звіту для показу: тексти комірок і ознака підсумкового рядка.</summary>
/// <param name="Cells">Тексти комірок у порядку колонок.</param>
/// <param name="IsTotal">Чи це рядок «Разом».</param>
public sealed record ReportDisplayRow(IReadOnlyList<string> Cells, bool IsTotal);

/// <summary>
/// Сторінка звіту (рішення D-32): період, таблиця з підсумком, показники, пояснення.
/// Нащадки визначають лише, який звіт показувати; таблицю для всіх звітів будує <see cref="ReportBase"/>.
/// </summary>
public abstract partial class ReportPageViewModel : ViewModelBase, ILoadable
{
    /// <summary>Типовий період, днів.</summary>
    public const int DefaultDays = 30;

    private readonly IReportService _reports;
    private readonly IDialogService _dialogs;
    private readonly IClock _clock;
    private readonly IReportExporter _exporter;
    private readonly IFileDialogService _files;
    private bool _isLoaded;

    /// <summary>
    /// Створює сторінку.
    /// </summary>
    /// <param name="reports">Сервіс звітів.</param>
    /// <param name="dialogs">Повідомлення.</param>
    /// <param name="clock">Поточний час (типовий період).</param>
    /// <param name="exporter">Експорт у Excel.</param>
    /// <param name="files">Вибір файлу для збереження.</param>
    protected ReportPageViewModel(
        IReportService reports, IDialogService dialogs, IClock clock, IReportExporter exporter, IFileDialogService files)
    {
        _reports = reports;
        _dialogs = dialogs;
        _clock = clock;
        _exporter = exporter;
        _files = files;
        ReportPeriod period = ReportPeriod.LastDays(clock.Now, DefaultDays);
        From = period.From;
        To = period.To;
    }

    /// <summary>Який звіт показує сторінка.</summary>
    protected abstract ReportKind Kind { get; }

    /// <inheritdoc />
    public override string Title => ReportCatalog.Get(Kind).Title;

    /// <summary>Чи має звіт період (у «Неоплачених» — стан на зараз).</summary>
    public bool UsesPeriod => ReportCatalog.Get(Kind).UsesPeriod;

    /// <summary>Швидкий вибір періоду, днів.</summary>
    public IReadOnlyList<int> QuickPeriods { get; } = [7, 30, 90];

    /// <summary>Перший день періоду.</summary>
    [ObservableProperty]
    public partial DateTime? From { get; set; }

    /// <summary>Останній день періоду.</summary>
    [ObservableProperty]
    public partial DateTime? To { get; set; }

    /// <summary>Сформований звіт.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasReport))]
    [NotifyCanExecuteChangedFor(nameof(ExportCommand))]
    public partial ReportTable? Table { get; private set; }

    /// <summary>Рядки для показу (з підсумком у кінці).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    public partial IReadOnlyList<ReportDisplayRow> Rows { get; private set; } = [];

    /// <summary>Пояснення, якщо звіт не сформовано (наприклад, неправильний період).</summary>
    [ObservableProperty]
    public partial string? Notice { get; private set; }

    /// <summary>Чи триває формування.</summary>
    [ObservableProperty]
    public partial bool IsBusy { get; private set; }

    /// <summary>Чи сформовано звіт.</summary>
    [MemberNotNullWhen(true, nameof(Table))]
    public bool HasReport => Table is not null;

    /// <summary>Чи звіт без рядків.</summary>
    public bool IsEmpty => Table is not null && Table.Rows.Count == 0;

    /// <summary>Поточний період.</summary>
    public ReportPeriod? Period => From is { } from && To is { } to ? new ReportPeriod(from.Date, to.Date) : null;

    partial void OnFromChanged(DateTime? value) => RebuildOnChange();

    partial void OnToChanged(DateTime? value) => RebuildOnChange();

    [RelayCommand]
    private async Task LoadAsync()
    {
        _isLoaded = true;
        await BuildAsync();
    }

    [RelayCommand]
    private async Task BuildAsync()
    {
        Notice = null;
        if (Period is not { } period)
        {
            Notice = "Оберіть початкову й кінцеву дату періоду.";
            return;
        }

        if (period.From > period.To)
        {
            Notice = "Початкова дата періоду пізніша за кінцеву.";
            return;
        }

        IsBusy = true;
        try
        {
            ReportTable table = await _reports.BuildAsync(Kind, period);
            Table = table;
            Rows = ToDisplayRows(table);
        }
        catch (DomainException ex)
        {
            _dialogs.ShowError(ex.UserMessage);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(HasReport))]
    private void Export()
    {
        if (!HasReport)
        {
            return;
        }

        string suggested = UsesPeriod && Period is { } period
            ? $"{Table.Title} {period.From:yyyy-MM-dd} — {period.To:yyyy-MM-dd}.xlsx"
            : $"{Table.Title} {_clock.Now:yyyy-MM-dd}.xlsx";
        if (_files.AskExcelSavePath(suggested) is not { } path)
        {
            return;
        }

        try
        {
            _exporter.Export(Table, path);
        }
        catch (IOException ex)
        {
            // Найчастіше файл відкритий в Excel і заблокований для запису
            _dialogs.ShowError($"Не вдалося зберегти файл. Можливо, його відкрито в Excel — закрийте його й спробуйте ще раз.\n\n{ex.Message}");
            return;
        }

        if (_dialogs.Confirm($"Звіт збережено у файл:\n{path}\n\nВідкрити його?"))
        {
            _files.OpenFile(path);
        }
    }

    [RelayCommand]
    private void UseLastDays(int days)
    {
        ReportPeriod period = ReportPeriod.LastDays(_clock.Now, days);

        // Обидві дати змінюються разом — звіт перебудовується один раз
        _isLoaded = false;
        From = period.From;
        To = period.To;
        _isLoaded = true;
        BuildCommand.Execute(null);
    }

    /// <summary>
    /// Тексти комірок для показу; підсумок — останнім рядком.
    /// </summary>
    /// <param name="table">Звіт.</param>
    /// <returns>Рядки для таблиці.</returns>
    public static IReadOnlyList<ReportDisplayRow> ToDisplayRows(ReportTable table)
    {
        ArgumentNullException.ThrowIfNull(table);
        var rows = table.Rows.Select(row => new ReportDisplayRow(Texts(table, row), false)).ToList();
        if (table.Totals is { } totals && table.Rows.Count > 0)
        {
            rows.Add(new ReportDisplayRow(Texts(table, totals), true));
        }

        return rows;
    }

    private static List<string> Texts(ReportTable table, IReadOnlyList<object?> values) =>
        table.Columns.Select((column, index) => values[index] is string text ? text : ReportFormat.Display(column.Kind, values[index]))
            .ToList();

    private void RebuildOnChange()
    {
        if (_isLoaded)
        {
            BuildCommand.Execute(null);
        }
    }
}

/// <summary>Звіт «Виручка».</summary>
/// <param name="reports">Сервіс звітів.</param>
/// <param name="dialogs">Повідомлення.</param>
/// <param name="clock">Поточний час.</param>
/// <param name="exporter">Експорт у Excel.</param>
/// <param name="files">Вибір файлу.</param>
public sealed class RevenueReportViewModel(
    IReportService reports, IDialogService dialogs, IClock clock, IReportExporter exporter, IFileDialogService files)
    : ReportPageViewModel(reports, dialogs, clock, exporter, files)
{
    /// <inheritdoc />
    protected override ReportKind Kind => ReportKind.Revenue;
}

/// <summary>Звіт «Неоплачені замовлення».</summary>
/// <param name="reports">Сервіс звітів.</param>
/// <param name="dialogs">Повідомлення.</param>
/// <param name="clock">Поточний час.</param>
/// <param name="exporter">Експорт у Excel.</param>
/// <param name="files">Вибір файлу.</param>
public sealed class UnpaidReportViewModel(
    IReportService reports, IDialogService dialogs, IClock clock, IReportExporter exporter, IFileDialogService files)
    : ReportPageViewModel(reports, dialogs, clock, exporter, files)
{
    /// <inheritdoc />
    protected override ReportKind Kind => ReportKind.Unpaid;
}

/// <summary>Звіт «Ефективність кур'єрів».</summary>
/// <param name="reports">Сервіс звітів.</param>
/// <param name="dialogs">Повідомлення.</param>
/// <param name="clock">Поточний час.</param>
/// <param name="exporter">Експорт у Excel.</param>
/// <param name="files">Вибір файлу.</param>
public sealed class CourierPerformanceReportViewModel(
    IReportService reports, IDialogService dialogs, IClock clock, IReportExporter exporter, IFileDialogService files)
    : ReportPageViewModel(reports, dialogs, clock, exporter, files)
{
    /// <inheritdoc />
    protected override ReportKind Kind => ReportKind.CourierPerformance;
}

/// <summary>Звіт «Нарахування кур'єрам» (лише адміністратор).</summary>
/// <param name="reports">Сервіс звітів.</param>
/// <param name="dialogs">Повідомлення.</param>
/// <param name="clock">Поточний час.</param>
/// <param name="exporter">Експорт у Excel.</param>
/// <param name="files">Вибір файлу.</param>
public sealed class CourierPayoutReportViewModel(
    IReportService reports, IDialogService dialogs, IClock clock, IReportExporter exporter, IFileDialogService files)
    : ReportPageViewModel(reports, dialogs, clock, exporter, files)
{
    /// <inheritdoc />
    protected override ReportKind Kind => ReportKind.CourierPayout;
}
