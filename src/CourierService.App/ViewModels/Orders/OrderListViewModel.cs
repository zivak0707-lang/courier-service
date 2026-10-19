using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CourierService.App.Services;
using CourierService.App.ViewModels.Directories;
using CourierService.Core.Abstractions;
using CourierService.Core.Directories;
using CourierService.Core.Enums;
using CourierService.Core.Exceptions;
using CourierService.Core.Orders;
using CourierService.Core.Security;

namespace CourierService.App.ViewModels.Orders;

/// <summary>
/// Список замовлень: фільтри виконуються в БД (рішення D-20), сортування — кліком по колонці.
/// Оператор оформлює нові замовлення, адміністратор лише переглядає.
/// </summary>
/// <remarks>
/// Фільтри зі списків і дат застосовуються одразу; рядок пошуку — кнопкою «Знайти» або Enter,
/// щоб не надсилати запит на кожну введену цифру.
/// </remarks>
public sealed partial class OrderListViewModel : ViewModelBase, ILoadable
{
    private readonly IOrderService _orders;
    private readonly IClientService _clients;
    private readonly ICourierService _couriers;
    private readonly IOrderFormLauncher _orderForm;
    private readonly IOrderDetailsDialog _orderDetails;
    private readonly IDialogService _dialogs;
    private readonly ICurrentUser _currentUser;
    private readonly IClock _clock;
    private bool _isLoaded;

    /// <summary>
    /// Створює сторінку.
    /// </summary>
    /// <param name="orders">Сервіс замовлень.</param>
    /// <param name="clients">Довідник клієнтів (для фільтра).</param>
    /// <param name="couriers">Довідник кур'єрів (для фільтра).</param>
    /// <param name="orderForm">Форма нового замовлення.</param>
    /// <param name="orderDetails">Картка замовлення.</param>
    /// <param name="dialogs">Повідомлення.</param>
    /// <param name="currentUser">Поточний користувач.</param>
    /// <param name="clock">Поточний час (типовий період).</param>
    public OrderListViewModel(
        IOrderService orders,
        IClientService clients,
        ICourierService couriers,
        IOrderFormLauncher orderForm,
        IOrderDetailsDialog orderDetails,
        IDialogService dialogs,
        ICurrentUser currentUser,
        IClock clock)
    {
        _orders = orders;
        _clients = clients;
        _couriers = couriers;
        _orderForm = orderForm;
        _orderDetails = orderDetails;
        _dialogs = dialogs;
        _currentUser = currentUser;
        _clock = clock;

        Statuses = [new(null, "Усі"), .. Enum.GetValues<OrderStatus>().Select(s => new Option<OrderStatus?>(s, s.ToDisplayName()))];
        PaymentStatuses = [new(null, "Усі"), .. Enum.GetValues<PaymentStatus>().Select(s => new Option<PaymentStatus?>(s, s.ToDisplayName()))];
        SetDefaultFilter();
    }

    /// <inheritdoc />
    public override string Title => "Список замовлень";

    /// <summary>Чи може користувач оформлювати замовлення (оператор).</summary>
    public bool CanCreate => _currentUser.Session?.Role == UserRole.Operator;

    /// <summary>Варіанти фільтра статусу.</summary>
    public IReadOnlyList<Option<OrderStatus?>> Statuses { get; }

    /// <summary>Варіанти фільтра оплати.</summary>
    public IReadOnlyList<Option<PaymentStatus?>> PaymentStatuses { get; }

    /// <summary>Варіанти фільтра кур'єра (заповнюються під час завантаження).</summary>
    [ObservableProperty]
    public partial IReadOnlyList<Option<int?>> Couriers { get; private set; } = [new(null, "Усі")];

    /// <summary>Варіанти фільтра відправника (заповнюються під час завантаження).</summary>
    [ObservableProperty]
    public partial IReadOnlyList<Option<int?>> Clients { get; private set; } = [new(null, "Усі")];

    /// <summary>Знайдені замовлення, новіші першими.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<OrderListItem> Items { get; private set; } = [];

    /// <summary>Виділене замовлення.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenOrderCommand))]
    public partial OrderListItem? SelectedOrder { get; set; }

    /// <summary>Пояснення до результату (помилка в запиті, обрізаний результат).</summary>
    [ObservableProperty]
    public partial string? Notice { get; private set; }

    /// <summary>Кількість знайдених замовлень.</summary>
    [ObservableProperty]
    public partial string CountText { get; private set; } = string.Empty;

