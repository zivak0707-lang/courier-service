using CourierService.Core;
using CourierService.Core.Entities;
using CourierService.Core.Enums;
using Microsoft.EntityFrameworkCore;

namespace CourierService.Tests.Data;

public class AppDbContextTests : IAsyncLifetime
{
    private SqliteTestDatabase _db = null!;

    public async Task InitializeAsync() => _db = await SqliteTestDatabase.CreateAsync();

    public async Task DisposeAsync() => await _db.DisposeAsync();

    [Fact]
    public async Task Migrations_MatchCurrentModel()
    {
        await using var context = _db.CreateDbContext();

        // Якщо модель змінили, а міграцію не створили, тест падає
        Assert.False(context.Database.HasPendingModelChanges());
    }

    [Fact]
    public async Task Money_IsStoredAsIntegerKopecks()
    {
        await using (var context = _db.CreateDbContext())
        {
            context.Tariffs.Add(NewTariff("Стандарт", basePriceKop: 11000));
            await context.SaveChangesAsync();
        }

        Assert.Equal("integer", _db.ExecuteScalar("SELECT typeof(BasePriceKop) FROM Tariffs"));
        Assert.Equal(11000L, _db.ExecuteScalar("SELECT BasePriceKop FROM Tariffs"));

        await using var readContext = _db.CreateDbContext();
        Tariff tariff = await readContext.Tariffs.SingleAsync();
        Assert.Equal(Money.FromHryvnias(110m), tariff.BasePrice);
    }

    [Fact]
    public async Task Money_CanBeComparedInDatabaseQuery()
    {
        await using (var context = _db.CreateDbContext())
        {
            context.Tariffs.AddRange(NewTariff("Дешевий", 5000), NewTariff("Дорогий", 20000));
            await context.SaveChangesAsync();
        }

        await using var readContext = _db.CreateDbContext();
        var limit = Money.FromHryvnias(100m);
        // Порівняння виконується в SQL над цілими числами
        string[] names = await readContext.Tariffs.Where(t => t.BasePrice > limit).Select(t => t.Name).ToArrayAsync();

        Assert.Equal(["Дорогий"], names);
    }

    [Fact]
    public async Task Enums_AreStoredAsText()
    {
        await using (var context = _db.CreateDbContext())
        {
            context.Users.Add(NewUser("admin", UserRole.Admin));
            await context.SaveChangesAsync();
        }

        Assert.Equal("Admin", _db.ExecuteScalar("SELECT Role FROM Users"));
    }

    [Fact]
    public async Task Login_IsUniqueIgnoringCase()
    {
        await using var context = _db.CreateDbContext();
        context.Users.Add(NewUser("operator", UserRole.Operator));
        await context.SaveChangesAsync();

        context.Users.Add(NewUser("OPERATOR", UserRole.Operator));

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task ClientPhone_IsUnique()
    {
        await using var context = _db.CreateDbContext();
        context.Clients.Add(NewClient("+380500000001"));
        await context.SaveChangesAsync();

        context.Clients.Add(NewClient("+380500000001"));

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task ZoneUsedByCourier_CannotBeDeleted()
    {
        await using (var context = _db.CreateDbContext())
        {
            var zone = new Zone { Name = "Печерський", Bank = RiverBank.Right, CoefficientPercent = 100 };
            context.Couriers.Add(new Courier
            {
                User = NewUser("courier1", UserRole.Courier),
                Phone = "+380500000010",
                TransportType = TransportType.Car,
                MaxWeightGrams = 30_000,
                HomeZone = zone,
            });
            await context.SaveChangesAsync();
        }

        await using var deleteContext = _db.CreateDbContext();
        deleteContext.Zones.Remove(await deleteContext.Zones.SingleAsync());

        await Assert.ThrowsAsync<DbUpdateException>(() => deleteContext.SaveChangesAsync());
    }

    [Theory]
    [InlineData(99)]   // коефіцієнт менший за 1,00
    [InlineData(301)]  // коефіцієнт більший за 3,00
    public async Task ZoneCoefficient_OutOfRange_IsRejectedByDatabase(int coefficientPercent)
    {
        await using var context = _db.CreateDbContext();
        context.Zones.Add(new Zone { Name = "Тест", Bank = RiverBank.Left, CoefficientPercent = coefficientPercent });

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task CourierPayoutPercent_Above100_IsRejectedByDatabase()
    {
        await using var context = _db.CreateDbContext();
        context.Couriers.Add(new Courier
        {
            User = NewUser("courier2", UserRole.Courier),
            Phone = "+380500000011",
            TransportType = TransportType.Foot,
            MaxWeightGrams = 5_000,
            HomeZone = new Zone { Name = "Подільський", Bank = RiverBank.Right, CoefficientPercent = 105 },
            PayoutPercent = 120,
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    private static Tariff NewTariff(string name, long basePriceKop) => new()
    {
        Name = name,
        BasePrice = new Money(basePriceKop),
        IncludedWeightGrams = 2_000,
        PricePerExtraKg = Money.FromHryvnias(12m),
        MaxWeightGrams = 30_000,
        DeliveryHours = 12,
        CrossRiverSurcharge = Money.FromHryvnias(25m),
        InsuranceFreeLimit = Money.FromHryvnias(500m),
        InsurancePermille = 5,
        FragileSurcharge = Money.FromHryvnias(30m),
    };

    private static User NewUser(string login, UserRole role) => new()
    {
        Login = login,
        PasswordHash = "hash",
        PasswordSalt = "salt",
        FullName = "Тестовий Користувач",
        Role = role,
        CreatedAt = new DateTime(2026, 10, 19, 10, 0, 0),
    };

    private static Client NewClient(string phone) => new()
    {
        ClientType = ClientType.Individual,
        Name = "Тестовий Клієнт",
        Phone = phone,
        Address = "вул. Тестова, 1",
        CreatedAt = new DateTime(2026, 10, 19, 10, 0, 0),
    };
}
