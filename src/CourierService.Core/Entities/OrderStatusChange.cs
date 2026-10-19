using CourierService.Core.Enums;

namespace CourierService.Core.Entities;

/// <summary>
/// Запис історії зміни статусу замовлення: хто, коли й чому змінив статус.
/// </summary>
public class OrderStatusChange : EntityBase
{
    /// <summary>Замовлення.</summary>
    public int OrderId { get; set; }

    /// <summary>Замовлення.</summary>
    public Order Order { get; set; } = null!;

    /// <summary>Попередній статус (порожній для створення замовлення).</summary>
    public OrderStatus? FromStatus { get; set; }

    /// <summary>Новий статус.</summary>
    public OrderStatus ToStatus { get; set; }

    /// <summary>Дата й час зміни.</summary>
    public DateTime ChangedAt { get; set; }

    /// <summary>Хто змінив статус.</summary>
    public int ChangedByUserId { get; set; }

    /// <summary>Хто змінив статус.</summary>
    public User ChangedBy { get; set; } = null!;

    /// <summary>Причина (обов'язкова для невдалої спроби доставки).</summary>
    public string? Reason { get; set; }
}
