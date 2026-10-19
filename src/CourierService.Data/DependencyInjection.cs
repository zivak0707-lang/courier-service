using CourierService.Core.Abstractions;
using CourierService.Core.Directories;
using CourierService.Core.Security;
using CourierService.Core.Validation;
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

        // Довідники: правила перевірки (Core) і сервіси (рішення D-18)
        services.AddSingleton<IValidator<ZoneInput>, ZoneValidator>();
        services.AddSingleton<IValidator<TariffInput>, TariffValidator>();
        services.AddSingleton<IValidator<ClientInput>, ClientValidator>();
        services.AddSingleton<IValidator<CourierInput>, CourierValidator>();
        services.AddSingleton<IValidator<UserInput>, UserValidator>();
        services.AddSingleton<IZoneService, ZoneService>();
        services.AddSingleton<ITariffService, TariffService>();
        services.AddSingleton<IClientService, ClientService>();
        services.AddSingleton<ICourierService, CourierDirectoryService>();
        services.AddSingleton<IUserService, UserService>();

        return services;
    }
}
