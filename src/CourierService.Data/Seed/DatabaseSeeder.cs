using CourierService.Core;
using CourierService.Core.Abstractions;
using CourierService.Core.Entities;
using CourierService.Core.Enums;
using CourierService.Core.Security;
using Microsoft.EntityFrameworkCore;

namespace CourierService.Data.Seed;

/// <summary>
/// Початкові дані: зони Києва (D-07), тарифи (D-13), демонстраційні користувачі,
/// кур'єри й клієнти (D-12). Усі імена, телефони й адреси вигадані.
/// </summary>
/// <remarks>
/// Кожна група даних додається лише в порожню таблицю, тому повторний запуск нічого не дублює
/// і не перезаписує зміни, зроблені адміністратором.
/// Демонстраційні замовлення додає окремо <see cref="DemoOrderGenerator"/> (D-28).
/// </remarks>
public sealed class DatabaseSeeder
{
    private readonly IPasswordHasher _passwordHasher;
    private readonly IClock _clock;

    /// <summary>
    /// Створює заповнювач початкових даних.
    /// </summary>
    /// <param name="passwordHasher">Хешування паролів демонстраційних користувачів.</param>
    /// <param name="clock">Поточний час для дат створення.</param>
    public DatabaseSeeder(IPasswordHasher passwordHasher, IClock clock)
    {
        _passwordHasher = passwordHasher;
        _clock = clock;
    }

    /// <summary>
    /// Додає початкові дані в порожні таблиці.
    /// </summary>
    /// <param name="context">Контекст БД.</param>
    /// <param name="cancellationToken">Скасування операції.</param>
    /// <returns>Задача.</returns>
    public async Task SeedAsync(AppDbContext context, CancellationToken cancellationToken = default)
    {
        if (!await context.Zones.AnyAsync(cancellationToken))
        {
            context.Zones.AddRange(CreateKyivZones());
            await context.SaveChangesAsync(cancellationToken);
        }

        if (!await context.Tariffs.AnyAsync(cancellationToken))
        {
            context.Tariffs.AddRange(CreateTariffs());
            await context.SaveChangesAsync(cancellationToken);
        }

        if (!await context.Users.AnyAsync(cancellationToken))
        {
            var zones = await context.Zones.ToDictionaryAsync(z => z.Name, cancellationToken);
            context.Users.AddRange(CreateUser(DemoAccounts.Admin), CreateUser(DemoAccounts.Operator));
            context.Couriers.AddRange(CreateCouriers(zones));
            await context.SaveChangesAsync(cancellationToken);
        }

        if (!await context.Clients.AnyAsync(cancellationToken))
        {
            context.Clients.AddRange(CreateClients());
            await context.SaveChangesAsync(cancellationToken);
        }
    }

    /// <summary>Райони Києва з коефіцієнтами віддаленості від центру (рішення D-07).</summary>
    /// <returns>10 зон.</returns>
    public static IReadOnlyList<Zone> CreateKyivZones() =>
    [
        new() { Name = "Шевченківський", Bank = RiverBank.Right, CoefficientPercent = 100 },
        new() { Name = "Печерський", Bank = RiverBank.Right, CoefficientPercent = 100 },
        new() { Name = "Подільський", Bank = RiverBank.Right, CoefficientPercent = 105 },
        new() { Name = "Солом'янський", Bank = RiverBank.Right, CoefficientPercent = 110 },
        new() { Name = "Дніпровський", Bank = RiverBank.Left, CoefficientPercent = 110 },
        new() { Name = "Оболонський", Bank = RiverBank.Right, CoefficientPercent = 115 },
        new() { Name = "Голосіївський", Bank = RiverBank.Right, CoefficientPercent = 115 },
        new() { Name = "Святошинський", Bank = RiverBank.Right, CoefficientPercent = 120 },
        new() { Name = "Дарницький", Bank = RiverBank.Left, CoefficientPercent = 120 },
        new() { Name = "Деснянський", Bank = RiverBank.Left, CoefficientPercent = 125 },
    ];

