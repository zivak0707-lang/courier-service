using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CourierService.App.Services;
using CourierService.Core.Exceptions;
using CourierService.Core.Orders;

namespace CourierService.App.ViewModels.Orders;

/// <summary>
/// Швидкий пошук одного замовлення за трек-номером (наприклад, коли клієнт телефонує).
/// Номер з опискою не знаходить чуже замовлення: контрольна цифра дає підказку.
/// </summary>
public sealed partial class TrackingSearchViewModel : ViewModelBase
{
    private readonly IOrderService _orders;
    private readonly IDialogService _dialogs;
    private readonly IOrderDetailsDialog _orderDetails;
    private readonly IClipboardService _clipboard;

    /// <summary>
    /// Створює сторінку.
    /// </summary>
    /// <param name="orders">Сервіс замовлень.</param>
    /// <param name="dialogs">Повідомлення.</param>
    /// <param name="orderDetails">Картка замовлення.</param>
    /// <param name="clipboard">Буфер обміну (копіювання трек-номера).</param>
    public TrackingSearchViewModel(IOrderService orders, IDialogService dialogs, IOrderDetailsDialog orderDetails, IClipboardService clipboard)
    {
        _orders = orders;
        _dialogs = dialogs;
        _orderDetails = orderDetails;
        _clipboard = clipboard;
    }

    /// <inheritdoc />
    public override string Title => "Пошук за трек-номером";

    /// <summary>Введений трек-номер.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SearchCommand))]
    public partial string TrackingNumber { get; set; } = string.Empty;

    /// <summary>Знайдене замовлення.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenOrderCommand), nameof(CopyTrackingNumberCommand))]
    public partial OrderListItem? Result { get; private set; }

    /// <summary>Пояснення, якщо замовлення не знайдено або номер введено з помилкою.</summary>
    [ObservableProperty]
    public partial string? Notice { get; private set; }

    /// <summary>Чи щойно скопійовано трек-номер знайденого замовлення (показується позначка «Скопійовано»).</summary>
    [ObservableProperty]
    public partial bool IsTrackingNumberCopied { get; private set; }

    partial void OnResultChanged(OrderListItem? value) => IsTrackingNumberCopied = false;

    [RelayCommand(CanExecute = nameof(CanOpenOrder))]
    private void CopyTrackingNumber() =>
        IsTrackingNumberCopied = TrackingNumberCopy.Copy(_clipboard, _dialogs, Result?.TrackingNumber);

    private bool CanSearch() => !string.IsNullOrWhiteSpace(TrackingNumber);

    private bool CanOpenOrder() => Result is not null;

    [RelayCommand(CanExecute = nameof(CanOpenOrder))]
    private async Task OpenOrderAsync()
    {
        // Після дії в картці показуємо актуальний статус
        if (Result is { } order && _orderDetails.ShowOrder(order.Id))
        {
            await SearchAsync();
        }
    }

    [RelayCommand(CanExecute = nameof(CanSearch))]
    private async Task SearchAsync()
    {
        Result = null;
        Notice = null;

        OrderQuery query = OrderQuery.Parse(TrackingNumber);
        if (query.Kind != OrderQueryKind.TrackingNumber)
        {
            // Тут шукається лише трек-номер; телефон — у списку замовлень
            Notice = query.Kind == OrderQueryKind.Invalid
                ? query.Message
                : "Введіть трек-номер, наприклад KV2610190042-6. Пошук за телефоном — у «Списку замовлень».";
            return;
        }

        try
        {
            OrderSearchResult result = await _orders.SearchAsync(new OrderFilter(Query: query.Value));
            Result = result.Items.FirstOrDefault();
            Notice = result.Notice;
        }
        catch (DomainException ex)
        {
            _dialogs.ShowError(ex.UserMessage);
        }
    }
}
