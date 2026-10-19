using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CourierService.App.ViewModels.Directories;
using CourierService.Core.Assignment;
using CourierService.Core.Orders;
using CourierService.Core.Validation;

namespace CourierService.App.ViewModels.Orders;

/// <summary>
/// Вибір кур'єра для призначення або повторної доставки. Типово обрано кандидата
/// автопризначення (рішення D-27); оператор може обрати іншого.
/// </summary>
/// <remarks>
/// Форма використовує спільне вікно редагування: помилку сервісу («кур'єр перевозить до 10 кг»)
/// показує біля поля вибору.
/// </remarks>
public sealed partial class AssignCourierViewModel : EditViewModelBase
{
    private readonly IOrderActionService _actions;
    private readonly int _orderId;
    private readonly OrderAction _action;

    /// <summary>
    /// Створює форму.
    /// </summary>
    /// <param name="actions">Сервіс дій.</param>
    /// <param name="orderId">Замовлення.</param>
    /// <param name="action">Призначення або повторна доставка.</param>
    /// <param name="trackingNumber">Трек-номер для заголовка.</param>
    /// <param name="options">Кур'єри й пропозиція алгоритму.</param>
    public AssignCourierViewModel(
        IOrderActionService actions, int orderId, OrderAction action, string trackingNumber, AssignmentOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _actions = actions;
        _orderId = orderId;
        _action = action;
        TrackingNumber = trackingNumber;
        Suggestion = options.Suggestion.Courier is null
            ? $"Автопризначення: {options.Suggestion.Explanation}"
            : $"Пропозиція: {options.Suggestion.Explanation}";
        Couriers = options.Couriers
            .Where(c => c.IsActive)
            .Select(c => new Option<int>(c.CourierId, Describe(c)))
            .ToList();
        CourierId = options.Suggestion.Courier?.CourierId ?? 0;
    }

    /// <inheritdoc />
    public override string Title => _action == OrderAction.RetryDelivery ? "Повторна доставка" : "Призначення кур'єра";

    /// <inheritdoc />
    public override double DialogWidth => 680;

    /// <summary>Трек-номер замовлення.</summary>
    public string TrackingNumber { get; }

    /// <summary>Пояснення автопризначення.</summary>
    public string Suggestion { get; }

    /// <summary>Активні кур'єри з вантажопідйомністю й навантаженням.</summary>
    public IReadOnlyList<Option<int>> Couriers { get; }

    /// <summary>Обраний кур'єр.</summary>
    [ObservableProperty]
    public partial int CourierId { get; set; }

    /// <inheritdoc />
    protected override ValidationErrors Validate()
    {
        var errors = new ValidationErrors();
        if (CourierId <= 0)
        {
            errors.Add(nameof(CourierId), "Оберіть кур'єра.");
        }

        return errors;
    }

    /// <inheritdoc />
    protected override Task SaveCoreAsync() => _actions.ExecuteAsync(_orderId, _action, CourierId);

    private static string Describe(CourierCandidate courier)
    {
        string load = CourierAssignment.ActiveDeliveriesText(courier.ActiveDeliveries);
        string warning = courier.ActiveDeliveries >= CourierAssignment.MaxActiveDeliveries ? " — перевантажений" : string.Empty;
        return $"{courier.FullName} · {courier.HomeZoneName} · до {InputParsing.FormatKilograms(courier.MaxWeightGrams)} кг · {load}{warning}";
    }
}

/// <summary>
/// Причина невдалої спроби доставки (обов'язкова, D-24).
/// </summary>
public sealed partial class FailureReasonViewModel : EditViewModelBase
{
    private readonly IOrderActionService _actions;
    private readonly int _orderId;

    /// <summary>
    /// Створює форму.
    /// </summary>
    /// <param name="actions">Сервіс дій.</param>
    /// <param name="orderId">Замовлення.</param>
    /// <param name="trackingNumber">Трек-номер для заголовка.</param>
    public FailureReasonViewModel(IOrderActionService actions, int orderId, string trackingNumber)
    {
        _actions = actions;
        _orderId = orderId;
        TrackingNumber = trackingNumber;
    }

    /// <inheritdoc />
    public override string Title => "Невдала спроба доставки";

    /// <summary>Трек-номер замовлення.</summary>
    public string TrackingNumber { get; }

    /// <summary>Типові причини — кнопки швидкого вибору.</summary>
    public IReadOnlyList<string> TypicalReasons { get; } =
    [
        "Одержувач не відповідає на дзвінки",
        "Одержувача немає за адресою",
        "Одержувач відмовився від посилки",
        "Неправильна адреса",
    ];

    /// <summary>Причина.</summary>
    [ObservableProperty]
    public partial string Reason { get; set; } = string.Empty;

    /// <inheritdoc />
    protected override ValidationErrors Validate()
    {
        var errors = new ValidationErrors();
        int length = Reason.Trim().Length;
        if (length < OrderWorkflow.ReasonMinLength || length > OrderWorkflow.ReasonMaxLength)
        {
            errors.Add(nameof(Reason),
                $"Вкажіть причину ({OrderWorkflow.ReasonMinLength}–{OrderWorkflow.ReasonMaxLength} символів).");
        }

        return errors;
    }

    /// <inheritdoc />
    protected override Task SaveCoreAsync() => _actions.ExecuteAsync(_orderId, OrderAction.ReportFailure, reason: Reason);

    [RelayCommand]
    private void UseReason(string reason) => Reason = reason;
}
