namespace CourierService.Core.Enums;

/// <summary>Роль користувача (рішення D-08).</summary>
public enum UserRole
{
    /// <summary>Адміністратор: користувачі, зони, тарифи, усі звіти.</summary>
    Admin = 1,

    /// <summary>Оператор (диспетчер): клієнти, кур'єри, замовлення, оплати.</summary>
    Operator = 2,

    /// <summary>Кур'єр: лише власні доставки.</summary>
    Courier = 3,
}

/// <summary>Вид транспорту кур'єра.</summary>
public enum TransportType
{
    /// <summary>Пішки.</summary>
    Foot = 1,

    /// <summary>Велосипед або самокат.</summary>
    Bicycle = 2,

    /// <summary>Автомобіль.</summary>
    Car = 3,
}

/// <summary>Тип клієнта-відправника.</summary>
public enum ClientType
{
    /// <summary>Фізична особа.</summary>
    Individual = 1,

    /// <summary>Компанія або ФОП.</summary>
    Company = 2,
}

/// <summary>Берег Дніпра, на якому розташована зона (рішення D-07).</summary>
public enum RiverBank
{
    /// <summary>Правий берег.</summary>
    Right = 1,

    /// <summary>Лівий берег.</summary>
    Left = 2,
}

/// <summary>Статус замовлення (docs/plan.md, розділ 5).</summary>
public enum OrderStatus
{
    /// <summary>Нове, кур'єра ще не призначено.</summary>
    New = 1,

    /// <summary>Кур'єра призначено.</summary>
    Assigned = 2,

    /// <summary>Кур'єр забрав посилку.</summary>
    PickedUp = 3,

    /// <summary>Доставлено одержувачу (кінцевий статус).</summary>
    Delivered = 4,

    /// <summary>Невдала спроба доставки.</summary>
    DeliveryFailed = 5,

    /// <summary>Повернено відправнику (кінцевий статус).</summary>
    Returned = 6,

    /// <summary>Скасовано (кінцевий статус).</summary>
    Cancelled = 7,
}

/// <summary>Хто оплачує доставку (рішення D-09).</summary>
public enum PaymentPayer
{
    /// <summary>Відправник.</summary>
    Sender = 1,

    /// <summary>Одержувач — накладений платіж під час доставки.</summary>
    Recipient = 2,
}

/// <summary>Спосіб оплати.</summary>
public enum PaymentMethod
{
    /// <summary>Готівка.</summary>
    Cash = 1,

    /// <summary>Банківська картка.</summary>
    Card = 2,

    /// <summary>Безготівковий розрахунок (лише для відправника).</summary>
    BankTransfer = 3,
}

/// <summary>Стан оплати.</summary>
public enum PaymentStatus
{
    /// <summary>Очікує оплати.</summary>
    Pending = 1,

    /// <summary>Оплачено.</summary>
    Paid = 2,

    /// <summary>Скасовано разом із неоплаченим замовленням.</summary>
    Cancelled = 3,

    /// <summary>Гроші повернено після скасування оплаченого замовлення.</summary>
    Refunded = 4,
}
