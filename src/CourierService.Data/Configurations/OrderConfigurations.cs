using CourierService.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CourierService.Data.Configurations;

/// <summary>Таблиця Orders.</summary>
internal sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.ToTable(t =>
        {
            t.HasCheckConstraint("CK_Orders_Weight", "WeightGrams > 0");
            t.HasCheckConstraint("CK_Orders_Dimensions", "LengthCm > 0 AND WidthCm > 0 AND HeightCm > 0");
            t.HasCheckConstraint("CK_Orders_Price", "PriceKop >= 0");
        });

        builder.Property(o => o.TrackingNumber).IsRequired().HasMaxLength(20);
        builder.HasIndex(o => o.TrackingNumber).IsUnique();
        builder.Property(o => o.RecipientName).IsRequired().HasMaxLength(100);
        builder.Property(o => o.RecipientPhone).IsRequired().HasMaxLength(20);
        builder.Property(o => o.PickupAddress).IsRequired().HasMaxLength(Order.AddressMaxLength);
        builder.Property(o => o.DeliveryAddress).IsRequired().HasMaxLength(Order.AddressMaxLength);
        builder.Property(o => o.Comment).HasMaxLength(500);

        // Індекси для фільтрів списку замовлень і звітів
        builder.HasIndex(o => o.Status);
        builder.HasIndex(o => o.CreatedAt);

        builder.HasOne(o => o.Client).WithMany().HasForeignKey(o => o.ClientId);
        builder.HasOne(o => o.PickupZone).WithMany().HasForeignKey(o => o.PickupZoneId);
        builder.HasOne(o => o.DeliveryZone).WithMany().HasForeignKey(o => o.DeliveryZoneId);
        builder.HasOne(o => o.Tariff).WithMany().HasForeignKey(o => o.TariffId);
        builder.HasOne(o => o.Courier).WithMany().HasForeignKey(o => o.CourierId);
        builder.HasOne(o => o.CreatedBy).WithMany().HasForeignKey(o => o.CreatedByUserId);
    }
}

/// <summary>Таблиця OrderStatusChanges.</summary>
internal sealed class OrderStatusChangeConfiguration : IEntityTypeConfiguration<OrderStatusChange>
{
    public void Configure(EntityTypeBuilder<OrderStatusChange> builder)
    {
        builder.Property(c => c.Reason).HasMaxLength(500);

        builder.HasOne(c => c.Order).WithMany(o => o.StatusChanges).HasForeignKey(c => c.OrderId);
        builder.HasOne(c => c.ChangedBy).WithMany().HasForeignKey(c => c.ChangedByUserId);
    }
}

/// <summary>Таблиця Payments.</summary>
internal sealed class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.ToTable(t => t.HasCheckConstraint("CK_Payments_Amount", "AmountKop >= 0"));

        builder.Property(p => p.Note).HasMaxLength(500);

        // Звіт про виручку відбирає оплачені платежі за датою оплати
        builder.HasIndex(p => new { p.Status, p.PaidAt });

        builder.HasOne(p => p.Order).WithOne(o => o.Payment).HasForeignKey<Payment>(p => p.OrderId);
        builder.HasOne(p => p.ConfirmedBy).WithMany().HasForeignKey(p => p.ConfirmedByUserId);
    }
}
