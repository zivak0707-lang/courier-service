using CommunityToolkit.Mvvm.ComponentModel;
using CourierService.App.ViewModels.Directories;
using CourierService.Core.Entities;
using CourierService.Core.Orders;
using CourierService.Core.Validation;

namespace CourierService.App.ViewModels.Orders;

/// <summary>
/// Виправлення оформленого замовлення (рішення D-40): контакти одержувача, адреси й коментар.
/// Райони показуються лише як підказка — їх змінити не можна, бо від них залежить ціна.
/// </summary>
public sealed partial class OrderDetailsEditorViewModel : EditViewModelBase
{
    private static readonly OrderDetailsEditValidator Validator = new();
    private readonly IOrderService _orders;
    private readonly int _orderId;
    private OrderDetailsEdit? _validInput;

    /// <summary>
    /// Створює форму з поточними даними замовлення.
    /// </summary>
    /// <param name="orders">Сервіс замовлень.</param>
    /// <param name="order">Замовлення з районами.</param>
    public OrderDetailsEditorViewModel(IOrderService orders, Order order)
    {
        ArgumentNullException.ThrowIfNull(order);
        _orders = orders;
        _orderId = order.Id;
        TrackingNumber = order.TrackingNumber;
        PickupZoneHint = ZoneHint(order.PickupZone?.Name);
        DeliveryZoneHint = ZoneHint(order.DeliveryZone?.Name);
        RecipientName = order.RecipientName;
        RecipientPhone = PhoneNumber.Format(order.RecipientPhone);
        PickupAddress = order.PickupAddress;
        DeliveryAddress = order.DeliveryAddress;
        Comment = order.Comment ?? string.Empty;
    }

    /// <inheritdoc />
    public override string Title => "Виправлення даних замовлення";

    /// <summary>Трек-номер замовлення.</summary>
    public string TrackingNumber { get; }

    /// <summary>У якому районі має бути адреса, звідки забрати.</summary>
    public string PickupZoneHint { get; }

    /// <summary>У якому районі має бути адреса, куди доставити.</summary>
    public string DeliveryZoneHint { get; }

    /// <summary>ПІБ одержувача.</summary>
    [ObservableProperty]
    public partial string RecipientName { get; set; }

    /// <summary>Телефон одержувача.</summary>
    [ObservableProperty]
    public partial string RecipientPhone { get; set; }

    /// <summary>Звідки забрати.</summary>
    [ObservableProperty]
    public partial string PickupAddress { get; set; }

    /// <summary>Куди доставити.</summary>
    [ObservableProperty]
    public partial string DeliveryAddress { get; set; }

    /// <summary>Коментар.</summary>
    [ObservableProperty]
    public partial string Comment { get; set; }

    /// <summary>Чи змінилися дані після збереження (<c>false</c> — нічого не змінено).</summary>
    public bool HasChanged { get; private set; }

    /// <inheritdoc />
    protected override ValidationErrors Validate()
    {
        var input = new OrderDetailsEdit(_orderId, RecipientName, RecipientPhone, PickupAddress, DeliveryAddress, Comment);
        ValidationErrors errors = Validator.Validate(input);
        _validInput = errors.IsValid ? input : null;
        return errors;
    }

    /// <inheritdoc />
    protected override async Task SaveCoreAsync() =>
        HasChanged = await _orders.UpdateDetailsAsync(_validInput ?? throw new InvalidOperationException("Дані форми не перевірено."));

    // Програма не має довідника вулиць, тому відповідність адреси району перевіряє оператор (обмеження D-40)
    private static string ZoneHint(string? zone) =>
        $"Адреса має бути в районі «{zone}»: район змінити не можна, від нього залежить ціна.";
}
