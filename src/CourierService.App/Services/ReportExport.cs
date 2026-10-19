using System.Diagnostics;
using ClosedXML.Excel;
using CourierService.Core;
using CourierService.Core.Reports;
using Microsoft.Win32;

namespace CourierService.App.Services;

/// <summary>
/// Збереження звіту у файл.
/// </summary>
public interface IReportExporter
{
    /// <summary>
    /// Зберігає звіт у файл Excel.
    /// </summary>
    /// <param name="table">Звіт.</param>
    /// <param name="path">Повний шлях до файлу .xlsx.</param>
    /// <exception cref="System.IO.IOException">Файл не вдалося записати (наприклад, він відкритий в Excel).</exception>
    void Export(ReportTable table, string path);
}

/// <summary>
/// Експорт у Excel через ClosedXML (рішення D-11): назва, період, показники, таблиця з підсумком і пояснення.
/// Числа, гроші й дати записуються як значення з форматом, а не як текст, — з ними можна рахувати в Excel.
/// </summary>
public sealed class ExcelReportExporter : IReportExporter
{
    private const string MoneyFormat = "#,##0.00 \"грн\"";

    /// <inheritdoc />
    public void Export(ReportTable table, string path)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        using var workbook = new XLWorkbook();
        IXLWorksheet sheet = workbook.Worksheets.Add(SheetName(table.Title));

        sheet.Cell(1, 1).Value = table.Title;
        sheet.Cell(1, 1).Style.Font.SetBold().Font.SetFontSize(14);
        sheet.Cell(2, 1).Value = table.Subtitle;
        int row = 4;

        foreach (ReportSummaryItem item in table.Summary)
        {
            sheet.Cell(row, 1).Value = item.Label;
            WriteValue(sheet.Cell(row, 2), item.Kind, item.Value);
            sheet.Cell(row, 2).Style.Font.SetBold();
            row++;
        }

        if (table.Summary.Count > 0)
        {
            row++;
        }

        int headerRow = row;
        for (int column = 0; column < table.Columns.Count; column++)
        {
            IXLCell cell = sheet.Cell(headerRow, column + 1);
            cell.Value = table.Columns[column].Header;
            cell.Style.Font.SetBold().Fill.SetBackgroundColor(XLColor.FromHtml("#E6EEF8"));
        }

        foreach (IReadOnlyList<object?> values in table.Rows)
        {
            WriteRow(sheet, ++row, table.Columns, values, bold: false);
        }

        if (table.Totals is { } totals && table.Rows.Count > 0)
        {
            WriteRow(sheet, ++row, table.Columns, totals, bold: true);
        }

        row += 2;
        foreach (string note in table.Notes)
        {
            sheet.Cell(row++, 1).Value = note;
        }

        sheet.Columns(1, Math.Max(2, table.Columns.Count)).AdjustToContents();
        sheet.SheetView.FreezeRows(headerRow);
        workbook.SaveAs(path);
    }

    private static void WriteRow(IXLWorksheet sheet, int row, IReadOnlyList<ReportColumn> columns, IReadOnlyList<object?> values, bool bold)
    {
        for (int column = 0; column < columns.Count; column++)
        {
            IXLCell cell = sheet.Cell(row, column + 1);
            WriteValue(cell, columns[column].Kind, values[column]);
            cell.Style.Font.Bold = bold;
        }
    }

    /// <summary>
    /// Записує значення з форматом за видом колонки; текст у числовій колонці («Разом») записується як текст.
    /// </summary>
    private static void WriteValue(IXLCell cell, ReportValueKind kind, object? value)
    {
        switch (value)
        {
            case null:
                return;
            case string text:
                cell.Value = text;
                return;
            case Money money:
                cell.Value = money.Hryvnias;
                cell.Style.NumberFormat.Format = MoneyFormat;
                return;
            case DateTime date:
                cell.Value = date;
                cell.Style.DateFormat.Format = kind == ReportValueKind.Date ? "dd.mm.yyyy" : "dd.mm.yyyy hh:mm";
                return;
            case decimal number when kind == ReportValueKind.Percent:
                cell.Value = number / 100m;
                cell.Style.NumberFormat.Format = "0.0%";
                return;
            case decimal number:
                cell.Value = number;
                cell.Style.NumberFormat.Format = "0.0";
                return;
            case int number:
                cell.Value = number;
                return;
            default:
                cell.Value = value.ToString();
                return;
        }
    }

    /// <summary>Назва аркуша: до 31 символу, без заборонених Excel символів.</summary>
    private static string SheetName(string title)
    {
        string name = new(title.Where(ch => ch is not (':' or '\\' or '/' or '?' or '*' or '[' or ']')).ToArray());
        return name.Length > 31 ? name[..31] : name;
    }
}

/// <summary>
/// Вибір файлів і відкриття готового файлу чи каталогу (ViewModel не залежить від вікон Windows).
/// </summary>
public interface IFileDialogService
{
    /// <summary>
    /// Питає, куди зберегти файл Excel.
    /// </summary>
    /// <param name="suggestedName">Запропонована назва файлу.</param>
    /// <returns>Повний шлях або <c>null</c>, якщо користувач скасував.</returns>
    string? AskExcelSavePath(string suggestedName);

    /// <summary>
    /// Відкриває файл програмою, пов'язаною з ним у Windows (Excel), або каталог у Провіднику.
    /// </summary>
    /// <param name="path">Шлях до файлу чи каталогу.</param>
    void OpenFile(string path);

    /// <summary>
    /// Питає, з якого файлу відновити базу даних.
    /// </summary>
    /// <param name="initialDirectory">Каталог, відкритий у вікні вибору (каталог копій).</param>
    /// <returns>Повний шлях або <c>null</c>, якщо користувач скасував.</returns>
    string? AskBackupOpenPath(string initialDirectory);
}

/// <summary>
/// Реалізація <see cref="IFileDialogService"/> зі стандартними вікнами Windows.
/// </summary>
public sealed class FileDialogService : IFileDialogService
{
    /// <inheritdoc />
    public string? AskExcelSavePath(string suggestedName)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Зберегти звіт",
            FileName = suggestedName,
            DefaultExt = ".xlsx",
            Filter = "Книга Excel (*.xlsx)|*.xlsx",
            AddExtension = true,
            OverwritePrompt = true,
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    /// <inheritdoc />
    public void OpenFile(string path) => Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })?.Dispose();

    /// <inheritdoc />
    public string? AskBackupOpenPath(string initialDirectory)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Відновити дані з резервної копії",
            Filter = "Резервна копія бази даних (*.db)|*.db|Усі файли (*.*)|*.*",
            CheckFileExists = true,
            InitialDirectory = initialDirectory,
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }
}
