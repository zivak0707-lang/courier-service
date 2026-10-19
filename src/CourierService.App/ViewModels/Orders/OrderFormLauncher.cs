using CourierService.App.Services;
using CourierService.Core.Abstractions;
using CourierService.Core.Directories;
using CourierService.Core.Orders;
using CourierService.Core.Pricing;
using CourierService.Core.Sla;

namespace CourierService.App.ViewModels.Orders;

/// <summary>
/// Відкриття форми нового замовлення — з меню «Замовлення → Нове» і зі сторінки списку.
/// </summary>
public interface IOrderFormLauncher
{
    /// <summary>
    /// Завантажує довідники, показує форму й після збереження повідомляє трек-номер.
    /// </summary>
    /// <returns>Результат оформлення або <c>null</c>, якщо форму закрито без збереження.</returns>
    Task<OrderCreated?> OpenNewOrderAsync();
}

/// <summary>
/// Реалізація <see cref="IOrderFormLauncher"/>: модальне вікно на основі спільного вікна редагування (рішення D-21).
/// </summary>
public sealed class OrderFormLauncher : IOrderFormLauncher
{
    private readonly IClientService _clients;
    private readonly IZoneService _zones;
    private readonly ITariffService _tariffs;
    private readonly IOrderService _orders;
    private readonly OrderValidator _validator;
    private readonly IPriceCalculator _calculator;
    private readonly IDeadlineCalculator _deadlines;
    private readonly IClock _clock;
    private readonly IEditDialogService _editDialogs;
    private readonly IDialogService _dialogs;

    /// <summary>
    /// Створює сервіс.
    /// </summary>
    /// <param name="clients">Довідник клієнтів.</param>
    /// <param name="zones">Довідник зон.</param>
    /// <param name="tariffs">Довідник тарифів.</param>
    /// <param name="orders">Сервіс замовлень.</param>
    /// <param name="validator">Правила замовлення.</param>
    /// <param name="calculator">Калькулятор вартості.</param>
    /// <param name="deadlines">Розрахунок дедлайну.</param>
    /// <param name="clock">Поточний час.</param>
    /// <param name="editDialogs">Вікно форми.</param>
    /// <param name="dialogs">Повідомлення.</param>
    public OrderFormLauncher(
        IClientService clients,
        IZoneService zones,
        ITariffService tariffs,
        IOrderService orders,
        OrderValidator validator,
        IPriceCalculator calculator,
        IDeadlineCalculator deadlines,
        IClock clock,
        IEditDialogService editDialogs,
        IDialogService dialogs)
    {
        _clients = clients;
        _zones = zones;
        _tariffs = tariffs;
        _orders = orders;
        _validator = validator;
        _calculator = calculator;
        _deadlines = deadlines;
        _clock = clock;
        _editDialogs = editDialogs;
        _dialogs = dialogs;
    }

    /// <inheritdoc />
    public async Task<OrderCreated?> OpenNewOrderAsync()
    {
        // Для нових замовлень пропонуються лише активні записи довідників
        var data = new OrderFormData(
            (await _clients.GetAllAsync()).Where(c => c.IsActive).ToList(),
            (await _zones.GetAllAsync()).Where(z => z.IsActive).ToList(),
            (await _tariffs.GetAllAsync()).Where(t => t.IsActive).ToList());

        if (data.Clients.Count == 0)
        {
            _dialogs.ShowWarning("Немає активних клієнтів. Спершу додайте відправника в довіднику «Клієнти».");
            return null;
        }

        if (data.Zones.Count == 0 || data.Tariffs.Count == 0)
        {
            _dialogs.ShowWarning("Немає активних зон або тарифів. Зверніться до адміністратора.");
            return null;
        }

        var editor = new OrderEditorViewModel(_orders, _validator, _calculator, _deadlines, _clock, data);
        if (!_editDialogs.ShowEditor(editor) || editor.Created is not { } created)
        {
            return null;
        }

        // Трек-номер оператор зазвичай одразу надсилає клієнту, тому поруч — кнопка копіювання
        _dialogs.ShowInfoWithCopy(
            $"Замовлення оформлено.\n\nТрек-номер: {created.TrackingNumber}\nВартість: {created.Price}\n"
            + $"Доставити до: {created.Deadline:dd.MM.yyyy HH:mm}",
            created.TrackingNumber,
            "_Копіювати трек-номер");
        return created;
    }
}
