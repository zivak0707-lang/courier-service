using System.Diagnostics.CodeAnalysis;
using CourierService.Core.Entities;
using CourierService.Core.Enums;
using CourierService.Core.Exceptions;
using CourierService.Core.Security;
using CourierService.Core.Validation;

namespace CourierService.Core.Orders;

/// <summary>Дія над замовленням.</summary>
public enum OrderAction
{
    /// <summary>Призначити кур'єра (Нове → Призначено).</summary>
    Assign = 1,

    /// <summary>Зняти призначення (Призначено → Нове).</summary>
    Unassign = 2,

    /// <summary>Позначити оплату відправника карткою чи безготівково (статус не змінюється).</summary>
    ConfirmPayment = 3,

    /// <summary>Кур'єр забрав посилку (Призначено → Забрано).</summary>
    PickUp = 4,

    /// <summary>Кур'єр доставив посилку (Забрано → Доставлено).</summary>
    Deliver = 5,

    /// <summary>Невдала спроба доставки з причиною (Забрано → Невдала спроба).</summary>
    ReportFailure = 6,

    /// <summary>Повторна доставка (Невдала спроба → Призначено).</summary>
    RetryDelivery = 7,

    /// <summary>Повернути посилку відправнику (Невдала спроба → Повернено).</summary>
    Return = 8,

    /// <summary>Скасувати замовлення (Нове, Призначено → Скасовано).</summary>
    Cancel = 9,
}

/// <summary>
/// Рядок таблиці переходів: дія, статуси, з яких вона можлива, новий статус і роль виконавця.
/// </summary>
/// <param name="Action">Дія.</param>
/// <param name="From">Статуси, з яких дію можна виконати.</param>
/// <param name="To">Новий статус або <c>null</c>, якщо дія не змінює статусу.</param>
/// <param name="Role">Хто виконує дію.</param>
/// <param name="Title">Назва дії для кнопки.</param>
public sealed record OrderTransition(OrderAction Action, IReadOnlyList<OrderStatus> From, OrderStatus? To, UserRole Role, string Title);

/// <summary>
/// Дія, доступна користувачу для замовлення. Якщо <see cref="BlockedReason"/> заповнено,
/// кнопка показується неактивною з поясненням (наприклад, «відправник ще не оплатив»).
/// </summary>
/// <param name="Action">Дія.</param>
/// <param name="Title">Підпис кнопки.</param>
/// <param name="BlockedReason">Чому дію зараз виконати не можна, або <c>null</c>.</param>
public sealed record OrderActionOption(OrderAction Action, string Title, string? BlockedReason)
{
    /// <summary>Чи можна виконати дію зараз.</summary>
    public bool IsEnabled => BlockedReason is null;
}

/// <summary>
/// Правила життєвого циклу замовлення (рішення D-24): таблиця переходів, права за роллю,
/// «своє» замовлення кур'єра й умови оплати (D-09, D-26). Єдине джерело правил і для сервісу,
/// і для кнопок у картці замовлення.
/// </summary>
public interface IOrderWorkflow
{
    /// <summary>
    /// Дії, які користувач може бачити для замовлення (з поясненням, якщо дія тимчасово заблокована).
    /// </summary>
    /// <param name="order">Замовлення з оплатою.</param>
    /// <param name="user">Поточний користувач.</param>
    /// <returns>Дії в порядку таблиці переходів.</returns>
    IReadOnlyList<OrderActionOption> GetActions(Order order, UserSession user);

    /// <summary>
    /// Перевіряє й виконує дію: змінює статус (з записом в історію), оплату, кур'єра, виплату.
    /// </summary>
    /// <param name="order">Замовлення з оплатою (і кур'єром — для доставки).</param>
    /// <param name="action">Дія.</param>
    /// <param name="user">Хто виконує.</param>
    /// <param name="now">Час дії.</param>
    /// <param name="courier">Кур'єр для призначення або повторної доставки.</param>
    /// <param name="reason">Причина невдалої спроби.</param>
    /// <exception cref="AccessDeniedException">Роль не дозволяє дію або замовлення чуже.</exception>
    /// <exception cref="InvalidStatusTransitionException">Дія неможлива в поточному статусі.</exception>
    /// <exception cref="PaymentRequiredException">Бракує підтвердженої оплати.</exception>
    /// <exception cref="DomainValidationException">Не обрано кур'єра, кур'єр не підходить або немає причини.</exception>
    void Execute(Order order, OrderAction action, UserSession user, DateTime now, Courier? courier = null, string? reason = null);
}

