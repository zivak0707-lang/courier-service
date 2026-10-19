using CourierService.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CourierService.Data.Configurations;

/// <summary>Таблиця Users.</summary>
internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        // NOCASE: «Admin» і «admin» — той самий логін і для пошуку, і для унікальності
        builder.Property(u => u.Login).IsRequired().HasMaxLength(User.LoginMaxLength).UseCollation("NOCASE");
        builder.HasIndex(u => u.Login).IsUnique();
        builder.Property(u => u.PasswordHash).IsRequired().HasMaxLength(100);
        builder.Property(u => u.PasswordSalt).IsRequired().HasMaxLength(100);
        builder.Property(u => u.FullName).IsRequired().HasMaxLength(User.FullNameMaxLength);

        builder.HasOne(u => u.Courier)
            .WithOne(c => c.User)
            .HasForeignKey<Courier>(c => c.UserId);
    }
}

/// <summary>Таблиця Couriers.</summary>
internal sealed class CourierConfiguration : IEntityTypeConfiguration<Courier>
{
    public void Configure(EntityTypeBuilder<Courier> builder)
    {
        builder.ToTable(t =>
        {
            t.HasCheckConstraint("CK_Couriers_MaxWeight", "MaxWeightGrams > 0");
            t.HasCheckConstraint("CK_Couriers_PayoutPercent", "PayoutPercent BETWEEN 0 AND 100");
        });

        builder.Property(c => c.Phone).IsRequired().HasMaxLength(20);
        builder.HasOne(c => c.HomeZone).WithMany().HasForeignKey(c => c.HomeZoneId);
    }
}

/// <summary>Таблиця Clients.</summary>
internal sealed class ClientConfiguration : IEntityTypeConfiguration<Client>
{
    public void Configure(EntityTypeBuilder<Client> builder)
    {
        builder.Property(c => c.Name).IsRequired().HasMaxLength(150);
        builder.Property(c => c.Phone).IsRequired().HasMaxLength(20);
        builder.HasIndex(c => c.Phone).IsUnique();
        builder.Property(c => c.Email).HasMaxLength(100);
        builder.Property(c => c.Address).IsRequired().HasMaxLength(200);
        builder.Property(c => c.Notes).HasMaxLength(500);
    }
}

/// <summary>Таблиця Zones.</summary>
internal sealed class ZoneConfiguration : IEntityTypeConfiguration<Zone>
{
    public void Configure(EntityTypeBuilder<Zone> builder)
    {
        builder.ToTable(t => t.HasCheckConstraint("CK_Zones_Coefficient", "CoefficientPercent BETWEEN 100 AND 300"));

        builder.Property(z => z.Name).IsRequired().HasMaxLength(50);
        builder.HasIndex(z => z.Name).IsUnique();
    }
}

/// <summary>Таблиця Tariffs.</summary>
internal sealed class TariffConfiguration : IEntityTypeConfiguration<Tariff>
{
    public void Configure(EntityTypeBuilder<Tariff> builder)
    {
        builder.ToTable(t =>
        {
            t.HasCheckConstraint("CK_Tariffs_BasePrice", "BasePriceKop > 0");
            t.HasCheckConstraint("CK_Tariffs_Weights", "IncludedWeightGrams >= 0 AND MaxWeightGrams > IncludedWeightGrams");
            t.HasCheckConstraint("CK_Tariffs_DeliveryHours", "DeliveryHours > 0");
            t.HasCheckConstraint("CK_Tariffs_InsurancePermille", "InsurancePermille BETWEEN 0 AND 1000");
        });

        builder.Property(t => t.Name).IsRequired().HasMaxLength(50);
        builder.HasIndex(t => t.Name).IsUnique();
    }
}
