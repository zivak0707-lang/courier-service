using CourierService.Core;
using CourierService.Core.Abstractions;
using CourierService.Core.Entities;
using CourierService.Core.Enums;
using CourierService.Core.Orders;
using CourierService.Core.Pricing;
using CourierService.Core.Security;
using CourierService.Core.Sla;
using CourierService.Core.Validation;
using CourierService.Data.Services;
using Microsoft.EntityFrameworkCore;

namespace CourierService.Data.Seed;

/// <summary>
/// Демонстраційні замовлення за останні 30 днів у різних статусах (рішення D-28).
/// </summary>
/// <remarks>
/// Генератор детермінований (фіксоване зерно випадкових чисел): той самий день запуску дає ті самі дані.
/// Вартість рахує <see cref="PriceCalculator"/>, дедлайн — <see cref="DeadlineCalculator"/>, а статуси й оплати
/// змінює <see cref="OrderWorkflow"/> — тож демо-замовлення не можуть опинитися в неможливому стані.
/// Події, час яких ще не настав, не відбуваються: свіжі замовлення лишаються «в роботі».
/// Імена, телефони й адреси вигадані (D-12).
/// </remarks>
public sealed class DemoOrderGenerator
{
    private const int Seed = 2026;
    private const int HistoryDays = 30;

    private static readonly string[] RecipientNames =
    [
        "Оксана Мельник", "Тарас Бойко", "Ірина Шевчук", "Богдан Ковальчук", "Наталія Савченко", "Максим Олійник",
        "Юлія Ткачук", "Роман Марченко", "Світлана Гнатюк", "Андрій Поліщук", "Олена Кравець", "Василь Мороз",
        "Катерина Лук'яненко", "Дмитро Остапчук", "Галина Бондаренко", "Сергій Руденко",
    ];

    private static readonly string[] Streets =
    [
        "вул. Кленова", "вул. Яблунева", "вул. Каштанова", "вул. Липова", "вул. Вишнева", "вул. Березова",
        "просп. Умовний", "вул. Тиха", "вул. Річкова", "вул. Сонячна", "пров. Затишний", "вул. Гончарна",
    ];

    private static readonly string[] FailureReasons =
    [
        "Одержувач не відповідає на дзвінки", "Одержувача немає за адресою", "Одержувач відмовився від посилки",
    ];

    private readonly IClock _clock;
    private readonly PriceCalculator _calculator = new();
    private readonly DeadlineCalculator _deadlines = new();
    private readonly OrderWorkflow _workflow = new();

    /// <summary>
    /// Створює генератор.
    /// </summary>
    /// <param name="clock">Поточний час: дані будуються за останні 30 днів від нього.</param>
    public DemoOrderGenerator(IClock clock)
    {
        _clock = clock;
    }

    /// <summary>
    /// Додає демонстраційні замовлення, якщо таблиця замовлень порожня.
    /// </summary>
    /// <param name="context">Контекст БД з уже заповненими довідниками.</param>
    /// <param name="cancellationToken">Скасування операції.</param>
    /// <returns>Кількість доданих замовлень.</returns>
    public async Task<int> SeedAsync(AppDbContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (await context.Orders.AnyAsync(cancellationToken))
        {
            return 0;
        }

        var data = new ReferenceData(
            await context.Clients.Where(c => c.IsActive).OrderBy(c => c.Id).ToListAsync(cancellationToken),
            await context.Zones.Where(z => z.IsActive).OrderBy(z => z.Id).ToListAsync(cancellationToken),
            await context.Tariffs.Where(t => t.IsActive).OrderBy(t => t.BasePrice).ToListAsync(cancellationToken),
            await context.Couriers.Include(c => c.User).Where(c => c.IsActive && c.User.IsActive).OrderBy(c => c.Id)
                .ToListAsync(cancellationToken),
            await context.Users.Where(u => u.Role == UserRole.Operator && u.IsActive).OrderBy(u => u.Id)
                .FirstOrDefaultAsync(cancellationToken));
        if (data.Operator is null || data.Clients.Count == 0 || data.Zones.Count == 0 || data.Tariffs.Count == 0
            || data.Couriers.Count == 0)
        {
            return 0;
        }

        List<Order> orders = Generate(data);
        context.Orders.AddRange(orders);
        await context.SaveChangesAsync(cancellationToken);
        return orders.Count;
    }

