using CourierService.Core.Abstractions;
using CourierService.Core.Enums;

namespace CourierService.Core.Sla;

/// <summary>Стан дотримання терміну доставки (рішення D-31).</summary>
public enum SlaState
{
    /// <summary>Термін не оцінюється (скасовано, повернено).</summary>
    NotTracked = 0,

    /// <summary>Вчасно: доставлено до дедлайну або часу ще достатньо.</summary>
    OnTime = 1,

    /// <summary>Під загрозою: лишилося менше чверті терміну.</summary>
    AtRisk = 2,

    /// <summary>Прострочено: дедлайн минув або доставлено із запізненням.</summary>
    Overdue = 3,
}

/// <summary>
/// Результат оцінки терміну: стан (для кольору) і текст (щоб стан було видно не лише кольором).
/// </summary>
/// <param name="State">Стан.</param>
/// <param name="Text">Пояснення українською, наприклад «Під загрозою: лишилося 40 хв».</param>
public sealed record SlaResult(SlaState State, string Text)
{
    /// <summary>Термін не оцінюється.</summary>
    public static SlaResult NotTracked { get; } = new(SlaState.NotTracked, "—");

    /// <summary>
    /// Невдала спроба в історії кур'єра: доставку для нього завершено, далі вирішує оператор,
    /// тому залишок часу не показується.
    /// </summary>
    public static SlaResult AwaitingOperator { get; } = new(SlaState.NotTracked, "Очікує рішення оператора");
}

/// <summary>
/// Оцінка дотримання терміну доставки (рішення D-31).
/// </summary>
public interface ISlaEvaluator
{
    /// <summary>
    /// Оцінює термін замовлення на поточний момент.
    /// </summary>
    /// <param name="status">Статус замовлення.</param>
    /// <param name="createdAt">Час оформлення.</param>
    /// <param name="deadline">Дедлайн.</param>
    /// <param name="deliveredAt">Час доставки (для доставлених).</param>
    /// <returns>Стан і пояснення.</returns>
    SlaResult Evaluate(OrderStatus status, DateTime createdAt, DateTime deadline, DateTime? deliveredAt);
}

/// <summary>
/// Реалізація <see cref="ISlaEvaluator"/>: залишок і запізнення рахуються в робочих годинах служби (D-14),
/// поточний час — через <see cref="IClock"/>, тому правило перевіряється тестами з фіксованим часом.
/// </summary>
public sealed class SlaEvaluator : ISlaEvaluator
{
    /// <summary>Частка терміну (у відсотках), менше якої лишилося — «під загрозою».</summary>
    public const int AtRiskPercent = 25;

    // Запізнення рахується лише в робочих годинах (D-31): без уточнення «22:25 при дедлайні 20:45 → 14 хв»
    // виглядало б як помилка
    private const string WorkingTimeSuffix = "робочого часу";

    private readonly IClock _clock;

    /// <summary>
    /// Створює оцінювач.
    /// </summary>
    /// <param name="clock">Поточний час.</param>
    public SlaEvaluator(IClock clock)
    {
        _clock = clock;
    }

    /// <summary>
    /// Чи доставлено вчасно (для звітів): доставлено не пізніше дедлайну.
    /// </summary>
    /// <param name="deadline">Дедлайн.</param>
    /// <param name="deliveredAt">Час доставки.</param>
    /// <returns><c>true</c>, якщо вчасно.</returns>
    public static bool IsDeliveredOnTime(DateTime deadline, DateTime deliveredAt) => deliveredAt <= deadline;

    /// <inheritdoc />
    public SlaResult Evaluate(OrderStatus status, DateTime createdAt, DateTime deadline, DateTime? deliveredAt)
    {
        switch (status)
        {
            case OrderStatus.Cancelled:
            case OrderStatus.Returned:
                return SlaResult.NotTracked;
            case OrderStatus.Delivered when deliveredAt is { } delivered:
                return IsDeliveredOnTime(deadline, delivered)
                    ? new SlaResult(SlaState.OnTime, "Доставлено вчасно")
                    : new SlaResult(SlaState.Overdue,
                        $"Доставлено із запізненням {FormatDuration(DeadlineCalculator.WorkingTimeBetween(deadline, delivered))} {WorkingTimeSuffix}");
        }

        DateTime now = _clock.Now;
        if (now > deadline)
        {
            return new SlaResult(SlaState.Overdue,
                $"Прострочено на {FormatDuration(DeadlineCalculator.WorkingTimeBetween(deadline, now))} {WorkingTimeSuffix}");
        }

        TimeSpan remaining = DeadlineCalculator.WorkingTimeBetween(now, deadline);
        TimeSpan term = DeadlineCalculator.WorkingTimeBetween(createdAt, deadline);
        string left = $"лишилося {FormatDuration(remaining)}";
        return remaining.Ticks * 100 < term.Ticks * AtRiskPercent
            ? new SlaResult(SlaState.AtRisk, $"Під загрозою: {left}")
            : new SlaResult(SlaState.OnTime, $"Вчасно: {left}");
    }

    /// <summary>
    /// «2 год 15 хв», «40 хв», «3 год»; менше хвилини — «менше хвилини».
    /// </summary>
    /// <param name="duration">Тривалість.</param>
    /// <returns>Текст українською.</returns>
    public static string FormatDuration(TimeSpan duration)
    {
        int totalMinutes = (int)duration.TotalMinutes;
        if (totalMinutes < 1)
        {
            return "менше хвилини";
        }

        int hours = totalMinutes / 60;
        int minutes = totalMinutes % 60;
        return hours == 0 ? $"{minutes} хв" : minutes == 0 ? $"{hours} год" : $"{hours} год {minutes} хв";
    }
}
