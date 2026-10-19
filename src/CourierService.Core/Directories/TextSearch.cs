using System.Globalization;

namespace CourierService.Core.Directories;

/// <summary>
/// Пошук підрядка без урахування регістру за правилами української мови.
/// </summary>
/// <remarks>
/// Довідники фільтруються в пам'яті: порівняння NOCASE і LIKE в SQLite не враховують
/// регістр лише для латиниці, тож «київ» не знайшов би «Київ».
/// </remarks>
public static class TextSearch
{
    private static readonly CompareInfo Ukrainian = CultureInfo.GetCultureInfo("uk-UA").CompareInfo;

    /// <summary>
    /// Чи містить хоча б одне з полів запис пошуку.
    /// </summary>
    /// <param name="query">Запит користувача; порожній запит відповідає будь-якому запису.</param>
    /// <param name="fields">Поля запису, у яких шукати.</param>
    /// <returns><c>true</c>, якщо запис відповідає запиту.</returns>
    public static bool Matches(string? query, params string?[] fields)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return true;
        }

        string trimmed = query.Trim();
        return fields.Any(field => field is not null
            && Ukrainian.IndexOf(field, trimmed, CompareOptions.IgnoreCase) >= 0);
    }

    /// <summary>
    /// Чи однакові два тексти без урахування регістру й пробілів на краях («Стандарт» = « стандарт »).
    /// </summary>
    /// <param name="left">Перший текст.</param>
    /// <param name="right">Другий текст.</param>
    /// <returns><c>true</c>, якщо тексти однакові.</returns>
    public static bool SameText(string? left, string? right) =>
        Ukrainian.Compare(left?.Trim(), right?.Trim(), CompareOptions.IgnoreCase) == 0;

    /// <summary>
    /// Порівняння для сортування списків за українським алфавітом (і, ї, є, ґ — на своїх місцях).
    /// </summary>
    public static StringComparer UkrainianComparer { get; } =
        StringComparer.Create(CultureInfo.GetCultureInfo("uk-UA"), ignoreCase: true);
}
