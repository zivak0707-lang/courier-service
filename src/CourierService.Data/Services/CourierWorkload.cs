using CourierService.Core.Enums;
using CourierService.Core.Exceptions;
using CourierService.Core.Orders;
using Microsoft.EntityFrameworkCore;

namespace CourierService.Data.Services;

/// <summary>
/// Перевірка, що в кур'єра немає посилок, по яких ще не ухвалено рішення (рішення D-28, уточнення).
/// Спільна для довідника кур'єрів і довідника користувачів: вимкнений обліковий запис теж
/// не дав би кур'єру завершити доставку.
/// </summary>
internal static class CourierWorkload
{
    // Скільки трек-номерів показати в повідомленні
    private const int ShownNumbers = 5;

    /// <summary>
    /// Кидає виняток, якщо кур'єр має призначені, забрані або невдалі доставки.
    /// </summary>
    /// <param name="context">Контекст БД.</param>
    /// <param name="courierId">Кур'єр.</param>
    /// <param name="cancellationToken">Скасування операції.</param>
    /// <returns>Задача.</returns>
    /// <exception cref="DomainValidationException">Є незавершені доставки.</exception>
    public static async Task EnsureNoHeldOrdersAsync(AppDbContext context, int courierId, CancellationToken cancellationToken)
    {
        OrderStatus[] held = [.. OrderWorkflow.HeldByCourierStatuses];
        List<string> numbers = await context.Orders
            .Where(o => o.CourierId == courierId && held.Contains(o.Status))
            .OrderBy(o => o.Deadline)
            .Select(o => o.TrackingNumber)
            .ToListAsync(cancellationToken);

        if (numbers.Count > 0)
        {
            string shown = string.Join(", ", numbers.Take(ShownNumbers)) + (numbers.Count > ShownNumbers ? "…" : string.Empty);
            throw new DomainValidationException(
                $"Кур'єра не можна деактивувати: незавершених доставок — {numbers.Count} ({shown}). "
                + "Спершу перепризначте їх іншому кур'єру або завершіть.");
        }
    }
}
