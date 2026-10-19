namespace CourierService.Core.Abstractions;

/// <summary>
/// Джерело поточного часу.
/// </summary>
/// <remarks>
/// Код не звертається до <see cref="DateTime.Now"/> напряму: у тестах час підміняється фіксованим,
/// тому правила термінів доставки перевіряються однозначно.
/// </remarks>
public interface IClock
{
    /// <summary>Поточні місцеві дата й час.</summary>
    DateTime Now { get; }
}

/// <summary>
/// Системний годинник: повертає реальний місцевий час комп'ютера.
/// </summary>
public sealed class SystemClock : IClock
{
    /// <inheritdoc />
    public DateTime Now => DateTime.Now;
}
