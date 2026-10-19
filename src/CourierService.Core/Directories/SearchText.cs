using System.Globalization;

namespace CourierService.Core.Directories;

/// <summary>
/// Пошук підрядка без урахування регістру за правилами української мови.
/// </summary>
/// <remarks>
/// Довідники фільтруються в пам'яті: порівняння NOCASE і LIKE в SQLite не враховують
/// регістр лише для латиниці, тож «київ» не знайшов би «Київ».
/// </remarks>
public static class SearchText
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
}
