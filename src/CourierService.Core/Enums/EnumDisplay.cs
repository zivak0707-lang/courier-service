namespace CourierService.Core.Enums;

/// <summary>
/// Назви значень перелічень українською для показу в інтерфейсі.
/// </summary>
public static class EnumDisplay
{
    /// <summary>Назва ролі.</summary>
    /// <param name="role">Роль користувача.</param>
    /// <returns>Назва українською.</returns>
    public static string ToDisplayName(this UserRole role) => role switch
    {
        UserRole.Admin => "Адміністратор",
        UserRole.Operator => "Оператор",
        UserRole.Courier => "Кур'єр",
        _ => role.ToString(),
    };

    /// <summary>Назва виду транспорту.</summary>
    /// <param name="transport">Вид транспорту.</param>
    /// <returns>Назва українською.</returns>
    public static string ToDisplayName(this TransportType transport) => transport switch
    {
        TransportType.Foot => "Пішки",
        TransportType.Bicycle => "Велосипед",
        TransportType.Car => "Автомобіль",
        _ => transport.ToString(),
    };

    /// <summary>Назва типу клієнта.</summary>
    /// <param name="clientType">Тип клієнта.</param>
    /// <returns>Назва українською.</returns>
    public static string ToDisplayName(this ClientType clientType) => clientType switch
    {
        ClientType.Individual => "Фізична особа",
        ClientType.Company => "Компанія / ФОП",
        _ => clientType.ToString(),
    };

    /// <summary>Назва берега Дніпра.</summary>
    /// <param name="bank">Берег.</param>
    /// <returns>Назва українською.</returns>
    public static string ToDisplayName(this RiverBank bank) => bank switch
    {
        RiverBank.Right => "Правий",
        RiverBank.Left => "Лівий",
        _ => bank.ToString(),
    };

    /// <summary>Назва статусу замовлення.</summary>
    /// <param name="status">Статус замовлення.</param>
    /// <returns>Назва українською.</returns>
    public static string ToDisplayName(this OrderStatus status) => status switch
    {
        OrderStatus.New => "Нове",
        OrderStatus.Assigned => "Призначено",
        OrderStatus.PickedUp => "Забрано",
        OrderStatus.Delivered => "Доставлено",
        OrderStatus.DeliveryFailed => "Невдала спроба",
        OrderStatus.Returned => "Повернено",
        OrderStatus.Cancelled => "Скасовано",
        _ => status.ToString(),
    };
}
