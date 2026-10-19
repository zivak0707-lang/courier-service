using System.Globalization;

namespace CourierService.Core.Reports;

/// <summary>Вид значення в колонці звіту: від нього залежить показ і формат комірки в Excel.</summary>
public enum ReportValueKind
{
    /// <summary>Текст.</summary>
    Text = 1,

    /// <summary>Ціле число.</summary>
    Integer = 2,

    /// <summary>Гроші (<see cref="Money"/>).</summary>
    Money = 3,

    /// <summary>Відсоток (decimal від 0 до 100).</summary>
    Percent = 4,

    /// <summary>Дата.</summary>
    Date = 5,

    /// <summary>Дата й час.</summary>
    DateTime = 6,

    /// <summary>Години (decimal).</summary>
    Hours = 7,
}

/// <summary>Колонка звіту.</summary>
/// <param name="Header">Заголовок.</param>
/// <param name="Kind">Вид значень.</param>
public sealed record ReportColumn(string Header, ReportValueKind Kind);

/// <summary>Підсумковий показник над таблицею («Виручка за період: 12 345,00 грн»).</summary>
/// <param name="Label">Назва.</param>
/// <param name="Kind">Вид значення.</param>
/// <param name="Value">Значення.</param>
public sealed record ReportSummaryItem(string Label, ReportValueKind Kind, object? Value)
{
    /// <summary>Значення для показу.</summary>
    public string Text => ReportFormat.Display(Kind, Value);
}

/// <summary>
/// Готовий звіт — універсальна таблиця (рішення D-30): її однаково показує сторінка звітів
/// і зберігає експорт у Excel. Значення зберігаються «як є» (гроші, числа, дати), а не текстом.
/// </summary>
/// <param name="Title">Назва звіту.</param>
/// <param name="Subtitle">Період або момент формування.</param>
/// <param name="Columns">Колонки.</param>
/// <param name="Rows">Рядки (значення в порядку колонок).</param>
/// <param name="Totals">Рядок «Разом» або <c>null</c>.</param>
/// <param name="Summary">Підсумкові показники.</param>
/// <param name="Notes">Пояснення, як рахується звіт.</param>
public sealed record ReportTable(
    string Title,
    string Subtitle,
    IReadOnlyList<ReportColumn> Columns,
    IReadOnlyList<IReadOnlyList<object?>> Rows,
    IReadOnlyList<object?>? Totals,
    IReadOnlyList<ReportSummaryItem> Summary,
    IReadOnlyList<string> Notes);

/// <summary>
/// Показ значень звіту українською.
/// </summary>
public static class ReportFormat
{
    private static readonly CultureInfo Ukrainian = CultureInfo.GetCultureInfo("uk-UA");

    /// <summary>
    /// Текст значення для показу в таблиці.
    /// </summary>
    /// <param name="kind">Вид значення.</param>
    /// <param name="value">Значення.</param>
    /// <returns>Текст; для порожнього значення — «—».</returns>
    public static string Display(ReportValueKind kind, object? value) => value switch
    {
        null => "—",
        Money money => money.ToString(),
        System.DateTime date when kind == ReportValueKind.Date => date.ToString("dd.MM.yyyy", Ukrainian),
        System.DateTime dateTime => dateTime.ToString("dd.MM.yyyy HH:mm", Ukrainian),
        decimal percent when kind == ReportValueKind.Percent => percent.ToString("0.0", Ukrainian) + " %",
        decimal hours when kind == ReportValueKind.Hours => hours.ToString("0.#", Ukrainian) + " год",
        IFormattable number => number.ToString(null, Ukrainian),
        _ => value.ToString() ?? string.Empty,
    };
}
