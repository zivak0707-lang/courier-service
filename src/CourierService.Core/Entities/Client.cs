using CourierService.Core.Enums;

namespace CourierService.Core.Entities;

/// <summary>
/// Клієнт — відправник посилок.
/// </summary>
public class Client : ReferenceEntity
{
    /// <summary>Фізична особа чи компанія.</summary>
    public ClientType ClientType { get; set; }

    /// <summary>ПІБ або назва компанії.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Телефон у форматі +380XXXXXXXXX (унікальний).</summary>
    public string Phone { get; set; } = string.Empty;

    /// <summary>Електронна пошта (необов'язково).</summary>
    public string? Email { get; set; }

    /// <summary>Типова адреса, звідки забирати посилки.</summary>
    public string Address { get; set; } = string.Empty;

    /// <summary>Примітки оператора.</summary>
    public string? Notes { get; set; }

    /// <summary>Дата й час реєстрації клієнта.</summary>
    public DateTime CreatedAt { get; set; }
}
