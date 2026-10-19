using CourierService.Core.Assignment;

namespace CourierService.Core.Orders;

/// <summary>
/// Кур'єри для вибору під час призначення й пропозиція автопризначення.
/// </summary>
/// <param name="Couriers">Усі кур'єри з кількістю активних доставок (активні першими, за ім'ям).</param>
/// <param name="Suggestion">Кого пропонує алгоритм (рішення D-27).</param>
public sealed record AssignmentOptions(IReadOnlyList<CourierCandidate> Couriers, AssignmentSuggestion Suggestion);

/// <summary>
/// Дії над замовленням: зміна статусу, призначення кур'єра, позначка оплати (рішення D-24…D-27).
/// Правила перевіряє <see cref="IOrderWorkflow"/>; сервіс завантажує дані й зберігає результат.
/// </summary>
public interface IOrderActionService
{
    /// <summary>
    /// Виконує дію над замовленням від імені поточного користувача.
    /// </summary>
    /// <param name="orderId">Замовлення.</param>
    /// <param name="action">Дія.</param>
    /// <param name="courierId">Кур'єр для призначення або повторної доставки.</param>
    /// <param name="reason">Причина невдалої спроби.</param>
    /// <param name="cancellationToken">Скасування операції.</param>
    /// <returns>Задача.</returns>
    Task ExecuteAsync(
        int orderId, OrderAction action, int? courierId = null, string? reason = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Кур'єри для призначення з пропозицією алгоритму. Доступно оператору.
    /// </summary>
    /// <param name="orderId">Замовлення.</param>
    /// <param name="cancellationToken">Скасування операції.</param>
    /// <returns>Кандидати й пропозиція.</returns>
    Task<AssignmentOptions> GetAssignmentOptionsAsync(int orderId, CancellationToken cancellationToken = default);
}
