using System.Linq.Expressions;
using CourierService.Core.Abstractions;
using CourierService.Core.Entities;
using CourierService.Core.Enums;
using CourierService.Core.Exceptions;
using CourierService.Core.Orders;
using CourierService.Core.Pricing;
using CourierService.Core.Security;
using CourierService.Core.Sla;
using CourierService.Core.Validation;
using Microsoft.EntityFrameworkCore;

namespace CourierService.Data.Services;

/// <summary>
/// Замовлення: оформлення (оператор), пошук, картка замовлення, доставки кур'єра.
/// </summary>
public sealed class OrderService : IOrderService
{
    // Друга спроба потрібна, лише якщо трек-номер встиг зайняти інший запис (рішення D-23)
    private const int MaxSaveAttempts = 2;

    /// <summary>Проєкція замовлення в рядок списку — виконується в SQL, вибираються лише потрібні колонки.</summary>
    private static readonly Expression<Func<Order, OrderListItem>> ToListItem = o => new OrderListItem(
        o.Id,
        o.TrackingNumber,
        o.CreatedAt,
        o.Status,
        o.Client.Name,
        o.Client.Phone,
        o.RecipientName,
        o.RecipientPhone,
        o.PickupZone.Name,
        o.DeliveryZone.Name,
        o.Tariff.Name,
        o.Price,
        o.Payment!.Status,
        o.Payment.Payer,
        o.Payment.Method,
        o.Courier != null ? o.Courier.User.FullName : null,
        o.Deadline,
        o.DeliveredAt);

    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly ICurrentUser _currentUser;
    private readonly OrderValidator _validator;
    private readonly IPriceCalculator _priceCalculator;
    private readonly IDeadlineCalculator _deadlineCalculator;
    private readonly IClock _clock;
    private readonly ISlaEvaluator _sla;

    /// <summary>
    /// Створює сервіс.
    /// </summary>
    /// <param name="contextFactory">Фабрика контекстів БД.</param>
    /// <param name="currentUser">Поточний користувач для перевірки прав.</param>
    /// <param name="validator">Правила замовлення.</param>
    /// <param name="priceCalculator">Калькулятор вартості.</param>
    /// <param name="deadlineCalculator">Розрахунок дедлайну.</param>
    /// <param name="clock">Поточний час.</param>
    /// <param name="sla">Оцінка термінів для рядків списку.</param>
    public OrderService(
        IDbContextFactory<AppDbContext> contextFactory,
        ICurrentUser currentUser,
        OrderValidator validator,
        IPriceCalculator priceCalculator,
        IDeadlineCalculator deadlineCalculator,
        IClock clock,
        ISlaEvaluator sla)
    {
        _contextFactory = contextFactory;
        _currentUser = currentUser;
        _validator = validator;
        _priceCalculator = priceCalculator;
        _deadlineCalculator = deadlineCalculator;
        _clock = clock;
        _sla = sla;
    }

