using CourierService.Core;
using CourierService.Core.Entities;
using CourierService.Core.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace CourierService.Data;

/// <summary>
/// Контекст бази даних SQLite (рішення D-04). Короткоживучий: створюється
/// через <see cref="IDbContextFactory{TContext}"/> на кожну операцію.
/// </summary>
public class AppDbContext : DbContext
{
    /// <summary>
    /// Створює контекст.
    /// </summary>
    /// <param name="options">Параметри підключення.</param>
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    /// <summary>Користувачі.</summary>
    public DbSet<User> Users => Set<User>();

    /// <summary>Кур'єри.</summary>
    public DbSet<Courier> Couriers => Set<Courier>();

    /// <summary>Клієнти.</summary>
    public DbSet<Client> Clients => Set<Client>();

    /// <summary>Зони доставки.</summary>
    public DbSet<Zone> Zones => Set<Zone>();

    /// <summary>Тарифи.</summary>
    public DbSet<Tariff> Tariffs => Set<Tariff>();

    /// <summary>Замовлення.</summary>
    public DbSet<Order> Orders => Set<Order>();

    /// <summary>Історія змін статусів.</summary>
    public DbSet<OrderStatusChange> OrderStatusChanges => Set<OrderStatusChange>();

    /// <summary>Оплати.</summary>
    public DbSet<Payment> Payments => Set<Payment>();

    /// <inheritdoc />
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Гроші зберігаються цілим числом копійок (рішення D-05)
        configurationBuilder.Properties<Money>().HaveConversion<MoneyToKopecksConverter>();

        // Перелічення зберігаються текстом: у БД видно «Delivered», а не «4»,
        // і порядок значень у коді можна змінювати без міграції даних
        configurationBuilder.Properties<UserRole>().HaveConversion<string>().HaveMaxLength(20);
        configurationBuilder.Properties<TransportType>().HaveConversion<string>().HaveMaxLength(20);
        configurationBuilder.Properties<ClientType>().HaveConversion<string>().HaveMaxLength(20);
        configurationBuilder.Properties<RiverBank>().HaveConversion<string>().HaveMaxLength(20);
        configurationBuilder.Properties<OrderStatus>().HaveConversion<string>().HaveMaxLength(20);
        configurationBuilder.Properties<PaymentPayer>().HaveConversion<string>().HaveMaxLength(20);
        configurationBuilder.Properties<PaymentMethod>().HaveConversion<string>().HaveMaxLength(20);
        configurationBuilder.Properties<PaymentStatus>().HaveConversion<string>().HaveMaxLength(20);
    }

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Конфігурації окремих таблиць — у папці Configurations
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            // Записи, на які є посилання, видаляти заборонено (довідники деактивуються)
            foreach (var foreignKey in entityType.GetForeignKeys())
            {
                foreignKey.DeleteBehavior = DeleteBehavior.Restrict;
            }

            // Колонки з грошима мають суфікс Kop: одиницю видно зі специфікації БД
            foreach (var property in entityType.GetProperties()
                         .Where(p => p.ClrType == typeof(Money) || p.ClrType == typeof(Money?)))
            {
                property.SetColumnName(property.Name + "Kop");
            }
        }
    }

    /// <summary>
    /// Перетворення <see cref="Money"/> ↔ ціле число копійок для збереження в SQLite.
    /// </summary>
    private sealed class MoneyToKopecksConverter : ValueConverter<Money, long>
    {
        public MoneyToKopecksConverter()
            : base(money => money.Kopecks, kopecks => new Money(kopecks))
        {
        }
    }
}
