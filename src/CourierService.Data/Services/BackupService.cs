using CourierService.Core.Abstractions;
using CourierService.Core.Backups;
using CourierService.Core.Enums;
using CourierService.Core.Exceptions;
using CourierService.Core.Security;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CourierService.Data.Services;

/// <summary>
/// Розташування файлів бази даних і копій.
/// </summary>
/// <param name="DatabasePath">Повний шлях до робочої бази.</param>
/// <param name="BackupsDirectory">Каталог резервних копій.</param>
public sealed record BackupLocation(string DatabasePath, string BackupsDirectory);

/// <summary>
/// Реалізація <see cref="IBackupService"/> через вбудований механізм резервування SQLite (рішення D-35).
/// </summary>
/// <remarks>
/// Звичайне копіювання файлу під час роботи програми може захопити базу посеред запису.
/// Механізм резервування SQLite (<see cref="SqliteConnection.BackupDatabase(SqliteConnection)"/>)
/// копіює узгоджений стан сторінка за сторінкою; ним же копія записується назад під час відновлення.
/// </remarks>
public sealed class BackupService : IBackupService
{
    private const string NotProgramDatabaseMessage = "Обраний файл не є резервною копією бази даних програми.";
    private const string BackupJournalMode = "DELETE";
    private const string LiveJournalMode = "WAL";

    private readonly BackupLocation _location;
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly ICurrentUser _currentUser;
    private readonly IClock _clock;
    private readonly ILogger<BackupService> _logger;

    /// <summary>
    /// Створює сервіс.
    /// </summary>
    /// <param name="location">Шляхи до бази й каталогу копій.</param>
    /// <param name="contextFactory">Фабрика контекстів БД (для переліку міграцій програми).</param>
    /// <param name="currentUser">Поточний користувач.</param>
    /// <param name="clock">Поточний час.</param>
    /// <param name="logger">Журнал.</param>
    public BackupService(
        BackupLocation location,
        IDbContextFactory<AppDbContext> contextFactory,
        ICurrentUser currentUser,
        IClock clock,
        ILogger<BackupService> logger)
    {
        _location = location;
        _contextFactory = contextFactory;
        _currentUser = currentUser;
        _clock = clock;
        _logger = logger;
    }

    /// <inheritdoc />
    public string BackupsDirectory => _location.BackupsDirectory;

    /// <inheritdoc />
    public Task<IReadOnlyList<BackupEntry>> GetBackupsAsync(CancellationToken cancellationToken = default)
    {
        _currentUser.RequireRole(UserRole.Admin);
        return Task.Run(ListBackups, cancellationToken);
    }

    /// <inheritdoc />
    public Task<BackupEntry> CreateBackupAsync(CancellationToken cancellationToken = default)
    {
        _currentUser.RequireRole(UserRole.Admin);
        return Task.Run(() => CreateBackup(BackupKind.Manual), cancellationToken);
    }