    private List<Order> Generate(ReferenceData data)
    {
        var random = new Random(Seed);
        DateTime now = _clock.Now;
        var operatorSession = new UserSession(data.Operator!.Id, data.Operator.Login, data.Operator.FullName, UserRole.Operator, null);
        var orders = new List<Order>();
        List<(DateTime CreatedAt, OrderStatus StopAt)> openOrders = OpenOrderTimes(random, now);

        for (int daysAgo = HistoryDays - 1; daysAgo >= 0; daysAgo--)
        {
            DateTime day = now.Date.AddDays(-daysAgo);

            // Служба «росте»: ближче до сьогодні замовлень більше; null — звичайний сценарій
            int count = random.Next(1, 3) + (HistoryDays - daysAgo) / 10;
            var planned = Enumerable.Range(0, count)
                .Select(_ => (CreatedAt: day.AddHours(9).AddMinutes(random.Next(0, 11 * 60)), StopAt: (OrderStatus?)null))
                .Concat(openOrders.Where(o => o.CreatedAt.Date == day).Select(o => (o.CreatedAt, StopAt: (OrderStatus?)o.StopAt)))
                .Where(o => o.CreatedAt < now)
                .OrderBy(o => o.CreatedAt)
                .ToList();

            // Трек-номери дня зростають разом із часом оформлення (D-23)
            int sequence = 0;
            foreach ((DateTime createdAt, OrderStatus? stopAt) in planned)
            {
                Order order = CreateOrder(data, random, createdAt, operatorSession.UserId);
                order.TrackingNumber = TrackingNumber.Create(createdAt, ++sequence);
                int outcome = stopAt is null ? random.Next(100) : stopAt == OrderStatus.DeliveryFailed ? 15 : 50;
                Simulate(order, data, random, operatorSession, now, outcome, stopAt);
                orders.Add(order);
            }
        }

        return orders;
    }

    /// <summary>
    /// Свіжі замовлення, що зупиняються в робочих статусах: щоб на екранах оператора й кур'єрів
    /// були справи «в роботі» незалежно від часу запуску. Якщо зараз ранок, вони оформлені напередодні ввечері.
    /// </summary>
    private static List<(DateTime CreatedAt, OrderStatus StopAt)> OpenOrderTimes(Random random, DateTime now)
    {
        (OrderStatus Status, int MinAgo, int MaxAgo)[] plan =
        [
            (OrderStatus.New, 10, 60), (OrderStatus.New, 20, 90),
            (OrderStatus.Assigned, 60, 120), (OrderStatus.Assigned, 70, 140), (OrderStatus.Assigned, 80, 150),
            (OrderStatus.PickedUp, 150, 220), (OrderStatus.PickedUp, 160, 240), (OrderStatus.PickedUp, 170, 250),
            (OrderStatus.DeliveryFailed, 260, 330),
        ];

        DateTime anchor = now.TimeOfDay >= DeadlineCalculator.WorkdayStart + TimeSpan.FromHours(5)
            ? now
            : now.Date.AddDays(-1) + DeadlineCalculator.WorkdayEnd;
        return plan
            .Select(p => (anchor.AddMinutes(-random.Next(p.MinAgo, p.MaxAgo + 1)), p.Status))
            .ToList();
    }

    private Order CreateOrder(ReferenceData data, Random random, DateTime createdAt, int operatorId)
    {
        Client client = data.Clients[random.Next(data.Clients.Count)];
        // Забирають з типової адреси клієнта, тож і район — її (D-41); випадкове число береться
        // завжди, щоб решта демо-даних не змінювалася
        Zone randomZone = data.Zones[random.Next(data.Zones.Count)];
        Zone pickupZone = data.Zones.FirstOrDefault(z => z.Id == client.DefaultZoneId) ?? randomZone;
        Zone deliveryZone = data.Zones[random.Next(data.Zones.Count)];

        // Здебільшого Стандарт, рідше Експрес, зрідка Термінова (тарифи впорядковано за ціною)
        int roll = random.Next(100);
        Tariff tariff = data.Tariffs[Math.Min(data.Tariffs.Count - 1, roll < 60 ? 0 : roll < 90 ? 1 : 2)];

        int maxKg = Math.Max(1, Math.Min(8, tariff.MaxWeightGrams / 1000));
        int weightGrams = random.Next(2, maxKg * 10 + 1) * 100;
        (PaymentPayer payer, PaymentMethod method) = random.Next(100) switch
        {
            < 30 => (PaymentPayer.Sender, PaymentMethod.Cash),
            < 45 => (PaymentPayer.Sender, PaymentMethod.Card),
            < 60 => (PaymentPayer.Sender, PaymentMethod.BankTransfer),
            < 85 => (PaymentPayer.Recipient, PaymentMethod.Cash),
            _ => (PaymentPayer.Recipient, PaymentMethod.Card),
        };
        string recipientPhone = "+38050000" + random.Next(3000, 4000).ToString(System.Globalization.CultureInfo.InvariantCulture);

        var input = new OrderInput(
            client.Id,
            RecipientNames[random.Next(RecipientNames.Length)],
            recipientPhone,
            client.Address,
            pickupZone.Id,
            $"{Streets[random.Next(Streets.Length)]}, {random.Next(1, 120)}",
            deliveryZone.Id,
            tariff.Id,
            weightGrams,
            random.Next(10, 41),
            random.Next(10, 31),
            random.Next(5, 26),
            random.Next(100) < 25 ? Money.FromHryvnias(random.Next(5, 60) * 100) : Money.Zero,
            random.Next(100) < 15,
            payer,
            method,
            null);

        // Габарити до 40 × 30 × 25 см: об'ємна вага не більша за 7,5 кг, тож посилка вміщується в будь-який тариф.
        // Перевірка — та сама, що й для оператора
        new OrderValidator().Validate(input, tariff).ThrowIfInvalid();
        PriceBreakdown price = _calculator.Calculate(tariff, pickupZone, deliveryZone, input.Parcel);
        DateTime deadline = _deadlines.CalculateDeadline(createdAt, tariff.DeliveryHours);
        return OrderService.Compose(input, recipientPhone, client, pickupZone, deliveryZone, tariff, price, createdAt, deadline, operatorId);
    }