/// <summary>
/// Реалізація <see cref="IOrderWorkflow"/> за таблицею з розділу 5 плану.
/// </summary>
public sealed class OrderWorkflow : IOrderWorkflow
{
    /// <summary>Мінімальна довжина причини невдалої спроби.</summary>
    public const int ReasonMinLength = 3;

    /// <summary>Максимальна довжина причини невдалої спроби.</summary>
    public const int ReasonMaxLength = 500;

    private static readonly OrderStatus[] Unfinished = [OrderStatus.New, OrderStatus.Assigned];

    /// <summary>Незавершені статуси: доставку ще не виконано й не скасовано, тож дедлайн має значення.</summary>
    public static IReadOnlyList<OrderStatus> OpenStatuses { get; } =
        [OrderStatus.New, OrderStatus.Assigned, OrderStatus.PickedUp, OrderStatus.DeliveryFailed];

    /// <summary>Статуси, у яких доставка «в роботі» в кур'єра (для навантаження й «Моїх доставок»).</summary>
    public static IReadOnlyList<OrderStatus> InProgressStatuses { get; } = [OrderStatus.Assigned, OrderStatus.PickedUp];

    /// <summary>Статуси, у яких посилка в кур'єра й рішення по ній ще не ухвалене (кур'єра не можна деактивувати).</summary>
    public static IReadOnlyList<OrderStatus> HeldByCourierStatuses { get; } =
        [OrderStatus.Assigned, OrderStatus.PickedUp, OrderStatus.DeliveryFailed];

    /// <summary>Таблиця переходів (рішення D-24) — одна на всю програму.</summary>
    public static IReadOnlyList<OrderTransition> Transitions { get; } =
    [
        new(OrderAction.Assign, [OrderStatus.New], OrderStatus.Assigned, UserRole.Operator, "Призначити кур'єра"),
        new(OrderAction.Unassign, [OrderStatus.Assigned], OrderStatus.New, UserRole.Operator, "Зняти призначення"),
        new(OrderAction.ConfirmPayment, Unfinished, null, UserRole.Operator, "Позначити оплату"),
        new(OrderAction.PickUp, [OrderStatus.Assigned], OrderStatus.PickedUp, UserRole.Courier, "Забрав посилку"),
        new(OrderAction.Deliver, [OrderStatus.PickedUp], OrderStatus.Delivered, UserRole.Courier, "Доставив"),
        new(OrderAction.ReportFailure, [OrderStatus.PickedUp], OrderStatus.DeliveryFailed, UserRole.Courier, "Невдала спроба"),
        new(OrderAction.RetryDelivery, [OrderStatus.DeliveryFailed], OrderStatus.Assigned, UserRole.Operator, "Повторна доставка"),
        new(OrderAction.Return, [OrderStatus.DeliveryFailed], OrderStatus.Returned, UserRole.Operator, "Повернути відправнику"),
        new(OrderAction.Cancel, Unfinished, OrderStatus.Cancelled, UserRole.Operator, "Скасувати замовлення"),
    ];

    /// <summary>
    /// Чи є в таблиці перехід між статусами (захист сутності від неможливого стану).
    /// </summary>
    /// <param name="from">Поточний статус.</param>
    /// <param name="to">Новий статус.</param>
    /// <returns><c>true</c>, якщо перехід дозволено.</returns>
    public static bool IsTransitionAllowed(OrderStatus from, OrderStatus to) =>
        Transitions.Any(t => t.To == to && t.From.Contains(from));

