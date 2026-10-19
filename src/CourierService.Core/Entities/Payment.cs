using CourierService.Core.Enums;

namespace CourierService.Core.Entities;

/// <summary>
/// Оплата доставки — імітація без платіжного шлюзу (рішення D-09). Одна оплата на замовлення.
/// </summary>
public class Payment : EntityBase
{
    /// <summary>Замовлення (унікальне: зв'язок 1:1).</summary>
    public int OrderId { get; set; }

    /// <summary>Замовлення.</summary>
    public Order Order { get; set; } = null!;

    /// <summary>Хто платить.</summary>
    public PaymentPayer Payer { get; set; }

    /// <summary>Спосіб оплати.</summary>
    public PaymentMethod Method { get; set; }

    /// <summary>Сума до оплати (дорівнює вартості доставки).</summary>
    public Money Amount { get; set; }

    /// <summary>Стан оплати.</summary>
    public PaymentStatus Status { get; set; } = PaymentStatus.Pending;

    /// <summary>Дата й час оплати.</summary>
    public DateTime? PaidAt { get; set; }

    /// <summary>Хто підтвердив отримання грошей (кур'єр або оператор).</summary>
    public int? ConfirmedByUserId { get; set; }

    /// <summary>Хто підтвердив отримання грошей.</summary>
    public User? ConfirmedBy { get; set; }

    /// <summary>Примітка.</summary>
    public string? Note { get; set; }
}
