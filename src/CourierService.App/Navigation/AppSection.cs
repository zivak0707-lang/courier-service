namespace CourierService.App.Navigation;

/// <summary>
/// Розділи застосунку, що відкриваються з головного меню.
/// </summary>
public enum AppSection
{
    /// <summary>Оформлення нового замовлення.</summary>
    NewOrder,

    /// <summary>Список замовлень із фільтрами.</summary>
    OrderList,

    /// <summary>Пошук замовлення за трек-номером.</summary>
    TrackingSearch,

    /// <summary>Активні доставки поточного кур'єра.</summary>
    MyActiveDeliveries,

    /// <summary>Виконані доставки поточного кур'єра.</summary>
    MyCompletedDeliveries,

    /// <summary>Довідник клієнтів.</summary>
    Clients,

    /// <summary>Довідник кур'єрів.</summary>
    Couriers,

    /// <summary>Довідник зон доставки (районів Києва).</summary>
    Zones,

    /// <summary>Довідник тарифів.</summary>
    Tariffs,

    /// <summary>Дашборд із графіками й показниками.</summary>
    Dashboard,

    /// <summary>Звіт про виручку за оплаченими замовленнями.</summary>
    RevenueReport,

    /// <summary>Звіт про неоплачені замовлення.</summary>
    UnpaidReport,

    /// <summary>Звіт про ефективність кур'єрів.</summary>
    CourierPerformanceReport,

    /// <summary>Звіт про нарахування кур'єрам і валовий дохід.</summary>
    CourierPayoutReport,

    /// <summary>Керування користувачами.</summary>
    Users,
}
