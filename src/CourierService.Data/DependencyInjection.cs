using CourierService.Core.Abstractions;
using CourierService.Core.Security;
using CourierService.Data.Seed;
using CourierService.Data.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CourierService.Data;

/// <summary>
/// Реєстрація шару даних і бізнес-сервісів у DI-контейнері.
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// Реєструє контекст БД SQLite, сервіси безпеки й доступу до даних.
    /// </summary>
    /// <param name="services">Колекція сервісів.</param>
    /// <param name="databasePath">Повний шлях до файлу бази даних.</param>
    /// <returns>Ту саму колекцію для ланцюжкових викликів.</returns>
    public static IServiceCollection AddCourierData(this IServiceCollection services, string databasePath)
    {
        string connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString();
        services.AddDbContextFactory<AppDbContext>(options => options.UseSqlite(connectionString));

        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();
        services.AddSingleton<ICurrentUser, CurrentUser>();
        services.AddSingleton<IAuthService, AuthService>();

        services.AddSingleton<DatabaseSeeder>();
        services.AddSingleton<IDatabaseInitializer, DatabaseInitializer>();

        return services;
    }
}
