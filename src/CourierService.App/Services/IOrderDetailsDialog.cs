using System.Windows;
using CourierService.App.ViewModels.Orders;
using CourierService.App.Views.Orders;
using CourierService.Core.Orders;
using CourierService.Core.Security;

namespace CourierService.App.Services;

/// <summary>
/// Показ картки замовлення в модальному вікні (рішення D-25).
/// </summary>
public interface IOrderDetailsDialog
{
    /// <summary>
    /// Показує картку замовлення.
    /// </summary>
    /// <param name="orderId">Замовлення.</param>
    /// <returns><c>true</c>, якщо в картці виконано дію (список треба оновити).</returns>
    bool ShowOrder(int orderId);
}

/// <summary>
/// Реалізація <see cref="IOrderDetailsDialog"/>: окреме вікно з <see cref="OrderDetailsViewModel"/>.
/// </summary>
public sealed class OrderDetailsDialog : IOrderDetailsDialog
{
    private readonly IOrderService _orders;
    private readonly IOrderActionService _actions;
    private readonly IOrderWorkflow _workflow;
    private readonly ICurrentUser _currentUser;
    private readonly IDialogService _dialogs;
    private readonly IEditDialogService _editDialogs;

    /// <summary>
    /// Створює сервіс.
    /// </summary>
    /// <param name="orders">Сервіс замовлень.</param>
    /// <param name="actions">Сервіс дій.</param>
    /// <param name="workflow">Правила переходів.</param>
    /// <param name="currentUser">Поточний користувач.</param>
    /// <param name="dialogs">Повідомлення.</param>
    /// <param name="editDialogs">Вікна вибору кур'єра й причини.</param>
    public OrderDetailsDialog(
        IOrderService orders,
        IOrderActionService actions,
        IOrderWorkflow workflow,
        ICurrentUser currentUser,
        IDialogService dialogs,
        IEditDialogService editDialogs)
    {
        _orders = orders;
        _actions = actions;
        _workflow = workflow;
        _currentUser = currentUser;
        _dialogs = dialogs;
        _editDialogs = editDialogs;
    }

    /// <inheritdoc />
    public bool ShowOrder(int orderId)
    {
        var viewModel = new OrderDetailsViewModel(orderId, _orders, _actions, _workflow, _currentUser, _dialogs, _editDialogs);
        var window = new OrderDetailsWindow(viewModel);
        if (Application.Current?.MainWindow is { IsVisible: true } owner)
        {
            window.Owner = owner;
        }

        window.ShowDialog();
        return viewModel.HasChanges;
    }
}
