using CourierService.Core.Entities;
using CourierService.Core.Validation;

namespace CourierService.Core.Directories;

/// <summary>
/// Дані форми тарифу (рішення D-06, D-13).
/// </summary>
/// <param name="Id">Ідентифікатор або <c>null</c> для нового тарифу.</param>
/// <param name="Name">Назва.</param>
/// <param name="BasePrice">Базова ціна.</param>
/// <param name="IncludedWeightGrams">Вага, включена в базову ціну, г.</param>
/// <param name="PricePerExtraKg">Ціна кожного додаткового кілограма.</param>
/// <param name="MaxWeightGrams">Максимальна вага, г.</param>
/// <param name="DeliveryHours">Термін у робочих годинах.</param>
/// <param name="CrossRiverSurcharge">Доплата за переїзд через Дніпро.</param>
/// <param name="InsuranceFreeLimit">Цінність без страхової комісії.</param>
/// <param name="InsurancePermille">Ставка страхування, ‰.</param>
/// <param name="FragileSurcharge">Доплата за крихкість.</param>
public sealed record TariffInput(
    int? Id,
    string Name,
    Money BasePrice,
    int IncludedWeightGrams,
    Money PricePerExtraKg,
    int MaxWeightGrams,
    int DeliveryHours,
    Money CrossRiverSurcharge,
    Money InsuranceFreeLimit,
    int InsurancePermille,
    Money FragileSurcharge) : IDirectoryInput;

/// <summary>
/// Правила тарифу (узгоджені з обмеженнями CHECK у БД).
/// </summary>
public sealed class TariffValidator : IValidator<TariffInput>
{
    /// <summary>Максимальна довжина назви.</summary>
    public const int NameMaxLength = 50;

    /// <summary>Найбільша вага посилки, яку приймає служба, г.</summary>
    public const int MaxParcelWeightGrams = 100_000;

    private static readonly Money MaxPrice = Money.FromHryvnias(100_000m);
    private static readonly Money MaxSurcharge = Money.FromHryvnias(10_000m);
    private static readonly Money MaxInsuranceFreeLimit = Money.FromHryvnias(1_000_000m);

    /// <inheritdoc />
    public ValidationErrors Validate(TariffInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var errors = new ValidationErrors();

        errors.RequiredText(nameof(input.Name), input.Name, "Назва", NameMaxLength);
        errors.MoneyInRange(nameof(input.BasePrice), input.BasePrice, new Money(1), MaxPrice, "Базова ціна");
        errors.MoneyInRange(nameof(input.PricePerExtraKg), input.PricePerExtraKg, Money.Zero, MaxSurcharge, "Ціна за додатковий кг");
        errors.InRange(nameof(input.MaxWeightGrams), input.MaxWeightGrams, 1, MaxParcelWeightGrams,
            "Максимальна вага має бути від 0,001 до 100 кг.");
        errors.InRange(nameof(input.IncludedWeightGrams), input.IncludedWeightGrams, 0, MaxParcelWeightGrams,
            "Включена вага має бути від 0 до 100 кг.");
        if (input.IncludedWeightGrams >= input.MaxWeightGrams && input.MaxWeightGrams > 0)
        {
            errors.Add(nameof(input.IncludedWeightGrams), "Включена вага має бути меншою за максимальну.");
        }

        errors.InRange(nameof(input.DeliveryHours), input.DeliveryHours, 1, 72,
            "Термін доставки має бути від 1 до 72 робочих годин.");
        errors.MoneyInRange(nameof(input.CrossRiverSurcharge), input.CrossRiverSurcharge, Money.Zero, MaxSurcharge,
            "Доплата за переїзд через Дніпро");
        errors.MoneyInRange(nameof(input.InsuranceFreeLimit), input.InsuranceFreeLimit, Money.Zero, MaxInsuranceFreeLimit,
            "Цінність без страхування");
        errors.InRange(nameof(input.InsurancePermille), input.InsurancePermille, 0, 1000,
            "Ставка страхування має бути від 0 до 1000 ‰.");
        errors.MoneyInRange(nameof(input.FragileSurcharge), input.FragileSurcharge, Money.Zero, MaxSurcharge,
            "Доплата за крихкість");

        return errors;
    }
}

/// <summary>
/// Довідник тарифів. Змінювати може лише адміністратор; зміни діють тільки для нових замовлень.
/// </summary>
public interface ITariffService : IDirectoryService<Tariff, TariffInput>
{
}
