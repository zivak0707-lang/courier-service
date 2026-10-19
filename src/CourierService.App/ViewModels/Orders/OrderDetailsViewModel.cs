using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CourierService.App.Services;
using CourierService.Core;
using CourierService.Core.Entities;
using CourierService.Core.Enums;
using CourierService.Core.Exceptions;
using CourierService.Core.Orders;
using CourierService.Core.Security;
using CourierService.Core.Sla;
using CourierService.Core.Validation;

namespace CourierService.App.ViewModels.Orders;

/// <summary>Запис історії статусів для показу.</summary>
/// <param name="ChangedAt">Коли.</param>
/// <param name="Change">«Нове → Призначено» або «Оформлено».</param>
/// <param name="ChangedBy">Хто.</param>
/// <param name="Reason">Причина (для невдалої спроби).</param>
public sealed record StatusHistoryItem(DateTime ChangedAt, string Change, string ChangedBy, string? Reason);

/// <summary>
/// Картка замовлення (рішення D-25): усі дані, розбивка вартості за знімком, оплата, історія статусів
/// і кнопки дій, дозволених поточному користувачу (їх визначає <see cref="IOrderWorkflow"/>, D-24).
/// </summary>
public sealed partial class OrderDetailsViewModel : ViewModelBase, ILoadable
{
    private static readonly CultureInfo Ukrainian = CultureInfo.GetCultureInfo("uk-UA");

    private readonly int _orderId;
    private readonly IOrderService _orders;
    private readonly IOrderActionService _actions;
    private readonly IOrderWorkflow _workflow;
    private readonly ICurrentUser _currentUser;
    private readonly IDialogService _dialogs;
    private readonly IEditDialogService _editDialogs;
    private readonly ISlaEvaluator _sla;
    private readonly IWaybillPrinter _waybills;
    private readonly IClipboardService _clipboard;

    /// <summary>
    /// Створює картку.
    /// </summary>
    /// <param name="orderId">Замовлення.</param>
    /// <param name="orders">Сервіс замовлень.</param>
    /// <param name="actions">Сервіс дій.</param>
    /// <param name="workflow">Правила переходів (які кнопки показати).</param>
    /// <param name="currentUser">Поточний користувач.</param>
    /// <param name="dialogs">Повідомлення й підтвердження.</param>
    /// <param name="editDialogs">Вікна вибору кур'єра й причини.</param>
    /// <param name="sla">Оцінка терміну доставки.</param>
    /// <param name="waybills">Перегляд і друк накладної (D-36).</param>
    /// <param name="clipboard">Буфер обміну (копіювання трек-номера).</param>
    public OrderDetailsViewModel(
        int orderId,
        IOrderService orders,
        IOrderActionService actions,
        IOrderWorkflow workflow,
        ICurrentUser currentUser,
        IDialogService dialogs,
        IEditDialogService editDialogs,
        ISlaEvaluator sla,
        IWaybillPrinter waybills,
        IClipboardService clipboard)
    {
        _orderId = orderId;
        _orders = orders;
        _actions = actions;
        _workflow = workflow;
        _currentUser = currentUser;
        _dialogs = dialogs;
        _editDialogs = editDialogs;
        _sla = sla;
        _waybills = waybills;
        _clipboard = clipboard;
    }

    /// <inheritdoc />
    public override string Title => Order is null ? "Замовлення" : $"Замовлення {Order.TrackingNumber}";

    /// <summary>Чи змінено замовлення (список, з якого відкрито картку, треба оновити).</summary>
    public bool HasChanges { get; private set; }

    /// <summary>Замовлення з пов'язаними даними (лише для читання).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title), nameof(FeaturesText), nameof(PaymentDetails), nameof(DatesText), nameof(CanPrintWaybill))]
    [NotifyCanExecuteChangedFor(nameof(PrintWaybillCommand), nameof(CopyTrackingNumberCommand))]
    public partial Order? Order { get; private set; }

    /// <summary>Хто й коли оформив, дедлайн і (якщо є) час доставки.</summary>
    public string DatesText => Order is not { } order
        ? string.Empty
        : $"Оформлено {order.CreatedAt.ToString("dd.MM.yyyy HH:mm", Ukrainian)} ({order.CreatedBy?.FullName})"
          + $" · доставити до {order.Deadline.ToString("dd.MM.yyyy HH:mm", Ukrainian)}"
          + (order.DeliveredAt is { } delivered ? $" · доставлено {delivered.ToString("dd.MM.yyyy HH:mm", Ukrainian)}" : string.Empty);