    /// <summary>
    /// Чи кінцевий статус (після нього дій немає).
    /// </summary>
    /// <param name="status">Статус.</param>
    /// <returns><c>true</c> для «Доставлено», «Скасовано», «Повернено».</returns>
    public static bool IsFinal(OrderStatus status) =>
        status is OrderStatus.Delivered or OrderStatus.Cancelled or OrderStatus.Returned;

    /// <inheritdoc />
    public IReadOnlyList<OrderActionOption> GetActions(Order order, UserSession user)
    {
        ArgumentNullException.ThrowIfNull(order);
        ArgumentNullException.ThrowIfNull(user);

        var actions = new List<OrderActionOption>();
        foreach (OrderTransition transition in Transitions)
        {
            if (transition.Role != user.Role || !transition.From.Contains(order.Status) || !IsOwnOrder(order, user))
            {
                continue;
            }

            // Позначка оплати має сенс лише для безготівкової оплати відправника, що ще очікує
            if (transition.Action == OrderAction.ConfirmPayment && !NeedsOperatorPayment(order))
            {
                continue;
            }

            actions.Add(new OrderActionOption(transition.Action, Title(transition, order), BlockedReason(transition.Action, order)));
        }

        return actions;
    }

    /// <inheritdoc />
    public void Execute(Order order, OrderAction action, UserSession user, DateTime now, Courier? courier = null, string? reason = null)
    {
        ArgumentNullException.ThrowIfNull(order);
        ArgumentNullException.ThrowIfNull(user);
        Payment payment = order.Payment ?? throw new InvalidOperationException("Оплату замовлення не завантажено.");
        OrderTransition transition = Transitions.Single(t => t.Action == action);

        if (transition.Role != user.Role)
        {
            throw new AccessDeniedException($"Дію «{transition.Title}» виконує {RoleName(transition.Role)}.");
        }

        if (!IsOwnOrder(order, user))
        {
            throw new AccessDeniedException("Це замовлення призначено іншому кур'єру.");
        }

        if (!transition.From.Contains(order.Status))
        {
            throw new InvalidStatusTransitionException(
                $"Дію «{transition.Title}» не можна виконати для замовлення в статусі «{order.Status.ToDisplayName()}».");
        }

        if (BlockedReason(action, order) is { } blocked)
        {
            throw new PaymentRequiredException(blocked);
        }

        switch (action)
        {
            case OrderAction.Assign:
            case OrderAction.RetryDelivery:
                EnsureCourierFits(order, courier);
                order.CourierId = courier.Id;
                order.Courier = courier;
                break;
            case OrderAction.Unassign:
                order.CourierId = null;
                order.Courier = null;
                break;
            case OrderAction.ConfirmPayment:
                if (!NeedsOperatorPayment(order))
                {
                    throw new InvalidStatusTransitionException("Оплату цього замовлення оператор не позначає: її приймає кур'єр або вона вже підтверджена.");
                }

                MarkPaid(payment, user, now);
                break;
            case OrderAction.PickUp:
                // Відправник розраховується до того, як віддає посилку (D-26): готівку приймає кур'єр
                if (payment.Payer == PaymentPayer.Sender && payment.Status == PaymentStatus.Pending)
                {
                    MarkPaid(payment, user, now);
                }

                break;
            case OrderAction.Deliver:
                // Одержувач розраховується, коли отримує посилку (D-09)
                if (payment.Status == PaymentStatus.Pending)
                {
                    MarkPaid(payment, user, now);
                }

                Courier deliveredBy = order.Courier ?? throw new InvalidOperationException("Кур'єра замовлення не завантажено.");
                order.DeliveredAt = now;
                order.CourierPayout = CourierPayout(order.Price, deliveredBy.PayoutPercent);
                break;
            case OrderAction.ReportFailure:
                reason = reason?.Trim();
                if (string.IsNullOrEmpty(reason) || reason.Length < ReasonMinLength || reason.Length > ReasonMaxLength)
                {
                    throw new DomainValidationException(
                        $"Вкажіть причину невдалої спроби ({ReasonMinLength}–{ReasonMaxLength} символів).", "Reason");
                }

                break;
            case OrderAction.Return:
                // Неотримана оплата одержувача вже не надійде; оплата відправника лишається (послугу надано)
                if (payment.Status == PaymentStatus.Pending)
                {
                    payment.Status = PaymentStatus.Cancelled;
                }

                break;
            case OrderAction.Cancel:
                payment.Status = payment.Status == PaymentStatus.Paid ? PaymentStatus.Refunded : PaymentStatus.Cancelled;
                break;
        }

        if (transition.To is { } newStatus)
        {
            order.ChangeStatus(newStatus, user.UserId, now, action == OrderAction.ReportFailure ? reason : null);
        }
    }

