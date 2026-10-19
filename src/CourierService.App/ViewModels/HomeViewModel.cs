using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CourierService.App.Services;
using CourierService.Core.Abstractions;
using CourierService.Core.Backups;
using CourierService.Core.Enums;
using CourierService.Core.Exceptions;
using CourierService.Core.Orders;
using CourierService.Core.Reports;
using CourierService.Core.Security;

namespace CourierService.App.ViewModels;

/// <summary>
/// Головна сторінка — робочий стіл за роллю (рішення D-42): оператору — нові без кур'єра, прострочені
/// й неоплачені; кур'єру — його активні доставки й найближчий дедлайн; адміністратору — показники дня
/// й остання резервна копія.
/// </summary>
public sealed partial class HomeViewModel : ViewModelBase, ILoadable
{
    private readonly ICurrentUser _currentUser;
    private readonly IOrderService _orders;
    private readonly IReportService _reports;
    private readonly IBackupService _backups;
    private readonly IOrderDetailsDialog _orderDetails;
    private readonly IDialogService _dialogs;
    private readonly IClock _clock;

    /// <summary>
    /// Створює сторінку.
    /// </summary>
    /// <param name="currentUser">Поточний користувач (визначає вміст столу).</param>
    /// <param name="orders">Сервіс замовлень.</param>
    /// <param name="reports">Звіти (показники дня).</param>
    /// <param name="backups">Резервні копії.</param>
    /// <param name="orderDetails">Картка замовлення.</param>
    /// <param name="dialogs">Повідомлення.</param>
    /// <param name="clock">Поточний час.</param>
    public HomeViewModel(
        ICurrentUser currentUser,
        IOrderService orders,
        IReportService reports,
        IBackupService backups,
        IOrderDetailsDialog orderDetails,
        IDialogService dialogs,
        IClock clock)
    {
        _currentUser = currentUser;
        _orders = orders;
        _reports = reports;
        _backups = backups;
        _orderDetails = orderDetails;
        _dialogs = dialogs;
        _clock = clock;
    }

    /// <inheritdoc />
    public override string Title => "Головна";

    /// <summary>Привітання з ім'ям користувача.</summary>
    public string Greeting => _currentUser.Session is { } session ? $"Вітаємо, {session.FullName}!" : "Вітаємо!";

    /// <summary>Роль і сьогоднішня дата.</summary>
    public string Subtitle => $"{_currentUser.Session?.Role.ToDisplayName()} · {_clock.Now:dd.MM.yyyy}";

    /// <summary>Чи показувати стіл оператора.</summary>
    public bool IsOperator => _currentUser.Session?.Role == UserRole.Operator;

    /// <summary>Чи показувати стіл кур'єра.</summary>
    public bool IsCourier => _currentUser.Session?.Role == UserRole.Courier;

    /// <summary>Чи показувати стіл адміністратора.</summary>
    public bool IsAdmin => _currentUser.Session?.Role == UserRole.Admin;

    /// <summary>Підказка, з чого почати.</summary>
    public string Hint => "Усі розділи — у головному меню. Довідка — клавіша F1.";

    /// <summary>Підсумки оператора (після завантаження).</summary>
    [ObservableProperty]
    public partial OperatorDesk? Operator { get; private set; }

    /// <summary>Підсумки кур'єра (після завантаження).</summary>
    [ObservableProperty]
    public partial CourierDesk? Courier { get; private set; }

    /// <summary>Показники адміністратора за сьогодні й остання резервна копія.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<ReportSummaryItem> AdminTiles { get; private set; } = [];

    /// <summary>Чи триває завантаження.</summary>
    [ObservableProperty]
    public partial bool IsBusy { get; private set; }

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsBusy = true;
        try
        {
            switch (_currentUser.Session?.Role)
            {
                case UserRole.Operator:
                    Operator = await LoadOperatorAsync();
                    break;
                case UserRole.Courier:
                    Courier = WorkDesk.ForCourier(await _orders.GetMyDeliveriesAsync(active: true));
                    break;
                case UserRole.Admin:
                    AdminTiles = await LoadAdminTilesAsync();
                    break;
            }
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
    private async Task OpenOrderAsync(OrderListItem? item)
    {
        // Після дії в картці (призначили кур'єра, підтвердили оплату) підсумки змінюються
        if (item is not null && _orderDetails.ShowOrder(item.Id))
        {
            await LoadAsync();
        }
    }

    private async Task<OperatorDesk> LoadOperatorAsync()
    {
        // Окремий запит на кожен незавершений статус: так обмеження розміру списку не відріже старі замовлення
        var unfinished = new List<OrderListItem>();
        foreach (OrderStatus status in OrderWorkflow.OpenStatuses)
        {
            unfinished.AddRange((await _orders.SearchAsync(new OrderFilter(Status: status))).Items);
        }

        OrderSearchResult unpaid = await _orders.SearchAsync(new OrderFilter(PaymentStatus: PaymentStatus.Pending));
        return WorkDesk.ForOperator(unfinished, unpaid.Items);
    }

    private async Task<IReadOnlyList<ReportSummaryItem>> LoadAdminTilesAsync()
    {
        DateTime today = _clock.Now.Date;
        ReportData data = await _reports.GetDataAsync(new ReportPeriod(today, today));
        IReadOnlyList<BackupEntry> backups = await _backups.GetBackupsAsync();
        DateTime? lastBackup = backups.Count == 0 ? null : backups.Max(b => b.File.CreatedAt);

        return
        [
            .. DashboardCalculator.Calculate(data).Tiles,
            new("Остання резервна копія", ReportValueKind.DateTime, lastBackup),
        ];
    }
}