    /// <summary>Оголошена цінність і крихкість: «цінність 500,00 грн, крихка», «крихка» або «—», якщо нічого немає.</summary>
    public string FeaturesText
    {
        get
        {
            if (Order is not { } order)
            {
                return string.Empty;
            }

            List<string> features = [];
            if (order.DeclaredValue > Money.Zero)
            {
                features.Add($"цінність {order.DeclaredValue}");
            }

            if (order.IsFragile)
            {
                features.Add("крихка");
            }

            return features.Count == 0 ? "—" : string.Join(", ", features);
        }
    }

    /// <summary>Коли й хто підтвердив оплату: «(19.10.2026 16:35, Ігор Бондар)».</summary>
    public string PaymentDetails => Order?.Payment is { PaidAt: { } paidAt } payment
        ? $"({paidAt.ToString("dd.MM.yyyy HH:mm", Ukrainian)}, {payment.ConfirmedBy?.FullName})"
        : string.Empty;

    /// <summary>Дотримання терміну доставки (D-31).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSlaTracked))]
    public partial SlaResult Sla { get; private set; } = SlaResult.NotTracked;

    /// <summary>Чи показувати позначку терміну: для скасованих і повернених вона була б порожньою.</summary>
    public bool IsSlaTracked => Sla.State != SlaState.NotTracked;

    /// <summary>Розбивка вартості за знімком у замовленні.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<PriceLine> PriceLines { get; private set; } = [];

    /// <summary>Історія статусів, найновіші першими.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<StatusHistoryItem> History { get; private set; } = [];

    /// <summary>Дії, доступні поточному користувачу.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActions))]
    public partial IReadOnlyList<OrderActionOption> Actions { get; private set; } = [];

    /// <summary>
    /// Чи показувати кнопку «Друк накладної»: накладну друкує адміністратор чи оператор, для скасованого замовлення — ні.
    /// </summary>
    public bool CanPrintWaybill => Order is { } order && Waybill.CanPrint(order)
        && _currentUser.Session?.Role is UserRole.Admin or UserRole.Operator;

    /// <summary>Чи є доступні дії (інакше панель дій прихована).</summary>
    public bool HasActions => Actions.Count > 0;

    /// <summary>Чи триває запит.</summary>
    [ObservableProperty]
    public partial bool IsBusy { get; private set; }

