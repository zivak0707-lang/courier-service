using CourierService.Core.Enums;
using CourierService.Core.Exceptions;
using CourierService.Core.Orders;

namespace CourierService.Core.Entities;

/// <summary>
/// Замовлення на доставку посилки.
/// </summary>
/// <remarks>
/// Ціна та її складові — знімок на момент створення: зміна тарифу не змінює старі замовлення.
/// Статус змінюється лише через <see cref="ChangeStatus"/> за правилами життєвого циклу (D-24), тому сетер закритий.
/// </remarks>
public class Order : EntityBase
{
    /// <summary>Максимальна довжина адреси.</summary>
    public const int AddressMaxLength = 200;

    /// <summary>Трек-номер (унікальний).</summary>
    public string TrackingNumber { get; set; } = string.Empty;

    /// <summary>Відправник.</summary>
    public int ClientId { get; set; }

    /// <summary>Відправник.</summary>
    public Client Client { get; set; } = null!;

    /// <summary>ПІБ одержувача.</summary>
    public string RecipientName { get; set; } = string.Empty;

    /// <summary>Телефон одержувача.</summary>
    public string RecipientPhone { get; set; } = string.Empty;

    /// <summary>Адреса забору.</summary>
    public string PickupAddress { get; set; } = string.Empty;

    /// <summary>Зона забору.</summary>
    public int PickupZoneId { get; set; }

    /// <summary>Зона забору.</summary>
    public Zone PickupZone { get; set; } = null!;

    /// <summary>Адреса доставки.</summary>
    public string DeliveryAddress { get; set; } = string.Empty;

    /// <summary>Зона доставки.</summary>
    public int DeliveryZoneId { get; set; }

    /// <summary>Зона доставки.</summary>
    public Zone DeliveryZone { get; set; } = null!;

    /// <summary>Тариф.</summary>
    public int TariffId { get; set; }

    /// <summary>Тариф.</summary>
    public Tariff Tariff { get; set; } = null!;

    /// <summary>Фактична вага, г.</summary>
    public int WeightGrams { get; set; }

    /// <summary>Довжина, см.</summary>
    public int LengthCm { get; set; }

    /// <summary>Ширина, см.</summary>
    public int WidthCm { get; set; }

    /// <summary>Висота, см.</summary>
    public int HeightCm { get; set; }

    /// <summary>Оголошена цінність.</summary>
    public Money DeclaredValue { get; set; }

    /// <summary>Чи крихка посилка.</summary>
    public bool IsFragile { get; set; }

    /// <summary>Розрахункова вага: більша з фактичної та об'ємної, г.</summary>
    public int ChargeableWeightGrams { get; set; }

    /// <summary>Застосований коефіцієнт зони у відсотках.</summary>
    public int ZoneCoefficientPercent { get; set; }

    /// <summary>Базова вартість з урахуванням коефіцієнта зони.</summary>
    public Money BaseCost { get; set; }

    /// <summary>Вартість додаткової ваги з урахуванням коефіцієнта зони.</summary>
    public Money WeightCost { get; set; }

    /// <summary>Доплата за переїзд через Дніпро.</summary>
    public Money CrossRiverCost { get; set; }

    /// <summary>Вартість страхування.</summary>
    public Money InsuranceCost { get; set; }

    /// <summary>Доплата за крихкість.</summary>
    public Money FragileCost { get; set; }

    /// <summary>Підсумкова вартість доставки — сума складових.</summary>
    public Money Price { get; set; }

    /// <summary>Поточний статус.</summary>
    public OrderStatus Status { get; private set; } = OrderStatus.New;

    /// <summary>Призначений кур'єр.</summary>
    public int? CourierId { get; set; }

    /// <summary>Призначений кур'єр.</summary>
    public Courier? Courier { get; set; }

    /// <summary>Нарахування кур'єру — знімок у момент доставки (рішення D-15).</summary>
    public Money? CourierPayout { get; set; }

    /// <summary>Хто оформив замовлення.</summary>
    public int CreatedByUserId { get; set; }

    /// <summary>Хто оформив замовлення.</summary>
    public User CreatedBy { get; set; } = null!;

    /// <summary>Дата й час оформлення.</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>Крайній термін доставки (рішення D-14).</summary>
    public DateTime Deadline { get; set; }

    /// <summary>Дата й час фактичної доставки.</summary>
    public DateTime? DeliveredAt { get; set; }

    /// <summary>Коментар до замовлення.</summary>
    public string? Comment { get; set; }

    /// <summary>Оплата доставки (1:1).</summary>
    public Payment? Payment { get; set; }

    /// <summary>Історія змін статусу.</summary>
    public ICollection<OrderStatusChange> StatusChanges { get; } = [];

    /// <summary>
    /// Змінює статус і записує зміну в історію. Неможливий перехід (якого немає в таблиці
    /// <see cref="OrderWorkflow.Transitions"/>) відхиляється, навіть якщо його спробує зробити код в обхід правил.
    /// </summary>
    /// <param name="newStatus">Новий статус.</param>
    /// <param name="changedByUserId">Хто змінює.</param>
    /// <param name="changedAt">Коли.</param>
    /// <param name="reason">Причина (для невдалої спроби).</param>
    /// <exception cref="InvalidStatusTransitionException">Переходу немає в таблиці.</exception>
    public void ChangeStatus(OrderStatus newStatus, int changedByUserId, DateTime changedAt, string? reason = null)
    {
        if (!OrderWorkflow.IsTransitionAllowed(Status, newStatus))
        {
            throw new InvalidStatusTransitionException(
                $"Не можна змінити статус «{Status.ToDisplayName()}» на «{newStatus.ToDisplayName()}».");
        }

        StatusChanges.Add(new OrderStatusChange
        {
            FromStatus = Status,
            ToStatus = newStatus,
            ChangedAt = changedAt,
            ChangedByUserId = changedByUserId,
            Reason = reason,
        });
        Status = newStatus;
    }
}
