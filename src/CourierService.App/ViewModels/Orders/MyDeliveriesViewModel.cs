using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CourierService.App.Services;
using CourierService.Core.Exceptions;
using CourierService.Core.Orders;

namespace CourierService.App.ViewModels.Orders;

/// <summary>
/// Доставки поточного кур'єра. Нащадки визначають лише, які доставки показувати й заголовок;
/// дії над доставкою виконуються в картці замовлення (рішення D-25).
/// </summary>
public abstract partial class MyDeliveriesViewModelBase : ViewModelBase, ILoadable
{
    private readonly IOrderService _orders;
    private readonly IOrderDetailsDialog _orderDetails;
    private readonly IDialogService _dialogs;

    /// <summary>
    /// Створює сторінку.
    /// </summary>
    /// <param name="orders">Сервіс замовлень.</param>
    /// <param name="orderDetails">Картка замовлення.</param>
    /// <param name="dialogs">Повідомлення.</param>
    protected MyDeliveriesViewModelBase(IOrderService orders, IOrderDetailsDialog orderDetails, IDialogService dialogs)
    {
        _orders = orders;
        _orderDetails = orderDetails;
        _dialogs = dialogs;
    }

    /// <summary>Активні (призначені й забрані) чи решта (історія).</summary>
    protected abstract bool ShowActive { get; }

    /// <summary>Підказка, коли список порожній.</summary>
    public abstract string EmptyText { get; }

    /// <summary>Доставки.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty), nameof(CountText))]
    public partial IReadOnlyList<OrderListItem> Items { get; private set; } = [];

    /// <summary>Виділена доставка.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenOrderCommand))]
    public partial OrderListItem? SelectedOrder { get; set; }

    /// <summary>Чи список порожній (показується підказка).</summary>
    public bool IsEmpty => Items.Count == 0;

    /// <summary>Кількість доставок.</summary>
    public string CountText => $"Доставок: {Items.Count}";

    [RelayCommand]
    private async Task LoadAsync()
    {
        try
        {
            Items = await _orders.GetMyDeliveriesAsync(ShowActive);
        }
        catch (DomainException ex)
        {
            _dialogs.ShowError(ex.UserMessage);
        }
    }

    private bool CanOpenOrder() => SelectedOrder is not null;

    [RelayCommand(CanExecute = nameof(CanOpenOrder))]
    private async Task OpenOrderAsync()
    {
        // Після «Забрав» чи «Доставив» список оновлюється: доставка переходить в історію
        if (SelectedOrder is { } order && _orderDetails.ShowOrder(order.Id))
        {
            await LoadAsync();
        }
    }
}

/// <summary>
/// «Мої доставки → Активні»: призначені й забрані посилки, найтерміновіші першими.
/// </summary>
/// <param name="orders">Сервіс замовлень.</param>
/// <param name="orderDetails">Картка замовлення.</param>
/// <param name="dialogs">Повідомлення.</param>
public sealed class MyActiveDeliveriesViewModel(IOrderService orders, IOrderDetailsDialog orderDetails, IDialogService dialogs)
    : MyDeliveriesViewModelBase(orders, orderDetails, dialogs)
{
    /// <inheritdoc />
    public override string Title => "Мої доставки: активні";

    /// <inheritdoc />
    public override string EmptyText => "Активних доставок немає. Нові з'являться, щойно оператор призначить їх вам.";

    /// <inheritdoc />
    protected override bool ShowActive => true;
}

/// <summary>
/// «Мої доставки → Виконані»: доставлені, невдалі, повернені — історія, новіші першими.
/// </summary>
/// <param name="orders">Сервіс замовлень.</param>
/// <param name="orderDetails">Картка замовлення.</param>
/// <param name="dialogs">Повідомлення.</param>
public sealed class MyCompletedDeliveriesViewModel(IOrderService orders, IOrderDetailsDialog orderDetails, IDialogService dialogs)
    : MyDeliveriesViewModelBase(orders, orderDetails, dialogs)
{
    /// <inheritdoc />
    public override string Title => "Мої доставки: виконані";

    /// <inheritdoc />
    public override string EmptyText => "Виконаних доставок ще немає.";

    /// <inheritdoc />
    protected override bool ShowActive => false;
}
