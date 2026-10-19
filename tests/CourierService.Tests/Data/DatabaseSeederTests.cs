using System.IO;
using CourierService.Core;
using CourierService.Core.Abstractions;
using CourierService.Core.Enums;
using CourierService.Core.Security;
using CourierService.Data;
using CourierService.Data.Seed;
using CourierService.Data.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace CourierService.Tests.Data;

/// <summary>Фіксований час для тестів.</summary>
internal sealed class FixedClock(DateTime now) : IClock
{
    public DateTime Now { get; set; } = now;
}

public class DatabaseSeederTests : IAsyncLifetime
{
    private readonly Pbkdf2PasswordHasher _hasher = new();
    private SqliteTestDatabase _db = null!;
    private DatabaseInitializer _initializer = null!;

    private static FixedClock Clock => new(new DateTime(2026, 10, 19, 15, 0, 0));

    public async Task InitializeAsync()
    {
        _db = await SqliteTestDatabase.CreateAsync();
        var seeder = new DatabaseSeeder(_hasher, Clock);
        _initializer = new DatabaseInitializer(_db, seeder, new DemoOrderGenerator(Clock), new NoBackups(), NullLogger<DatabaseInitializer>.Instance);
        await _initializer.InitializeAsync();
    }

    public async Task DisposeAsync() => await _db.DisposeAsync();

    [Fact]
    public async Task Seed_CreatesKyivZonesWithAgreedCoefficients()
    {
        await using var context = _db.CreateDbContext();
        var zones = await context.Zones.ToDictionaryAsync(z => z.Name, z => z.CoefficientPercent);

        Assert.Equal(10, zones.Count);
        Assert.Equal(100, zones["Шевченківський"]);
        Assert.Equal(110, zones["Дніпровський"]);
        Assert.Equal(125, zones["Деснянський"]);
        Assert.Equal(3, await context.Zones.CountAsync(z => z.Bank == RiverBank.Left));
    }

    [Fact]
    public async Task Seed_CreatesTariffsFromMarketResearch()
    {
        await using var context = _db.CreateDbContext();
        var standard = await context.Tariffs.SingleAsync(t => t.Name == "Стандарт");

        Assert.Equal(3, await context.Tariffs.CountAsync());
        Assert.Equal(Money.FromHryvnias(110m), standard.BasePrice);
        Assert.Equal(12, standard.DeliveryHours);
        Assert.Equal(Money.FromHryvnias(25m), standard.CrossRiverSurcharge);
        Assert.Equal(Money.FromHryvnias(500m), standard.InsuranceFreeLimit);
    }

    [Fact]
    public async Task Seed_CreatesUsersOfAllRolesAndCourierProfiles()
    {
        await using var context = _db.CreateDbContext();

        Assert.Equal(1, await context.Users.CountAsync(u => u.Role == UserRole.Admin));
        Assert.Equal(1, await context.Users.CountAsync(u => u.Role == UserRole.Operator));
        Assert.Equal(4, await context.Couriers.CountAsync());
        Assert.Equal(6, await context.Clients.CountAsync());
        Assert.True(await context.Couriers.AllAsync(c => c.PayoutPercent == 50));
    }

    [Fact]
    public async Task Initialize_Twice_DoesNotDuplicateData()
    {
        int firstRunOrders;
        await using (var before = _db.CreateDbContext())
        {
            firstRunOrders = await before.Orders.CountAsync();
        }

        // Новий ініціалізатор — імітація повторного запуску програми з тією самою БД
        var seeder = new DatabaseSeeder(_hasher, new FixedClock(new DateTime(2026, 10, 20, 9, 0, 0)));
        await new DatabaseInitializer(_db, seeder, new DemoOrderGenerator(new FixedClock(new DateTime(2026, 10, 20, 9, 0, 0))), new NoBackups(), NullLogger<DatabaseInitializer>.Instance).InitializeAsync();

        await using var context = _db.CreateDbContext();
        Assert.Equal(10, await context.Zones.CountAsync());
        Assert.Equal(3, await context.Tariffs.CountAsync());
        Assert.Equal(DemoAccounts.All.Count, await context.Users.CountAsync());
        Assert.Equal(firstRunOrders, await context.Orders.CountAsync());
    }

    [Fact]
    public async Task DemoAccounts_CanSignIn()
    {
        var auth = new AuthService(_db, _hasher, new CurrentUser(), NullLogger<AuthService>.Instance);

        foreach (var account in DemoAccounts.All)
        {
            var result = await auth.SignInAsync(account.Login, account.Password);
            Assert.True(result.Succeeded, $"Не вдалося увійти як {account.Login}: {result.ErrorMessage}");
        }
    }

    [Fact]
    public void AddCourierData_AllRegistrationsCanBeResolved()
    {
        var services = new ServiceCollection()
            .AddLogging()
            .AddCourierData(Path.Combine(Path.GetTempPath(), "courier-di-test.db"));

        // Лише перевірка реєстрацій: файл БД при цьому не створюється
        var exception = Record.Exception(() =>
            services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true }).Dispose());

        Assert.Null(exception);
    }

    [Fact]
    public async Task Seed_StoresHashesInsteadOfPasswords()
    {
        await using var context = _db.CreateDbContext();
        var admin = await context.Users.SingleAsync(u => u.Login == DemoAccounts.Admin.Login);

        Assert.NotEqual(DemoAccounts.Admin.Password, admin.PasswordHash);
        Assert.True(_hasher.Verify(DemoAccounts.Admin.Password, admin.PasswordHash, admin.PasswordSalt));
    }
}
