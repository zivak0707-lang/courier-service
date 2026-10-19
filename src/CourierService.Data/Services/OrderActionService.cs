using CourierService.Core.Abstractions;
using CourierService.Core.Assignment;
using CourierService.Core.Directories;
using CourierService.Core.Entities;
using CourierService.Core.Enums;
using CourierService.Core.Exceptions;
using CourierService.Core.Orders;
using CourierService.Core.Security;
using Microsoft.EntityFrameworkCore;

namespace CourierService.Data.Services;

/// <summary>
/// Дії над замовленням. Сервіс лише завантажує дані й зберігає результат; що дозволено,
/// вирішує <see cref="IOrderWorkflow"/> (рішення D-24), кого призначити — <see cref="ICourierAssignmentService"/> (D-27).
/// </summary>
public sealed class OrderActionService : IOrderActionService
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly ICurrentUser _currentUser;
    private readonly IOrderWorkflow _workflow;
    private readonly ICourierAssignmentService _assignment;
    private readonly IClock _clock;

    /// <summary>
    /// Створює сервіс.
    /// </summary>
    /// <param name="contextFactory">Фабрика контекстів БД.</param>
    /// <param name="currentUser">Поточний користувач.</param>
    /// <param name="workflow">Правила переходів.</param>
    /// <param name="assignment">Алгоритм автопризначення.</param>
    /// <param name="clock">Поточний час.</param>
    public OrderActionService(
        IDbContextFactory<AppDbContext> contextFactory,
        ICurrentUser currentUser,
        IOrderWorkflow workflow,
        ICourierAssignmentService assignment,
        IClock clock)
    {
        _contextFactory = contextFactory;
        _currentUser = currentUser;
        _workflow = workflow;
        _assignment = assignment;
        _clock = clock;
    }

    /// <inheritdoc />
    public async Task ExecuteAsync(
        int orderId, OrderAction action, int? courierId = null, string? reason = null, CancellationToken cancellationToken = default)
    {
        UserSession session = _currentUser.RequireRole(UserRole.Operator, UserRole.Courier);

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        Order order = await context.Orders
            .Include(o => o.Payment)
            .Include(o => o.Courier)
            .SingleOrDefaultAsync(o => o.Id == orderId, cancellationToken)
            ?? throw new EntityNotFoundException("Замовлення не знайдено.");

        Courier? courier = null;
        if (courierId is { } id)
        {
            courier = await context.Couriers.SingleOrDefaultAsync(c => c.Id == id, cancellationToken)
                ?? throw new DomainValidationException("Кур'єра не знайдено.", "CourierId");
        }

        _workflow.Execute(order, action, session, _clock.Now, courier, reason);
        await context.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<AssignmentOptions> GetAssignmentOptionsAsync(int orderId, CancellationToken cancellationToken = default)
    {
        _currentUser.RequireRole(UserRole.Operator);
        OrderStatus[] inProgress = [.. OrderWorkflow.InProgressStatuses];

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var order = await context.Orders
            .Where(o => o.Id == orderId)
            .Select(o => new { o.PickupZoneId, o.PickupZone.Bank, o.ChargeableWeightGrams })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new EntityNotFoundException("Замовлення не знайдено.");

        // Кількість активних доставок рахується в SQL окремим підзапитом для кожного кур'єра
        List<CourierCandidate> couriers = await context.Couriers
            .Select(c => new CourierCandidate(
                c.Id,
                c.User.FullName,
                c.HomeZoneId,
                c.HomeZone.Name,
                c.HomeZone.Bank,
                c.MaxWeightGrams,
                context.Orders.Count(o => o.CourierId == c.Id && inProgress.Contains(o.Status)),
                c.IsActive))
            .ToListAsync(cancellationToken);

        AssignmentSuggestion suggestion = _assignment.Suggest(
            new AssignmentRequest(order.PickupZoneId, order.Bank, order.ChargeableWeightGrams), couriers);
        List<CourierCandidate> sorted = couriers
            .OrderByDescending(c => c.IsActive)
            .ThenBy(c => c.FullName, TextSearch.UkrainianComparer)
            .ToList();
        return new AssignmentOptions(sorted, suggestion);
    }
}
