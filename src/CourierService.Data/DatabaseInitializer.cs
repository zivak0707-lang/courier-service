using CourierService.Data.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CourierService.Data;

/// <summary>
/// Підготовка бази даних під час запуску програми.
/// </summary>
public interface IDatabaseInitializer
{
    /// <summary>
    /// Створює або оновлює схему БД міграціями й додає початкові дані.
    /// Повторний виклик нічого не робить.
    /// </summary>
    /// <param name="cancellationToken">Скасування операції.</param>
    /// <returns>Задача.</returns>
    Task InitializeAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Реалізація <see cref="IDatabaseInitializer"/>: файл БД створюється автоматично під час першого запуску.
/// </summary>
public sealed class DatabaseInitializer : IDatabaseInitializer
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly DatabaseSeeder _seeder;
    private readonly ILogger<DatabaseInitializer> _logger;
    private bool _initialized;

    /// <summary>
    /// Створює ініціалізатор.
    /// </summary>
    /// <param name="contextFactory">Фабрика контекстів БД.</param>
    /// <param name="seeder">Початкові дані.</param>
    /// <param name="logger">Журнал.</param>
    public DatabaseInitializer(
        IDbContextFactory<AppDbContext> contextFactory,
        DatabaseSeeder seeder,
        ILogger<DatabaseInitializer> logger)
    {
        _contextFactory = contextFactory;
        _seeder = seeder;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_initialized)
        {
            return;
        }

        // Міграції й хешування демо-паролів займають помітний час — виконуємо поза потоком інтерфейсу
        await Task.Run(async () =>
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            await context.Database.MigrateAsync(cancellationToken);
            await _seeder.SeedAsync(context, cancellationToken);
        }, cancellationToken);

        _initialized = true;
        _logger.LogInformation("Базу даних підготовлено");
    }
}
