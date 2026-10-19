using CourierService.Core.Enums;

namespace CourierService.Core.Entities;

/// <summary>
/// Обліковий запис користувача програми.
/// </summary>
public class User : ReferenceEntity
{
    /// <summary>Максимальна довжина логіна.</summary>
    public const int LoginMaxLength = 50;

    /// <summary>Максимальна довжина ПІБ.</summary>
    public const int FullNameMaxLength = 100;

    /// <summary>Логін (унікальний без урахування регістру).</summary>
    public string Login { get; set; } = string.Empty;

    /// <summary>Хеш пароля PBKDF2 у Base64. Сам пароль не зберігається.</summary>
    public string PasswordHash { get; set; } = string.Empty;

    /// <summary>Випадкова сіль для хешування пароля у Base64.</summary>
    public string PasswordSalt { get; set; } = string.Empty;

    /// <summary>Прізвище та ім'я.</summary>
    public string FullName { get; set; } = string.Empty;

    /// <summary>Роль, що визначає доступні функції.</summary>
    public UserRole Role { get; set; }

    /// <summary>Дата й час створення облікового запису.</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>Профіль кур'єра (лише для ролі «Кур'єр»).</summary>
    public Courier? Courier { get; set; }
}
