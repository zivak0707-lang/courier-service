using CourierService.Core.Entities;
using CourierService.Core.Enums;
using CourierService.Core.Exceptions;
using CourierService.Core.Security;
using CourierService.Core.Validation;

namespace CourierService.Core.Orders;

/// <summary>
/// Виправлення оформленого замовлення (рішення D-40): лише поля, від яких не залежить ціна.
/// Зони, тариф, вага, габарити, цінність і оплата не змінюються — вони входять у знімок ціни (D-06).
/// </summary>
/// <param name="OrderId">Замовлення.</param>
/// <param name="RecipientName">Одержувач.</param>
/// <param name="RecipientPhone">Телефон одержувача в будь-якому допустимому форматі.</param>
/// <param name="PickupAddress">Звідки забрати.</param>
/// <param name="DeliveryAddress">Куди доставити.</param>
/// <param name="Comment">Коментар (необов'язково).</param>
public sealed record OrderDetailsEdit(
    int OrderId,
    string RecipientName,
    string RecipientPhone,
    string PickupAddress,
    string DeliveryAddress,
    string? Comment);

/// <summary>
/// Правила полів, які можна виправити (ті самі, що й під час оформлення).
/// </summary>
public sealed class OrderDetailsEditValidator : IValidator<OrderDetailsEdit>
{
    /// <inheritdoc />
    public ValidationErrors Validate(OrderDetailsEdit input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var errors = new ValidationErrors();
        errors.RequiredText(nameof(input.RecipientName), input.RecipientName, "Одержувач", OrderValidator.RecipientNameMaxLength);
        errors.Phone(nameof(input.RecipientPhone), input.RecipientPhone);
        errors.RequiredText(nameof(input.PickupAddress), input.PickupAddress, "Звідки забрати", Order.AddressMaxLength);
        errors.RequiredText(nameof(input.DeliveryAddress), input.DeliveryAddress, "Куди доставити", Order.AddressMaxLength);
        errors.OptionalText(nameof(input.Comment), input.Comment, "Коментар", OrderValidator.CommentMaxLength);
        return errors;
    }
}

/// <summary>
/// Застосування виправлень до замовлення із записом в історію.
/// </summary>
public static class OrderEditing
{
    /// <summary>Статуси, у яких дані ще можна виправити: кур'єр посилку ще не забрав.</summary>
    public static IReadOnlyList<OrderStatus> EditableStatuses { get; } = [OrderStatus.New, OrderStatus.Assigned];

    /// <summary>
    /// Чи може користувач виправити дані замовлення: лише оператор і лише до того, як посилку забрали.
    /// </summary>
    /// <param name="status">Поточний статус.</param>
    /// <param name="user">Користувач.</param>
    /// <returns><c>true</c>, якщо можна.</returns>
    public static bool CanEdit(OrderStatus status, UserSession user)
    {
        ArgumentNullException.ThrowIfNull(user);
        return user.Role == UserRole.Operator && EditableStatuses.Contains(status);
    }

    /// <summary>
    /// Застосовує виправлення й додає в історію запис «хто, коли, що змінено» (статус при цьому не змінюється).
    /// </summary>
    /// <param name="order">Замовлення.</param>
    /// <param name="edit">Перевірені дані (валідатор уже пройдено).</param>
    /// <param name="userId">Хто змінює.</param>
    /// <param name="now">Коли.</param>
    /// <returns><c>true</c>, якщо щось змінилося; <c>false</c>, якщо дані ті самі.</returns>
    /// <exception cref="InvalidStatusTransitionException">Посилку вже забрали або замовлення завершено.</exception>
    public static bool Apply(Order order, OrderDetailsEdit edit, int userId, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(order);
        ArgumentNullException.ThrowIfNull(edit);
        if (!EditableStatuses.Contains(order.Status))
        {
            throw new InvalidStatusTransitionException(
                $"Дані можна виправити лише в статусах «Нове» й «Призначено», а замовлення вже «{order.Status.ToDisplayName()}».");
        }

        PhoneNumber.TryNormalize(edit.RecipientPhone, out string phone);
        string name = edit.RecipientName.Trim();
        string pickup = edit.PickupAddress.Trim();
        string delivery = edit.DeliveryAddress.Trim();
        string? comment = string.IsNullOrWhiteSpace(edit.Comment) ? null : edit.Comment.Trim();

        List<string> changes = [];
        Compare(changes, "одержувач", order.RecipientName, name);
        Compare(changes, "телефон одержувача", PhoneNumber.Format(order.RecipientPhone), PhoneNumber.Format(phone));
        Compare(changes, "звідки забрати", order.PickupAddress, pickup);
        Compare(changes, "куди доставити", order.DeliveryAddress, delivery);
        if (order.Comment != comment)
        {
            changes.Add("коментар");
        }

        if (changes.Count == 0)
        {
            return false;
        }

        order.RecipientName = name;
        order.RecipientPhone = phone;
        order.PickupAddress = pickup;
        order.DeliveryAddress = delivery;
        order.Comment = comment;
        order.StatusChanges.Add(new OrderStatusChange
        {
            FromStatus = order.Status,
            ToStatus = order.Status,
            ChangedAt = now,
            ChangedByUserId = userId,
            Reason = Describe(changes),
        });
        return true;
    }

    /// <summary>
    /// Текст для історії: «Змінено: куди доставити «вул. А, 1» → «вул. Б, 2»; коментар»,
    /// не довший за поле причини в БД.
    /// </summary>
    /// <param name="changes">Описи окремих змін.</param>
    /// <returns>Текст.</returns>
    public static string Describe(IReadOnlyList<string> changes)
    {
        ArgumentNullException.ThrowIfNull(changes);
        string text = "Змінено: " + string.Join("; ", changes);
        return text.Length <= OrderWorkflow.ReasonMaxLength ? text : text[..(OrderWorkflow.ReasonMaxLength - 1)] + "…";
    }

    private static void Compare(List<string> changes, string label, string before, string after)
    {
        if (before != after)
        {
            changes.Add($"{label} «{before}» → «{after}»");
        }
    }
}