    /// <summary>Чи триває запит до БД.</summary>
    [ObservableProperty]
    public partial bool IsBusy { get; private set; }

    /// <summary>Трек-номер або телефон.</summary>
    [ObservableProperty]
    public partial string SearchText { get; set; } = string.Empty;

    /// <summary>Фільтр статусу.</summary>
    [ObservableProperty]
    public partial Option<OrderStatus?> StatusFilter { get; set; }

    /// <summary>Фільтр стану оплати.</summary>
    [ObservableProperty]
    public partial Option<PaymentStatus?> PaymentFilter { get; set; }

    /// <summary>Фільтр кур'єра.</summary>
    [ObservableProperty]
    public partial Option<int?> CourierFilter { get; set; }

    /// <summary>Фільтр відправника.</summary>
    [ObservableProperty]
    public partial Option<int?> ClientFilter { get; set; }

    /// <summary>Перший день періоду.</summary>
    [ObservableProperty]
    public partial DateTime? From { get; set; }

    /// <summary>Останній день періоду.</summary>
    [ObservableProperty]
    public partial DateTime? To { get; set; }

    /// <summary>Умови пошуку з полів сторінки.</summary>
    public OrderFilter CurrentFilter => new(
        SearchText, StatusFilter.Value, From, To, CourierFilter.Value, ClientFilter.Value, PaymentFilter.Value);

    partial void OnStatusFilterChanged(Option<OrderStatus?> value) => SearchOnFilterChange();

    partial void OnPaymentFilterChanged(Option<PaymentStatus?> value) => SearchOnFilterChange();

    partial void OnCourierFilterChanged(Option<int?> value) => SearchOnFilterChange();

    partial void OnClientFilterChanged(Option<int?> value) => SearchOnFilterChange();

    partial void OnFromChanged(DateTime? value) => SearchOnFilterChange();

    partial void OnToChanged(DateTime? value) => SearchOnFilterChange();

    [RelayCommand]
    private async Task LoadAsync()
    {
        try
        {
            var clients = await _clients.GetAllAsync();
            var couriers = await _couriers.GetAllAsync();
            Clients = [new(null, "Усі"), .. clients.Select(c => new Option<int?>(c.Id, c.Name))];
            Couriers = [new(null, "Усі"), .. couriers.Select(c => new Option<int?>(c.Id, c.FullName))];
            ClientFilter = Clients[0];
            CourierFilter = Couriers[0];
            _isLoaded = true;
            await SearchAsync();
        }
        catch (DomainException ex)
        {
            _dialogs.ShowError(ex.UserMessage);
        }
    }

    [RelayCommand]
    private async Task SearchAsync()
    {
        IsBusy = true;
        try
        {
            OrderSearchResult result = await _orders.SearchAsync(CurrentFilter);
            Items = result.Items;
            Notice = result.Notice;
            CountText = result.IsTruncated
                ? $"Показано {result.Items.Count} (знайдено більше)"
                : $"Знайдено: {result.Items.Count}";
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

    [RelayCommand]
    private async Task ResetFiltersAsync()
    {
        _isLoaded = false;   // одна перевірка замість шести запитів під час скидання
        SetDefaultFilter();
        _isLoaded = true;
        await SearchAsync();
    }

    [RelayCommand(CanExecute = nameof(CanCreate))]
    private async Task NewOrderAsync()
    {
        if (await _orderForm.OpenNewOrderAsync() is not null)
        {
            await SearchAsync();
        }
    }

    private bool CanOpenOrder() => SelectedOrder is not null;

    [RelayCommand(CanExecute = nameof(CanOpenOrder))]
    private async Task OpenOrderAsync()
    {
        // Після дії в картці (призначення, скасування…) список оновлюється
        if (SelectedOrder is { } order && _orderDetails.ShowOrder(order.Id))
        {
            await SearchAsync();
        }
    }

    private void SetDefaultFilter()
    {
        OrderFilter defaults = OrderFilter.LastDays(_clock.Now);
        SearchText = string.Empty;
        StatusFilter = Statuses[0];
        PaymentFilter = PaymentStatuses[0];
        CourierFilter = Couriers[0];
        ClientFilter = Clients[0];
        From = defaults.From;
        To = defaults.To;
    }

    private void SearchOnFilterChange()
    {
        // До першого завантаження (і під час скидання) запити не надсилаються
        if (_isLoaded)
        {
            SearchCommand.Execute(null);
        }
    }
}
