using System.IO;
using ClosedXML.Excel;
using CourierService.App.Services;
using CourierService.Core.Reports;
using CourierService.Tests.Core;

namespace CourierService.Tests.App;

/// <summary>
/// Експорт перевіряється «туди й назад»: звіт зберігається у файл, а тест читає файл і порівнює значення.
/// </summary>
public sealed class ExcelReportExporterTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"courier-report-{Guid.NewGuid():N}.xlsx");

    public void Dispose()
    {
        if (File.Exists(_path))
        {
            File.Delete(_path);
        }
    }

    private static IXLCell FindCell(IXLWorksheet sheet, string text) =>
        sheet.CellsUsed().First(cell => cell.GetString() == text);

    [Fact]
    public void Revenue_WritesTitleSummaryHeaderRowsAndTotalsAsNumbers()
    {
        ReportTable table = new RevenueReport().Build(ReportTests.Data());

        new ExcelReportExporter().Export(table, _path);

        using var workbook = new XLWorkbook(_path);
        IXLWorksheet sheet = workbook.Worksheet(1);
        Assert.Equal("Виручка", sheet.Name);
        Assert.Equal("Виручка", sheet.Cell(1, 1).GetString());
        Assert.Equal("Період: 01.10.2026 — 03.10.2026", sheet.Cell(2, 1).GetString());

        // Показники: значення — число в гривнях із грошовим форматом
        IXLCell revenueLabel = FindCell(sheet, "Виручка за період");
        Assert.Equal(657m, revenueLabel.CellRight().GetValue<decimal>());

        // Заголовки й перший рядок: дата — дата, гроші — числа
        IXLCell header = FindCell(sheet, "Дата");
        Assert.Equal("Разом", header.CellRight(5).GetString());
        IXLRow firstRow = header.WorksheetRow().RowBelow();
        Assert.Equal(new DateTime(2026, 10, 1), firstRow.Cell(1).GetDateTime());
        Assert.Equal(2, firstRow.Cell(2).GetValue<int>());
        Assert.Equal(157m, firstRow.Cell(3).GetValue<decimal>());
        Assert.Equal(XLDataType.Number, firstRow.Cell(6).DataType);

        // Підсумок — жирний рядок після даних
        IXLRow totals = firstRow.RowBelow().RowBelow();
        Assert.Equal("Разом", totals.Cell(1).GetString());
        Assert.Equal(657m, totals.Cell(6).GetValue<decimal>());
        Assert.True(totals.Cell(6).Style.Font.Bold);

        // Пояснення під таблицею
        Assert.Contains(sheet.CellsUsed(), cell => cell.GetString().StartsWith("Виручка — оплати", StringComparison.Ordinal));
    }

    [Fact]
    public void Percent_IsStoredAsFraction_AndEmptyValuesAsBlank()
    {
        ReportTable table = new CourierPerformanceReport().Build(ReportTests.Data());

        new ExcelReportExporter().Export(table, _path);

        using var workbook = new XLWorkbook(_path);
        IXLWorksheet sheet = workbook.Worksheet(1);
        Assert.Equal("Ефективність кур'єрів", sheet.Name);
        IXLRow andrii = FindCell(sheet, "Андрій Ткаченко").WorksheetRow();
        Assert.Equal(0.5m, andrii.Cell(4).GetValue<decimal>());               // 50 % → 0,5 з форматом відсотка
        Assert.Equal("0.0%", andrii.Cell(4).Style.NumberFormat.Format);
        IXLRow oksana = FindCell(sheet, "Оксана Мельник").WorksheetRow();
        Assert.True(oksana.Cell(4).IsEmpty());                                 // немає доставок — частки немає
    }

    [Fact]
    public void EveryReport_CanBeExported()
    {
        foreach (ReportBase report in ReportCatalog.All)
        {
            new ExcelReportExporter().Export(report.Build(ReportTests.Data()), _path);

            using var workbook = new XLWorkbook(_path);
            Assert.Equal(report.Title, workbook.Worksheet(1).Cell(1, 1).GetString());
        }
    }
}
