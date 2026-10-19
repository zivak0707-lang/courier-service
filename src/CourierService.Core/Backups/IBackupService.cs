using CourierService.Core.Exceptions;

namespace CourierService.Core.Backups;

/// <summary>
/// Резервна копія з місцем на диску й розміром.
/// </summary>
/// <param name="File">Опис копії (дата, вид).</param>
/// <param name="FullPath">Повний шлях до файлу.</param>
/// <param name="SizeBytes">Розмір у байтах.</param>
public sealed record BackupEntry(BackupFile File, string FullPath, long SizeBytes);

/// <summary>
/// Резервне копіювання й відновлення бази даних (рішення D-35).
/// </summary>
public interface IBackupService
{
    /// <summary>Каталог, у якому зберігаються копії.</summary>
    string BackupsDirectory { get; }

    /// <summary>
    /// Список копій, найновіші першими (лише адміністратор).
    /// </summary>
    /// <param name="cancellationToken">Скасування операції.</param>
    /// <returns>Копії з каталогу копій.</returns>
    Task<IReadOnlyList<BackupEntry>> GetBackupsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Робить ручну копію поточної бази (лише адміністратор).
    /// </summary>
    /// <param name="cancellationToken">Скасування операції.</param>
    /// <returns>Створена копія.</returns>
    /// <exception cref="BackupException">Не вдалося записати файл.</exception>
    Task<BackupEntry> CreateBackupAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Робить автоматичну копію, якщо сьогодні її ще не було, і видаляє застарілі автоматичні копії.
    /// Викликається під час запуску програми, до входу користувача.
    /// </summary>
    /// <param name="cancellationToken">Скасування операції.</param>
    /// <returns>Створена копія або <c>null</c>, якщо копія не потрібна (уже є або бази ще немає).</returns>
    /// <exception cref="BackupException">Не вдалося записати файл.</exception>
    Task<BackupEntry?> CreateDailyBackupIfNeededAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Замінює поточну базу даними з копії (лише адміністратор). Перед заміною файл копії перевіряється,
    /// а поточна база зберігається страхувальною копією. Після відновлення програму треба перезапустити.
    /// </summary>
    /// <param name="backupPath">Шлях до файлу копії.</param>
    /// <param name="cancellationToken">Скасування операції.</param>
    /// <returns>Страхувальна копія даних, що були до відновлення.</returns>
    /// <exception cref="DomainValidationException">Файл не є придатною копією бази програми.</exception>
    /// <exception cref="BackupException">Не вдалося прочитати чи записати файл.</exception>
    Task<BackupEntry> RestoreAsync(string backupPath, CancellationToken cancellationToken = default);
}