    /// <summary>
    /// Розбивка вартості зі знімка замовлення (рішення D-06): базова ціна завжди, інші — лише ненульові.
    /// </summary>
    /// <param name="order">Замовлення.</param>
    /// <returns>Рядки розбивки.</returns>
    public static IReadOnlyList<PriceLine> PriceLinesFor(Order order)
    {
        ArgumentNullException.ThrowIfNull(order);
        string coefficient = order.ZoneCoefficientPercent == 100
            ? string.Empty
            : " × " + InputParsing.FormatCoefficient(order.ZoneCoefficientPercent);

        var lines = new List<PriceLine> { new($"Базова ціна{coefficient}", order.BaseCost.ToString()) };
        if (order.WeightCost.Kopecks > 0)
        {
            lines.Add(new($"Додаткова вага (розрахункова {InputParsing.FormatKilograms(order.ChargeableWeightGrams)} кг){coefficient}",
                order.WeightCost.ToString()));
        }

        if (order.CrossRiverCost.Kopecks > 0)
        {
            lines.Add(new("Переїзд через Дніпро", order.CrossRiverCost.ToString()));
        }

        if (order.InsuranceCost.Kopecks > 0)
        {
            lines.Add(new($"Страхування (цінність {order.DeclaredValue})", order.InsuranceCost.ToString()));
        }

        if (order.FragileCost.Kopecks > 0)
        {
            lines.Add(new("Крихка посилка", order.FragileCost.ToString()));
        }

        return lines;
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsBusy = true;
        try
        {
            Order order = await _orders.GetOrderAsync(_orderId);
            Order = order;
            Sla = _sla.Evaluate(order.Status, order.CreatedAt, order.Deadline, order.DeliveredAt);
            PriceLines = PriceLinesFor(order);
            History = order.StatusChanges
                .OrderByDescending(c => c.ChangedAt)
                .ThenByDescending(c => c.Id)
                .Select(c => new StatusHistoryItem(
                    c.ChangedAt,
                    c.FromStatus is { } from ? $"{from.ToDisplayName()} → {c.ToStatus.ToDisplayName()}" : "Оформлено",
                    c.ChangedBy?.FullName ?? string.Empty,
                    c.Reason))
                .ToList();
            Actions = _currentUser.Session is { } session ? _workflow.GetActions(order, session) : [];
        }
        catch (DomainException ex)
        {
            _dialogs.ShowError(ex.UserMessage);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Чи щойно скопійовано трек-номер (показується позначка «Скопійовано»).</summary>
    [ObservableProperty]
    public partial bool IsTrackingNumberCopied { get; private set; }

    private bool HasOrder() => Order is not null;

    [RelayCommand(CanExecute = nameof(HasOrder))]
    private void CopyTrackingNumber() =>
        IsTrackingNumberCopied = TrackingNumberCopy.Copy(_clipboard, _dialogs, Order?.TrackingNumber);

    [RelayCommand(CanExecute = nameof(CanPrintWaybill))]
    private void PrintWaybill()
    {
        if (Order is { } order)
        {
            _waybills.ShowPreview(Waybill.From(order));
        }
    }

    [RelayCommand]
    private async Task ExecuteActionAsync(OrderActionOption? option)
    {
        if (option is null || Order is not { } order)
        {
            return;
        }

        if (!option.IsEnabled)
        {
            _dialogs.ShowWarning(option.BlockedReason!);
            return;
        }

        try
        {
            bool done = option.Action switch
            {
                OrderAction.Assign or OrderAction.RetryDelivery => await ChooseCourierAsync(order, option.Action),
                OrderAction.ReportFailure => _editDialogs.ShowEditor(new FailureReasonViewModel(_actions, order.Id, order.TrackingNumber)),
                _ => await ConfirmAndExecuteAsync(order, option),
            };

            if (done)
            {
                HasChanges = true;
                await LoadAsync();
            }
        }
        catch (DomainException ex)
        {
            // Наприклад, замовлення вже змінив інший користувач — показуємо причину й оновлюємо картку
            _dialogs.ShowWarning(ex.UserMessage);
            await LoadAsync();
        }
    }

    private async Task<bool> ChooseCourierAsync(Order order, OrderAction action)
    {
        AssignmentOptions options = await _actions.GetAssignmentOptionsAsync(order.Id);
        return _editDialogs.ShowEditor(new AssignCourierViewModel(_actions, order.Id, action, order.TrackingNumber, options));
    }

    private async Task<bool> ConfirmAndExecuteAsync(Order order, OrderActionOption option)
    {
        if (!_dialogs.Confirm(ConfirmationText(order, option)))
        {
            return false;
        }

        await _actions.ExecuteAsync(order.Id, option.Action);
        return true;
    }

    /// <summary>
    /// Питання підтвердження: що саме станеться (сума, повернення коштів).
    /// </summary>
    /// <param name="order">Замовлення.</param>
    /// <param name="option">Дія.</param>
    /// <returns>Текст запиту.</returns>
    public static string ConfirmationText(Order order, OrderActionOption option)
    {
        ArgumentNullException.ThrowIfNull(order);
        ArgumentNullException.ThrowIfNull(option);
        Payment? payment = order.Payment;
        string number = order.TrackingNumber;
        return option.Action switch
        {
            OrderAction.Cancel when payment?.Status == PaymentStatus.Paid =>
                $"Скасувати замовлення {number}?\n\nОплату {payment.Amount} буде позначено як повернену відправнику.",
            OrderAction.Cancel => $"Скасувати замовлення {number}?",
            OrderAction.Return => $"Повернути посилку {number} відправнику?\n\nЗамовлення буде завершено.",
            OrderAction.Unassign => $"Зняти кур'єра із замовлення {number}?",
            OrderAction.ConfirmPayment =>
                $"Позначити, що відправник оплатив {payment?.Amount} ({payment?.Method.ToDisplayName().ToLower(Ukrainian)})?",
            _ => $"{option.Title}?",
        };
    }
}
