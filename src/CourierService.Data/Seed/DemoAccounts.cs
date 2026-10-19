using CourierService.Core.Enums;

namespace CourierService.Data.Seed;

/// <summary>
/// Демонстраційний обліковий запис.
/// </summary>
/// <param name="Login">Логін.</param>
/// <param name="Password">Пароль.</param>
/// <param name="FullName">Вигадане ім'я (рішення D-12).</param>
/// <param name="Role">Роль.</param>
public sealed record DemoAccount(string Login, string Password, string FullName, UserRole Role);

/// <summary>
/// Облікові записи, що створюються під час першого запуску, — для демонстрації й перевірки комісією.
/// </summary>
/// <remarks>
/// Це не секрети: паролі навмисно прості й наведені в довідці програми.
/// У реальній експлуатації адміністратор змінює їх після першого входу.
/// </remarks>
public static class DemoAccounts
{
    /// <summary>Адміністратор.</summary>
    public static DemoAccount Admin { get; } = new("admin", "admin123", "Олена Марченко", UserRole.Admin);

    /// <summary>Оператор.</summary>
    public static DemoAccount Operator { get; } = new("operator", "operator123", "Ігор Бондар", UserRole.Operator);

    /// <summary>Кур'єри (однаковий пароль для зручності демонстрації).</summary>
    public static IReadOnlyList<DemoAccount> Couriers { get; } =
    [
        new("courier1", "courier123", "Андрій Ткаченко", UserRole.Courier),
        new("courier2", "courier123", "Марія Лисенко", UserRole.Courier),
        new("courier3", "courier123", "Дмитро Кравчук", UserRole.Courier),
        new("courier4", "courier123", "Оксана Мельник", UserRole.Courier),
    ];

    /// <summary>Усі демонстраційні облікові записи.</summary>
    public static IReadOnlyList<DemoAccount> All { get; } = [Admin, Operator, .. Couriers];
}