    /// <summary>Тарифи за результатами аналізу ринку (рішення D-13).</summary>
    /// <returns>3 тарифи.</returns>
    public static IReadOnlyList<Tariff> CreateTariffs() =>
    [
        CreateTariff("Стандарт", basePrice: 110, perExtraKg: 12, maxWeightKg: 30, deliveryHours: 12, fragile: 30),
        CreateTariff("Експрес", basePrice: 150, perExtraKg: 15, maxWeightKg: 20, deliveryHours: 4, fragile: 40),
        CreateTariff("Термінова", basePrice: 200, perExtraKg: 20, maxWeightKg: 10, deliveryHours: 2, fragile: 50),
    ];

    private static Tariff CreateTariff(
        string name, decimal basePrice, decimal perExtraKg, int maxWeightKg, int deliveryHours, decimal fragile) => new()
    {
        Name = name,
        BasePrice = Money.FromHryvnias(basePrice),
        IncludedWeightGrams = 2_000,
        PricePerExtraKg = Money.FromHryvnias(perExtraKg),
        MaxWeightGrams = maxWeightKg * 1_000,
        DeliveryHours = deliveryHours,
        CrossRiverSurcharge = Money.FromHryvnias(25m),
        InsuranceFreeLimit = Money.FromHryvnias(500m),
        InsurancePermille = 5,
        FragileSurcharge = Money.FromHryvnias(fragile),
    };

    private User CreateUser(DemoAccount account)
    {
        var password = _passwordHasher.Hash(account.Password);
        return new User
        {
            Login = account.Login,
            PasswordHash = password.Hash,
            PasswordSalt = password.Salt,
            FullName = account.FullName,
            Role = account.Role,
            CreatedAt = _clock.Now,
        };
    }

    private IEnumerable<Courier> CreateCouriers(IReadOnlyDictionary<string, Zone> zones)
    {
        // Вантажопідйомність за видом транспорту: пішки 5 кг, велосипед 10 кг, авто 30 кг
        (TransportType Transport, int MaxWeightGrams, string Zone, string Phone)[] profiles =
        [
            (TransportType.Car, 30_000, "Шевченківський", "+380500000101"),
            (TransportType.Bicycle, 10_000, "Оболонський", "+380500000102"),
            (TransportType.Foot, 5_000, "Печерський", "+380500000103"),
            (TransportType.Car, 30_000, "Дарницький", "+380500000104"),
        ];

        return DemoAccounts.Couriers.Zip(profiles, (account, profile) => new Courier
        {
            User = CreateUser(account),
            Phone = profile.Phone,
            TransportType = profile.Transport,
            MaxWeightGrams = profile.MaxWeightGrams,
            HomeZone = zones[profile.Zone],
            PayoutPercent = 50,
        });
    }

    private IEnumerable<Client> CreateClients()
    {
        DateTime now = _clock.Now;
        return
        [
            new() { ClientType = ClientType.Company, Name = "ТОВ «Квітковий двір»", Phone = "+380670000201",
                    Email = "orders@kvitkovyi-dvir.example", Address = "вул. Каштанова, 12", CreatedAt = now },
            new() { ClientType = ClientType.Company, Name = "Інтернет-магазин «Книжкова полиця»", Phone = "+380670000202",
                    Email = "shop@knyzhkova-polytsia.example", Address = "вул. Липова, 5", CreatedAt = now },
            new() { ClientType = ClientType.Company, Name = "ФОП Гнатюк Р. В. («Чайна майстерня»)", Phone = "+380670000203",
                    Address = "просп. Світанковий, 48", CreatedAt = now },
            new() { ClientType = ClientType.Company, Name = "ТОВ «Тиха кава»", Phone = "+380670000206",
                    Email = "office@tykha-kava.example", Address = "вул. Річкова, 9", CreatedAt = now },
            new() { ClientType = ClientType.Individual, Name = "Наталія Руденко", Phone = "+380930000204",
                    Address = "вул. Яблунева, 3, кв. 17", CreatedAt = now },
            new() { ClientType = ClientType.Individual, Name = "Павло Остапенко", Phone = "+380930000205",
                    Address = "вул. Соснова, 21", CreatedAt = now },
        ];
    }
}