    /// <summary>
    /// Нарахування кур'єру: відсоток від вартості доставки (рішення D-15).
    /// </summary>
    /// <param name="price">Вартість доставки.</param>
    /// <param name="payoutPercent">Відсоток виплати кур'єра.</param>
    /// <returns>Сума нарахування.</returns>
    public static Money CourierPayout(Money price, int payoutPercent) => price.ApplyPercent(payoutPercent);

    private static bool IsOwnOrder(Order order, UserSession user) =>
        user.Role != UserRole.Courier || (order.CourierId is { } courierId && courierId == user.CourierId);

    private static bool NeedsOperatorPayment(Order order) => order.Payment is
    {
        Payer: PaymentPayer.Sender,
        Method: PaymentMethod.Card or PaymentMethod.BankTransfer,
        Status: PaymentStatus.Pending,
    };

    private static string? BlockedReason(OrderAction action, Order order)
    {
        Payment? payment = order.Payment;
        return action switch
        {
            OrderAction.PickUp when NeedsOperatorPayment(order) =>
                $"Відправник ще не оплатив доставку ({payment!.Method.ToDisplayName().ToLowerInvariant()}). "
                + "Оператор позначає оплату до того, як кур'єр забере посилку.",
            OrderAction.Deliver when payment is { Payer: PaymentPayer.Sender, Status: not PaymentStatus.Paid } =>
                "Оплату відправника не підтверджено.",
            _ => null,
        };
    }

    private static string Title(OrderTransition transition, Order order)
    {
        Payment? payment = order.Payment;
        bool collectsNow = payment?.Status == PaymentStatus.Pending && (
            (transition.Action == OrderAction.PickUp && payment.Payer == PaymentPayer.Sender && payment.Method == PaymentMethod.Cash)
            || (transition.Action == OrderAction.Deliver && payment.Payer == PaymentPayer.Recipient));

        // Кур'єр бачить, яку суму й як має отримати саме зараз
        return collectsNow
            ? $"{transition.Title}: отримано {payment!.Amount} ({payment.Method.ToDisplayName().ToLowerInvariant()})"
            : transition.Title;
    }

    private static void EnsureCourierFits(Order order, [NotNull] Courier? courier)
    {
        if (courier is null)
        {
            throw new DomainValidationException("Оберіть кур'єра.", "CourierId");
        }

        if (!courier.IsActive)
        {
            throw new DomainValidationException("Кур'єра деактивовано. Оберіть іншого.", "CourierId");
        }

        if (courier.MaxWeightGrams < order.ChargeableWeightGrams)
        {
            throw new DomainValidationException(
                $"Кур'єр перевозить до {InputParsing.FormatKilograms(courier.MaxWeightGrams)} кг, а розрахункова вага посилки — "
                + $"{InputParsing.FormatKilograms(order.ChargeableWeightGrams)} кг.", "CourierId");
        }
    }

    private static void MarkPaid(Payment payment, UserSession user, DateTime now)
    {
        payment.Status = PaymentStatus.Paid;
        payment.PaidAt = now;
        payment.ConfirmedByUserId = user.UserId;
    }

    private static string RoleName(UserRole role) => role switch
    {
        UserRole.Courier => "кур'єр",
        UserRole.Operator => "оператор",
        _ => "адміністратор",
    };
}