    /// <summary>
    /// Проводить замовлення через життєвий цикл; подія, час якої ще не настав, не відбувається.
    /// </summary>
    private void Simulate(
        Order order, ReferenceData data, Random random, UserSession operatorSession, DateTime now, int outcome, OrderStatus? stopAt)
    {
        DateTime time = order.CreatedAt;
        bool Step(int minMinutes, int maxMinutes)
        {
            time = DeadlineCalculator.FirstWorkingMoment(time.AddMinutes(random.Next(minMinutes, maxMinutes + 1)));
            return time <= now;
        }

        // stopAt — свіже замовлення «в роботі»: життєвий цикл зупиняється в цьому статусі
        if (stopAt == OrderStatus.New)
        {
            return;
        }

        if (outcome < 7)
        {
            // Скасоване до призначення кур'єра
            if (Step(10, 90))
            {
                _workflow.Execute(order, OrderAction.Cancel, operatorSession, time);
            }

            return;
        }

        if (order.Payment!.Payer == PaymentPayer.Sender && order.Payment.Method != PaymentMethod.Cash)
        {
            if (!Step(5, 30))
            {
                return;
            }

            _workflow.Execute(order, OrderAction.ConfirmPayment, operatorSession, time);
        }

        Courier courier = PickCourier(order, data, random);
        if (!Step(5, 40))
        {
            return;
        }

        _workflow.Execute(order, OrderAction.Assign, operatorSession, time, courier);
        if (stopAt == OrderStatus.Assigned)
        {
            return;
        }

        if (outcome < 10)
        {
            if (Step(10, 60))
            {
                _workflow.Execute(order, OrderAction.Cancel, operatorSession, time);
            }

            return;
        }

        UserSession courierSession = Session(courier);
        if (!Step(20, 90))
        {
            return;
        }

        _workflow.Execute(order, OrderAction.PickUp, courierSession, time);
        if (stopAt == OrderStatus.PickedUp)
        {
            return;
        }

        // Невдала спроба: далі повторна доставка іншим кур'єром або повернення відправнику
        if (outcome < 18)
        {
            if (!Step(30, 120))
            {
                return;
            }

            _workflow.Execute(order, OrderAction.ReportFailure, courierSession, time, reason: FailureReasons[random.Next(FailureReasons.Length)]);
            if (stopAt == OrderStatus.DeliveryFailed || !Step(30, 180))
            {
                return;
            }

            if (outcome < 13)
            {
                _workflow.Execute(order, OrderAction.Return, operatorSession, time);
                return;
            }

            courier = PickCourier(order, data, random);
            courierSession = Session(courier);
            _workflow.Execute(order, OrderAction.RetryDelivery, operatorSession, time, courier);
            if (!Step(20, 90))
            {
                return;
            }

            _workflow.Execute(order, OrderAction.PickUp, courierSession, time);
        }

        // Здебільшого вчасно; приблизно кожна восьма доставка — із запізненням (для контролю термінів)
        TimeSpan untilDeadline = order.Deadline - time;
        int maxMinutes = Math.Max(30, (int)untilDeadline.TotalMinutes - 10);
        bool late = random.Next(100) < 12;
        if (late ? !StepTo(order.Deadline.AddMinutes(random.Next(10, 150))) : !Step(20, Math.Min(maxMinutes, 240)))
        {
            return;
        }

        _workflow.Execute(order, OrderAction.Deliver, courierSession, time);

        bool StepTo(DateTime target)
        {
            time = DeadlineCalculator.FirstWorkingMoment(target > time ? target : time.AddMinutes(20));
            return time <= now;
        }
    }

    private static Courier PickCourier(Order order, ReferenceData data, Random random)
    {
        List<Courier> suitable = data.Couriers.Where(c => c.MaxWeightGrams >= order.ChargeableWeightGrams).ToList();
        IReadOnlyList<Courier> candidates = suitable.Count > 0 ? suitable : data.Couriers;
        return candidates[random.Next(candidates.Count)];
    }

    private static UserSession Session(Courier courier) =>
        new(courier.UserId, courier.User.Login, courier.User.FullName, UserRole.Courier, courier.Id);

    private sealed record ReferenceData(
        IReadOnlyList<Client> Clients,
        IReadOnlyList<Zone> Zones,
        IReadOnlyList<Tariff> Tariffs,
        IReadOnlyList<Courier> Couriers,
        User? Operator);
}
