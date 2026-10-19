using CourierService.Core.Abstractions;
using CourierService.Core.Enums;
using CourierService.Core.Reports;
using CourierService.Core.Security;
using Microsoft.EntityFrameworkCore;

namespace CourierService.Data.Services;

/// <summary>
/// Звіти й дані дашборда (рішення D-29): SQL відбирає легкі рядки за період за індексованими датами,
/// а суми, частки й терміни рахують класи звітів у Core.
/// </summary>
public sealed class ReportService : IReportService
{
    private static readonly UserRole[] ReadRoles = [UserRole.Admin, UserRole.Operator];

    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly ICurrentUser _currentUser;
    private readonly IClock _clock;

    /// <summary>
    /// Створює сервіс.
    /// </summary>
    /// <param name="contextFactory">Фабрика контекстів БД.</param>
    /// <param name="currentUser">Поточний користувач.</param>
    /// <param name="clock">Поточний час (момент формування звіту).</param>
    public ReportService(IDbContextFactory<AppDbContext> contextFactory, ICurrentUser currentUser, IClock clock)
    {
        _contextFactory = contextFactory;
        _currentUser = currentUser;
        _clock = clock;
    }

    /// <inheritdoc />
    public async Task<ReportTable> BuildAsync(ReportKind kind, ReportPeriod period, CancellationToken cancellationToken = default)
    {
        ReportBase report = ReportCatalog.Get(kind);
        _currentUser.RequireRole([.. report.AllowedRoles]);

        // Поліморфізм: далі всі звіти будуються однаково через базовий клас
        return report.Build(await LoadAsync(period, cancellationToken));
    }

    /// <inheritdoc />
    public Task<ReportData> GetDataAsync(ReportPeriod period, CancellationToken cancellationToken = default)
    {
        _currentUser.RequireRole(ReadRoles);
        return LoadAsync(period, cancellationToken);
    }

    private async Task<ReportData> LoadAsync(ReportPeriod period, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(period);
        DateTime start = period.Start;
        DateTime end = period.EndExclusive;

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        // Замовлення, оформлені, доставлені або оплачені в періоді, а також усі неоплачені на зараз
        List<ReportOrderRow> orders = await context.Orders
            .AsNoTracking()
            .Where(o => (o.CreatedAt >= start && o.CreatedAt < end)
                || (o.DeliveredAt >= start && o.DeliveredAt < end)
                || (o.Payment!.PaidAt >= start && o.Payment.PaidAt < end)
                || o.Payment!.Status == PaymentStatus.Pending)
            .Select(o => new ReportOrderRow(
                o.Id,
                o.TrackingNumber,
                o.Status,
                o.CreatedAt,
                o.Deadline,
                o.DeliveredAt,
                o.Price,
                o.Client.Name,
                o.CourierId,
                o.CourierPayout,
                o.Payment!.Payer,
                o.Payment.Method,
                o.Payment.Status,
                o.Payment.PaidAt))
            .ToListAsync(cancellationToken);

        // Невдала спроба належить кур'єру, який про неї повідомив
        List<FailedAttempt> failures = await (
                from change in context.OrderStatusChanges
                join courier in context.Couriers on change.ChangedByUserId equals courier.UserId
                where change.ToStatus == OrderStatus.DeliveryFailed && change.ChangedAt >= start && change.ChangedAt < end
                select new FailedAttempt(courier.Id, change.ChangedAt))
            .ToListAsync(cancellationToken);

        List<ReportCourier> couriers = await context.Couriers
            .Select(c => new ReportCourier(c.Id, c.User.FullName, c.IsActive))
            .ToListAsync(cancellationToken);

        return new ReportData(period, _clock.Now, orders, failures, couriers);
    }
}
