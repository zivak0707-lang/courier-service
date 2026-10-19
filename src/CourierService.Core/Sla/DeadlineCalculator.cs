namespace CourierService.Core.Sla;

/// <summary>
/// Розрахунок крайнього терміну доставки (рішення D-14).
/// </summary>
public interface IDeadlineCalculator
{
    /// <summary>
    /// Обчислює дедлайн: час створення + термін тарифу в робочих годинах служби.
    /// </summary>
    /// <param name="createdAt">Час оформлення замовлення.</param>
    /// <param name="deliveryHours">Термін доставки тарифу в робочих годинах.</param>
    /// <returns>Крайній термін доставки.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Термін не додатний.</exception>
    DateTime CalculateDeadline(DateTime createdAt, int deliveryHours);
}

/// <summary>
/// Дедлайн у робочих годинах: служба працює щодня з 09:00 до 21:00, тож години поза
/// цим проміжком не лічаться. Замовлення, оформлене поза робочим часом, починає відлік
/// із 09:00 найближчого робочого дня.
/// </summary>
/// <remarks>
/// Приклад: Експрес (4 год) о 20:00 → 1 год сьогодні + 3 год завтра → 12:00 наступного дня.
/// Час рахується за годинником на стіні (без переходу на літній час): уночі, коли
/// годинник переводять, служба однаково не працює.
/// </remarks>
public sealed class DeadlineCalculator : IDeadlineCalculator
{
    /// <summary>Початок робочого дня служби.</summary>
    public static readonly TimeSpan WorkdayStart = TimeSpan.FromHours(9);

    /// <summary>Кінець робочого дня служби.</summary>
    public static readonly TimeSpan WorkdayEnd = TimeSpan.FromHours(21);

    /// <inheritdoc />
    public DateTime CalculateDeadline(DateTime createdAt, int deliveryHours)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(deliveryHours);

        DateTime current = FirstWorkingMoment(createdAt);
        TimeSpan remaining = TimeSpan.FromHours(deliveryHours);

        while (true)
        {
            TimeSpan leftToday = current.Date + WorkdayEnd - current;
            if (remaining <= leftToday)
            {
                return current + remaining;
            }

            remaining -= leftToday;
            current = current.Date.AddDays(1) + WorkdayStart;
        }
    }

    /// <summary>
    /// Найближчий момент робочого часу, починаючи з заданого.
    /// </summary>
    /// <param name="moment">Довільний момент.</param>
    /// <returns>Сам момент, якщо він у робочому часі, інакше 09:00 сьогодні або завтра.</returns>
    public static DateTime FirstWorkingMoment(DateTime moment)
    {
        if (moment.TimeOfDay < WorkdayStart)
        {
            return moment.Date + WorkdayStart;
        }

        // Рівно о 21:00 служба вже не працює
        return moment.TimeOfDay >= WorkdayEnd ? moment.Date.AddDays(1) + WorkdayStart : moment;
    }
}