    /// <inheritdoc />
    public async Task<OrderCreated> CreateAsync(OrderInput input, CancellationToken cancellationToken = default)
    {
        _validator.Validate(input).ThrowIfInvalid();
        UserSession session = _currentUser.RequireRole(UserRole.Operator);
        PhoneNumber.TryNormalize(input.RecipientPhone, out string recipientPhone); // валідатор уже перевірив номер

        for (int attempt = 1; ; attempt++)
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            Order order = await BuildOrderAsync(context, input, recipientPhone, session.UserId, cancellationToken);

            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
            order.TrackingNumber = await NextTrackingNumberAsync(context, order.CreatedAt, cancellationToken);
            context.Orders.Add(order);
            try
            {
                await context.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return new OrderCreated(order.Id, order.TrackingNumber, order.Price, order.Deadline);
            }
            catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex) && attempt < MaxSaveAttempts)
            {
                // Номер зайняли між читанням і записом — повторюємо з новим контекстом
            }
        }
    }

    /// <inheritdoc />
    public async Task<OrderSearchResult> SearchAsync(OrderFilter filter, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        _currentUser.RequireRole(UserRole.Admin, UserRole.Operator);

        OrderQuery query = OrderQuery.Parse(filter.Query);
        if (query.Kind == OrderQueryKind.Invalid)
        {
            return new OrderSearchResult([], false, query.Message);
        }

        if (query.Kind != OrderQueryKind.TrackingNumber && filter.From > filter.To)
        {
            return new OrderSearchResult([], false, "Початкова дата періоду пізніша за кінцеву.");
        }

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        IQueryable<Order> orders = context.Orders.AsNoTracking();

        if (query.Kind == OrderQueryKind.TrackingNumber)
        {
            // Трек-номер однозначний — інші умови не застосовуються, шукаємо за весь час
            orders = orders.Where(o => o.TrackingNumber == query.Value);
        }
        else
        {
            orders = ApplyFilter(orders, filter, query);
        }

        // Береться на один рядок більше за межу — так видно, що результат обрізано
        List<OrderListItem> items = await orders
            .OrderByDescending(o => o.CreatedAt)
            .ThenByDescending(o => o.Id)
            .Take(OrderFilter.MaxResults + 1)
            .Select(ToListItem)
            .ToListAsync(cancellationToken);

        bool isTruncated = items.Count > OrderFilter.MaxResults;
        if (isTruncated)
        {
            items.RemoveAt(items.Count - 1);
        }

        string? notice = isTruncated
            ? $"Показано {OrderFilter.MaxResults} найновіших замовлень. Уточніть фільтр, щоб побачити решту."
            : query.Kind == OrderQueryKind.TrackingNumber && items.Count == 0
                ? $"Замовлення з трек-номером {query.Value} не знайдено."
                : null;
        return new OrderSearchResult(WithSla(items), isTruncated, notice);
    }

    /// <inheritdoc />
    public async Task<Order> GetOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        UserSession session = _currentUser.RequireRole(UserRole.Admin, UserRole.Operator, UserRole.Courier);

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        Order order = await context.Orders
            .AsNoTracking()
            .Include(o => o.Client)
            .Include(o => o.PickupZone)
            .Include(o => o.DeliveryZone)
            .Include(o => o.Tariff)
            .Include(o => o.CreatedBy)
            .Include(o => o.Courier!).ThenInclude(c => c.User)
            .Include(o => o.Payment!).ThenInclude(p => p.ConfirmedBy)
            .Include(o => o.StatusChanges).ThenInclude(c => c.ChangedBy)
            .AsSplitQuery()
            .SingleOrDefaultAsync(o => o.Id == orderId, cancellationToken)
            ?? throw new EntityNotFoundException("Замовлення не знайдено.");

        if (session.Role == UserRole.Courier && order.CourierId != session.CourierId)
        {
            throw new AccessDeniedException("Це замовлення не призначено вам.");
        }

        return order;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<OrderListItem>> GetMyDeliveriesAsync(bool active, CancellationToken cancellationToken = default)
    {
        UserSession session = _currentUser.RequireRole(UserRole.Courier);
        int courierId = session.CourierId ?? throw new AccessDeniedException("Обліковий запис не пов'язано з кур'єром.");
        OrderStatus[] inProgress = [.. OrderWorkflow.InProgressStatuses];

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        IQueryable<Order> orders = context.Orders.AsNoTracking().Where(o => o.CourierId == courierId);

        // Активні — найтерміновіші першими; історія — новіші першими й не більше межі списку
        orders = active
            ? orders.Where(o => inProgress.Contains(o.Status)).OrderBy(o => o.Deadline)
            : orders.Where(o => !inProgress.Contains(o.Status)).OrderByDescending(o => o.CreatedAt).Take(OrderFilter.MaxResults);
        return WithSla(await orders.Select(ToListItem).ToListAsync(cancellationToken));
    }

    /// <summary>
    /// Додає до рядків оцінку терміну: вона залежить від поточного часу, тому рахується після запиту, а не в SQL.
    /// </summary>
    private List<OrderListItem> WithSla(List<OrderListItem> items) =>
        items.Select(item => item with { Sla = _sla.Evaluate(item.Status, item.CreatedAt, item.Deadline, item.DeliveredAt) })
            .ToList();

    private static IQueryable<Order> ApplyFilter(IQueryable<Order> orders, OrderFilter filter, OrderQuery query)
    {
        if (query.Kind == OrderQueryKind.Phone)
        {
            // Телефони зберігаються як +380XXXXXXXXX, тож фрагмент цифр шукається як підрядок
            string digits = query.Value;
            orders = orders.Where(o => o.RecipientPhone.Contains(digits) || o.Client.Phone.Contains(digits));
        }

        if (filter.Status is { } status)
        {
            orders = orders.Where(o => o.Status == status);
        }

        if (filter.From is { } from)
        {
            DateTime start = from.Date;
            orders = orders.Where(o => o.CreatedAt >= start);
        }

        if (filter.To is { } to)
        {
            // Останній день включно: усе, що раніше за початок наступного дня
            DateTime end = to.Date.AddDays(1);
            orders = orders.Where(o => o.CreatedAt < end);
        }

        if (filter.CourierId is { } courierId)
        {
            orders = orders.Where(o => o.CourierId == courierId);
        }

        if (filter.ClientId is { } clientId)
        {
            orders = orders.Where(o => o.ClientId == clientId);
        }

        if (filter.PaymentStatus is { } paymentStatus)
        {
            orders = orders.Where(o => o.Payment!.Status == paymentStatus);
        }

        return orders;
    }

    /// <summary>
    /// Збирає замовлення з даних форми й довідників: ціна й дедлайн рахуються тут,
    /// а не беруться з інтерфейсу (рішення D-21).
    /// </summary>
    private async Task<Order> BuildOrderAsync(
        AppDbContext context, OrderInput input, string recipientPhone, int userId, CancellationToken cancellationToken)
    {
        Client client = await context.Clients.SingleOrDefaultAsync(c => c.Id == input.ClientId, cancellationToken)
            ?? throw new DomainValidationException("Відправника не знайдено.", nameof(input.ClientId));
        Zone pickupZone = await FindZoneAsync(context, input.PickupZoneId, nameof(input.PickupZoneId), cancellationToken);
        Zone deliveryZone = await FindZoneAsync(context, input.DeliveryZoneId, nameof(input.DeliveryZoneId), cancellationToken);
        Tariff tariff = await context.Tariffs.SingleOrDefaultAsync(t => t.Id == input.TariffId, cancellationToken)
            ?? throw new DomainValidationException("Тариф не знайдено.", nameof(input.TariffId));

        if (!client.IsActive)
        {
            throw new DomainValidationException($"Клієнта «{client.Name}» деактивовано.", nameof(input.ClientId));
        }

        if (!tariff.IsActive)
        {
            throw new DomainValidationException($"Тариф «{tariff.Name}» деактивовано. Оберіть інший.", nameof(input.TariffId));
        }

        _validator.Validate(input, tariff).ThrowIfInvalid();

        PriceBreakdown price = _priceCalculator.Calculate(tariff, pickupZone, deliveryZone, input.Parcel);
        DateTime now = _clock.Now;
        DateTime deadline = _deadlineCalculator.CalculateDeadline(now, tariff.DeliveryHours);
        return Compose(input, recipientPhone, client, pickupZone, deliveryZone, tariff, price, now, deadline, userId);
    }

    /// <summary>
    /// Складає нове замовлення зі знімком вартості, оплатою (очікує) і першим записом історії.
    /// Спільне для оформлення оператором і демонстраційних даних — обидва шляхи дають однакові замовлення.
    /// </summary>
    /// <param name="input">Перевірені дані форми.</param>
    /// <param name="recipientPhone">Телефон одержувача у форматі +380XXXXXXXXX.</param>
    /// <param name="client">Відправник.</param>
    /// <param name="pickupZone">Зона забору.</param>
    /// <param name="deliveryZone">Зона доставки.</param>
    /// <param name="tariff">Тариф.</param>
    /// <param name="price">Розрахована вартість.</param>
    /// <param name="createdAt">Час оформлення.</param>
    /// <param name="deadline">Дедлайн доставки.</param>
    /// <param name="userId">Хто оформив.</param>
    /// <returns>Замовлення без трек-номера (його призначає той, хто зберігає).</returns>
    internal static Order Compose(
        OrderInput input, string recipientPhone, Client client, Zone pickupZone, Zone deliveryZone, Tariff tariff,
        PriceBreakdown price, DateTime createdAt, DateTime deadline, int userId)
    {
        var order = new Order
        {
            Client = client,
            RecipientName = input.RecipientName.Trim(),
            RecipientPhone = recipientPhone,
            PickupAddress = input.PickupAddress.Trim(),
            PickupZone = pickupZone,
            DeliveryAddress = input.DeliveryAddress.Trim(),
            DeliveryZone = deliveryZone,
            Tariff = tariff,
            WeightGrams = input.WeightGrams,
            LengthCm = input.LengthCm,
            WidthCm = input.WidthCm,
            HeightCm = input.HeightCm,
            DeclaredValue = input.DeclaredValue,
            IsFragile = input.IsFragile,
            ChargeableWeightGrams = price.ChargeableWeightGrams,
            ZoneCoefficientPercent = price.ZoneCoefficientPercent,
            BaseCost = price.BaseCost,
            WeightCost = price.WeightCost,
            CrossRiverCost = price.CrossRiverCost,
            InsuranceCost = price.InsuranceCost,
            FragileCost = price.FragileCost,
            Price = price.Total,
            CreatedByUserId = userId,
            CreatedAt = createdAt,
            Deadline = deadline,
            Comment = string.IsNullOrWhiteSpace(input.Comment) ? null : input.Comment.Trim(),
            Payment = new Payment
            {
                Payer = input.Payer,
                Method = input.Method,
                Amount = price.Total,
                Status = PaymentStatus.Pending,
            },
        };
        order.StatusChanges.Add(new OrderStatusChange
        {
            FromStatus = null,
            ToStatus = OrderStatus.New,
            ChangedAt = createdAt,
            ChangedByUserId = userId,
        });
        return order;
    }

    private static async Task<Zone> FindZoneAsync(
        AppDbContext context, int zoneId, string property, CancellationToken cancellationToken)
    {
        Zone zone = await context.Zones.SingleOrDefaultAsync(z => z.Id == zoneId, cancellationToken)
            ?? throw new DomainValidationException("Зону не знайдено.", property);
        return zone.IsActive
            ? zone
            : throw new DomainValidationException($"Зону «{zone.Name}» деактивовано. Оберіть іншу.", property);
    }

    /// <summary>
    /// Наступний трек-номер дня: найбільший номер з префіксом дня + 1 (рішення D-23).
    /// </summary>
    private static async Task<string> NextTrackingNumberAsync(
        AppDbContext context, DateTime createdAt, CancellationToken cancellationToken)
    {
        string prefix = TrackingNumber.DayPrefix(createdAt);

        // Номери дня мають однакову довжину, тож найбільший рядок — найбільший порядковий номер
        string? last = await context.Orders
            .Where(o => o.TrackingNumber.StartsWith(prefix))
            .OrderByDescending(o => o.TrackingNumber)
            .Select(o => o.TrackingNumber)
            .FirstOrDefaultAsync(cancellationToken);

        int sequence = TrackingNumber.TryGetSequence(last, out int lastSequence) ? lastSequence + 1 : 1;
        if (sequence > TrackingNumber.MaxSequence)
        {
            throw new DomainValidationException(
                $"Досягнуто ліміт {TrackingNumber.MaxSequence} замовлень за добу. Нові замовлення можна оформити завтра.");
        }

        return TrackingNumber.Create(createdAt, sequence);
    }
}
