using CourierService.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace CourierService.Tests.Data;

/// <summary>
/// Справжня база SQLite у пам'яті для тестів. Схема створюється тими самими міграціями,
/// що й у програмі, тож тести перевіряють і їх.
/// </summary>
internal sealed class SqliteTestDatabase : IDbContextFactory<AppDbContext>, IAsyncDisposable
{
    // База в пам'яті існує, доки відкрите з'єднання
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly DbContextOptions<AppDbContext> _options;

    private SqliteTestDatabase()
    {
        _connection.Open();
        _options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;
    }

    public static async Task<SqliteTestDatabase> CreateAsync()
    {
        var database = new SqliteTestDatabase();
        await using var context = database.CreateDbContext();
        await context.Database.MigrateAsync();
        return database;
    }

    public AppDbContext CreateDbContext() => new(_options);

    public async ValueTask DisposeAsync() => await _connection.DisposeAsync();

    /// <summary>Виконує SQL-запит, що повертає одне значення (для перевірки фактичного вмісту колонок).</summary>
    public object? ExecuteScalar(string sql)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar();
    }
}
