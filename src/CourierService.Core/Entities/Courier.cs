using CourierService.Core.Enums;

namespace CourierService.Core.Entities;

/// <summary>
/// Профіль кур'єра. Ім'я береться з пов'язаного облікового запису <see cref="User"/>,
/// щоб не дублювати його в двох таблицях.
/// </summary>
public class Courier : ReferenceEntity
{
    /// <summary>Обліковий запис кур'єра (зв'язок 1:1).</summary>
    public int UserId { get; set; }

    /// <summary>Обліковий запис кур'єра.</summary>
    public User User { get; set; } = null!;

    /// <summary>Телефон у форматі +380XXXXXXXXX.</summary>
    public string Phone { get; set; } = string.Empty;

    /// <summary>Вид транспорту.</summary>
    public TransportType TransportType { get; set; }

    /// <summary>Максимальна вага посилки, яку може перевезти кур'єр, у грамах.</summary>
    public int MaxWeightGrams { get; set; }

    /// <summary>Домашня зона — використовується для автопризначення.</summary>
    public int HomeZoneId { get; set; }

    /// <summary>Домашня зона.</summary>
    public Zone HomeZone { get; set; } = null!;

    /// <summary>Відсоток від вартості доставки, що нараховується кур'єру (рішення D-15).</summary>
    public int PayoutPercent { get; set; } = 50;
}