    /// <inheritdoc />
    public Task<BackupEntry?> CreateDailyBackupIfNeededAsync(CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            // Під час першого запуску бази ще немає — копіювати нічого
            if (!File.Exists(_location.DatabasePath)
                || BackupCatalog.HasDailyBackup(ListBackups().Select(b => b.File), DateOnly.FromDateTime(_clock.Now)))
            {
                return null;
            }

            BackupEntry created = CreateBackup(BackupKind.Daily);
            DeleteExpiredDailyBackups();
            return created;
        }, cancellationToken);

    /// <inheritdoc />
    public async Task<BackupEntry> RestoreAsync(string backupPath, CancellationToken cancellationToken = default)
    {
        _currentUser.RequireRole(UserRole.Admin);
        IReadOnlySet<string> knownMigrations = await GetKnownMigrationsAsync(cancellationToken);

        return await Task.Run(() =>
        {
            EnsureNotLiveDatabase(backupPath);

            // Перевіряємо й відновлюємо з тимчасової копії файлу: обраний користувачем файл (наприклад, на флешці)
            // лишається недоторканим, навіть якщо SQLite захоче оновити його службові дані
            string stagingPath = StageCopy(backupPath);
            try
            {
                EnsureRestorable(stagingPath, knownMigrations);

                // Страхувальна копія: якщо обрали не ту копію, поточні дані можна повернути
                BackupEntry safetyCopy = CreateBackup(BackupKind.BeforeRestore);
                try
                {
                    // Пул EF тримає відкриті з'єднання з робочою базою — закриваємо їх до й після заміни
                    SqliteConnection.ClearAllPools();
                    CopyDatabase(stagingPath, _location.DatabasePath, LiveJournalMode);
                    SqliteConnection.ClearAllPools();
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SqliteException)
                {
                    throw new BackupException(
                        $"Не вдалося відновити дані: {ex.Message}\n\nПоточні дані збережено в копії {safetyCopy.File.FileName}.", ex);
                }

                _logger.LogWarning("Базу відновлено з копії {Backup}; попередні дані — у {SafetyCopy}", backupPath, safetyCopy.FullPath);
                return safetyCopy;
            }
            finally
            {
                TryDelete(stagingPath);
            }
        }, cancellationToken);
    }

    private void EnsureNotLiveDatabase(string backupPath)
    {
        if (!File.Exists(backupPath))
        {
            throw new DomainValidationException("Файл резервної копії не знайдено.");
        }

        if (string.Equals(Path.GetFullPath(backupPath), Path.GetFullPath(_location.DatabasePath), StringComparison.OrdinalIgnoreCase))
        {
            throw new DomainValidationException("Це файл робочої бази даних. Оберіть резервну копію.");
        }
    }

    private string StageCopy(string backupPath)
    {
        string stagingPath = Path.Combine(_location.BackupsDirectory, $"restore-{Guid.NewGuid():N}.tmp");
        try
        {
            Directory.CreateDirectory(_location.BackupsDirectory);
            File.Copy(backupPath, stagingPath);
            return stagingPath;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new BackupException($"Не вдалося прочитати файл резервної копії: {ex.Message}", ex);
        }
    }

    private IReadOnlyList<BackupEntry> ListBackups()
    {
        if (!Directory.Exists(_location.BackupsDirectory))
        {
            return [];
        }

        return new DirectoryInfo(_location.BackupsDirectory)
            .EnumerateFiles("*" + BackupCatalog.Extension)
            .Select(file => BackupCatalog.TryParse(file.Name) is { } parsed
                ? new BackupEntry(parsed, file.FullName, file.Length)
                : null)
            .OfType<BackupEntry>()
            .OrderByDescending(b => b.File.CreatedAt)
            .ToList();
    }

    private BackupEntry CreateBackup(BackupKind kind)
    {
        string fileName = BackupCatalog.FileNameFor(_clock.Now, kind);
        string path = Path.Combine(_location.BackupsDirectory, fileName);

        // Спершу у тимчасовий файл: обірвана копія не потрапить до списку копій
        string temporaryPath = path + ".tmp";
        try
        {
            Directory.CreateDirectory(_location.BackupsDirectory);
            CopyDatabase(_location.DatabasePath, temporaryPath, BackupJournalMode);
            File.Move(temporaryPath, path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SqliteException)
        {
            TryDelete(temporaryPath);
            throw new BackupException($"Не вдалося створити резервну копію: {ex.Message}", ex);
        }

        _logger.LogInformation("Створено резервну копію {Path}", path);
        return new BackupEntry(BackupCatalog.TryParse(fileName)!, path, new FileInfo(path).Length);
    }

    private void DeleteExpiredDailyBackups()
    {
        foreach (BackupFile expired in BackupCatalog.ExpiredDailyBackups(ListBackups().Select(b => b.File)))
        {
            TryDelete(Path.Combine(_location.BackupsDirectory, expired.FileName));
        }
    }

    private void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Зайвий файл не заважає роботі — лише записуємо в журнал
            _logger.LogWarning(ex, "Не вдалося видалити файл {Path}", path);
        }
    }

    private async Task<IReadOnlySet<string>> GetKnownMigrationsAsync(CancellationToken cancellationToken)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        return context.Database.GetMigrations().ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>
    /// Перевіряє, що файл — неушкоджена база цієї програми, а не довільний файл чи база новішої версії.
    /// </summary>
    private void EnsureRestorable(string stagingPath, IReadOnlySet<string> knownMigrations)
    {
        List<string> migrations;
        try
        {
            using var connection = new SqliteConnection(ConnectionString(stagingPath, SqliteOpenMode.ReadWrite));
            connection.Open();

            if (ExecuteScalar(connection, "PRAGMA quick_check") as string != "ok")
            {
                throw new DomainValidationException("Файл резервної копії пошкоджено.");
            }

            if (ExecuteScalar(connection, "SELECT name FROM sqlite_master WHERE type = 'table' AND name = '__EFMigrationsHistory'") is null)
            {
                throw new DomainValidationException(NotProgramDatabaseMessage);
            }

            migrations = [];
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT MigrationId FROM __EFMigrationsHistory";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                migrations.Add(reader.GetString(0));
            }
        }
        catch (SqliteException ex)
        {
            // Наприклад, «file is not a database» для документа Word чи зображення
            _logger.LogWarning(ex, "Файл копії не вдалося відкрити як базу SQLite");
            throw new DomainValidationException(NotProgramDatabaseMessage);
        }

        if (migrations.Count == 0)
        {
            throw new DomainValidationException(NotProgramDatabaseMessage);
        }

        // Старішу копію програма доповнить міграціями під час запуску, а новішу зрозуміти не зможе
        if (migrations.Any(id => !knownMigrations.Contains(id)))
        {
            throw new DomainValidationException(
                "Копію створено новішою версією програми. Оновіть програму, щоб відновити з неї дані.");
        }
    }

    /// <summary>
    /// Копіює базу механізмом резервування SQLite і задає режим журналу копії.
    /// </summary>
    /// <remarks>
    /// EF Core створює базу в режимі WAL (частина змін живе в окремому файлі -wal). Копія переводиться
    /// в звичайний режим — це один самодостатній файл, який можна переносити; робоча база після
    /// відновлення повертається в WAL.
    /// </remarks>
    private static void CopyDatabase(string sourcePath, string destinationPath, string journalMode)
    {
        using var source = new SqliteConnection(ConnectionString(sourcePath, SqliteOpenMode.ReadWrite));
        using var destination = new SqliteConnection(ConnectionString(destinationPath, SqliteOpenMode.ReadWriteCreate));
        source.Open();
        destination.Open();
        source.BackupDatabase(destination);
        ExecuteScalar(destination, $"PRAGMA journal_mode = {journalMode}");
    }

    private static object? ExecuteScalar(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar();
    }

    // Без пулу: файл звільняється одразу після закриття з'єднання (його можна перейменувати чи видалити)
    private static string ConnectionString(string path, SqliteOpenMode mode) => new SqliteConnectionStringBuilder
    {
        DataSource = path,
        Mode = mode,
        Pooling = false,
    }.ToString();
}
