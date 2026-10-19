using System.Globalization;

namespace CourierService.Core;

/// <summary>
/// Грошова сума в гривнях, що зберігається як ціла кількість копійок (рішення D-05).
/// </summary>
/// <remarks>
/// Інкапсуляція: копійки змінити не можна, усі операції повертають нову суму,
/// а округлення до копійки відбувається лише тут — за правилом «половина вгору»
/// (<see cref="MidpointRounding.AwayFromZero"/>).
/// </remarks>
/// <param name="Kopecks">Сума в копійках.</param>
public readonly record struct Money(long Kopecks) : IComparable<Money>
{
    private static readonly CultureInfo Ukrainian = CultureInfo.GetCultureInfo("uk-UA");

    /// <summary>Нульова сума.</summary>
    public static Money Zero => new(0);

    /// <summary>Сума в гривнях (з копійками в дробовій частині).</summary>
    public decimal Hryvnias => Kopecks / 100m;

    /// <summary>
    /// Створює суму з гривень; дробова частина округлюється до копійки.
    /// </summary>
    /// <param name="hryvnias">Сума в гривнях.</param>
    /// <returns>Грошова сума.</returns>
    public static Money FromHryvnias(decimal hryvnias) => new(RoundToKopecks(hryvnias * 100m));

    /// <summary>
    /// Множить суму на коефіцієнт з округленням до копійки.
    /// </summary>
    /// <param name="factor">Множник, наприклад 1,15 для коефіцієнта зони.</param>
    /// <returns>Нова сума.</returns>
    public Money MultiplyBy(decimal factor) => new(RoundToKopecks(Kopecks * factor));

    /// <summary>
    /// Обчислює відсоток від суми (115 % → ×1,15; 50 % → половина).
    /// </summary>
    /// <param name="percent">Відсоток.</param>
    /// <returns>Нова сума.</returns>
    public Money ApplyPercent(int percent) => MultiplyBy(percent / 100m);

    /// <summary>
    /// Обчислює проміле від суми (5 ‰ = 0,5 %).
    /// </summary>
    /// <param name="permille">Кількість проміле.</param>
    /// <returns>Нова сума.</returns>
    public Money ApplyPermille(int permille) => MultiplyBy(permille / 1000m);

    /// <summary>Сума двох грошових сум.</summary>
    /// <param name="left">Перший доданок.</param>
    /// <param name="right">Другий доданок.</param>
    /// <returns>Сума.</returns>
    /// <exception cref="OverflowException">Результат виходить за межі типу.</exception>
    public static Money operator +(Money left, Money right) => new(checked(left.Kopecks + right.Kopecks));

    /// <summary>Різниця двох грошових сум.</summary>
    /// <param name="left">Зменшуване.</param>
    /// <param name="right">Від'ємник.</param>
    /// <returns>Різниця.</returns>
    /// <exception cref="OverflowException">Результат виходить за межі типу.</exception>
    public static Money operator -(Money left, Money right) => new(checked(left.Kopecks - right.Kopecks));

    /// <summary>Порівняння сум.</summary>
    /// <param name="left">Перша сума.</param>
    /// <param name="right">Друга сума.</param>
    /// <returns><c>true</c>, якщо перша сума менша.</returns>
    public static bool operator <(Money left, Money right) => left.Kopecks < right.Kopecks;

    /// <summary>Порівняння сум.</summary>
    /// <param name="left">Перша сума.</param>
    /// <param name="right">Друга сума.</param>
    /// <returns><c>true</c>, якщо перша сума більша.</returns>
    public static bool operator >(Money left, Money right) => left.Kopecks > right.Kopecks;

    /// <summary>Порівняння сум.</summary>
    /// <param name="left">Перша сума.</param>
    /// <param name="right">Друга сума.</param>
    /// <returns><c>true</c>, якщо перша сума не більша.</returns>
    public static bool operator <=(Money left, Money right) => left.Kopecks <= right.Kopecks;

    /// <summary>Порівняння сум.</summary>
    /// <param name="left">Перша сума.</param>
    /// <param name="right">Друга сума.</param>
    /// <returns><c>true</c>, якщо перша сума не менша.</returns>
    public static bool operator >=(Money left, Money right) => left.Kopecks >= right.Kopecks;

    /// <inheritdoc />
    public int CompareTo(Money other) => Kopecks.CompareTo(other.Kopecks);

    /// <summary>
    /// Форматує суму за українськими правилами: «1 234,50 грн».
    /// </summary>
    /// <returns>Сума з двома знаками після коми й позначкою валюти.</returns>
    public override string ToString() => Hryvnias.ToString("N2", Ukrainian) + " грн";

    private static long RoundToKopecks(decimal kopecks) =>
        (long)Math.Round(kopecks, MidpointRounding.AwayFromZero);
}
