using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace CourierService.Data.Services;

/// <summary>
/// Розпізнавання помилок SQLite, на які сервіси реагують зрозумілим повідомленням.
/// </summary>
internal static class DbErrors
{
    // SQLITE_CONSTRAINT_UNIQUE
    private const int UniqueConstraintFailed = 2067;

    /// <summary>
    /// Чи порушено унікальний індекс (запис з таким значенням з'явився між перевіркою й збереженням).
    /// </summary>
    /// <param name="exception">Помилка збереження.</param>
    /// <returns><c>true</c> для порушення унікальності.</returns>
    public static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is SqliteException { SqliteExtendedErrorCode: UniqueConstraintFailed };
}
