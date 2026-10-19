namespace CourierService.Core.Entities;

/// <summary>
/// Базовий клас усіх сутностей: первинний ключ.
/// </summary>
public abstract class EntityBase
{
    /// <summary>Первинний ключ (генерується базою даних).</summary>
    public int Id { get; set; }
}

/// <summary>
/// Сутність довідника. Такі записи не видаляються фізично, бо на них посилаються
/// замовлення, а деактивуються (рішення D-04).
/// </summary>
public abstract class ReferenceEntity : EntityBase
{
    /// <summary>Чи активний запис (неактивні не пропонуються для нових замовлень).</summary>
    public bool IsActive { get; set; } = true;
}
