using CourierService.Core.Enums;

namespace CourierService.App.Navigation;

/// <summary>
/// Які розділи доступні кожній ролі.
/// </summary>
/// <remarks>
/// Ця таблиця лише приховує недоступні пункти меню. Справжня перевірка прав
/// виконується в сервісах через ICurrentUser (рішення D-08).
/// </remarks>
public static class SectionAccess
{
    private static readonly UserRole[] AdminOnly = [UserRole.Admin];
    private static readonly UserRole[] OperatorOnly = [UserRole.Operator];
    private static readonly UserRole[] CourierOnly = [UserRole.Courier];
    private static readonly UserRole[] AdminAndOperator = [UserRole.Admin, UserRole.Operator];

    private static readonly Dictionary<AppSection, UserRole[]> AllowedRoles = new()
    {
        [AppSection.NewOrder] = OperatorOnly,
        [AppSection.OrderList] = AdminAndOperator,
        [AppSection.TrackingSearch] = AdminAndOperator,
        [AppSection.MyActiveDeliveries] = CourierOnly,
        [AppSection.MyCompletedDeliveries] = CourierOnly,
        [AppSection.Clients] = AdminAndOperator,
        [AppSection.Couriers] = AdminAndOperator,
        [AppSection.Zones] = AdminAndOperator,
        [AppSection.Tariffs] = AdminAndOperator,
        [AppSection.Dashboard] = AdminAndOperator,
        [AppSection.RevenueReport] = AdminAndOperator,
        [AppSection.UnpaidReport] = AdminAndOperator,
        [AppSection.CourierPerformanceReport] = AdminAndOperator,
        [AppSection.CourierPayoutReport] = AdminOnly,
        [AppSection.Users] = AdminOnly,
        [AppSection.Backups] = AdminOnly,
    };

    /// <summary>Розділи меню «Замовлення».</summary>
    public static IReadOnlyList<AppSection> OrderSections { get; } =
        [AppSection.NewOrder, AppSection.OrderList, AppSection.TrackingSearch];

    /// <summary>Розділи меню «Мої доставки».</summary>
    public static IReadOnlyList<AppSection> MyDeliverySections { get; } =
        [AppSection.MyActiveDeliveries, AppSection.MyCompletedDeliveries];

    /// <summary>Розділи меню «Довідники».</summary>
    public static IReadOnlyList<AppSection> DirectorySections { get; } =
        [AppSection.Clients, AppSection.Couriers, AppSection.Zones, AppSection.Tariffs];

    /// <summary>Розділи меню «Звіти».</summary>
    public static IReadOnlyList<AppSection> ReportSections { get; } =
    [
        AppSection.Dashboard, AppSection.RevenueReport, AppSection.UnpaidReport,
        AppSection.CourierPerformanceReport, AppSection.CourierPayoutReport,
    ];

    /// <summary>Розділи меню «Адміністрування».</summary>
    public static IReadOnlyList<AppSection> AdministrationSections { get; } = [AppSection.Users, AppSection.Backups];

    /// <summary>
    /// Чи доступний розділ ролі.
    /// </summary>
    /// <param name="section">Розділ.</param>
    /// <param name="role">Роль користувача або <c>null</c>, якщо вхід не виконано.</param>
    /// <returns><c>true</c>, якщо розділ доступний.</returns>
    public static bool IsAllowed(AppSection section, UserRole? role) =>
        role is { } actualRole
        && AllowedRoles.TryGetValue(section, out var roles)
        && roles.Contains(actualRole);

    /// <summary>
    /// Чи доступний ролі хоча б один розділ групи (тоді показується відповідне меню).
    /// </summary>
    /// <param name="sections">Розділи групи меню.</param>
    /// <param name="role">Роль користувача.</param>
    /// <returns><c>true</c>, якщо меню треба показати.</returns>
    public static bool IsAnyAllowed(IEnumerable<AppSection> sections, UserRole? role) =>
        sections.Any(section => IsAllowed(section, role));
}
