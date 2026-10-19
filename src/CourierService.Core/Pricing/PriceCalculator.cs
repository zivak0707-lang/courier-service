using CourierService.Core.Entities;

namespace CourierService.Core.Pricing;

/// <summary>
/// Параметри посилки, від яких залежить вартість.
/// </summary>
/// <param name="WeightGrams">Фактична вага, г.</param>
/// <param name="LengthCm">Довжина, см.</param>
/// <param name="WidthCm">Ширина, см.</param>
/// <param name="HeightCm">Висота, см.</param>
/// <param name="DeclaredValue">Оголошена цінність.</param>
/// <param name="IsFragile">Чи крихка посилка.</param>
public sealed record ParcelParameters(
    int WeightGrams,
    int LengthCm,
    int WidthCm,
    int HeightCm,
    Money DeclaredValue,
    bool IsFragile);

/// <summary>
/// Результат розрахунку: проміжні величини й складові вартості.
/// Підсумок — сума складових, тому розбивка завжди сходиться з ним.
/// </summary>
/// <param name="VolumetricWeightGrams">Об'ємна вага, г.</param>
/// <param name="ChargeableWeightGrams">Розрахункова вага — більша з фактичної та об'ємної, г.</param>
/// <param name="ExtraKilograms">Кількість розпочатих кілограмів понад включену вагу.</param>
/// <param name="ZoneCoefficientPercent">Застосований коефіцієнт зони, %.</param>
/// <param name="CrossesRiver">Чи забір і доставка на різних берегах Дніпра.</param>
/// <param name="BaseCost">Базова вартість з коефіцієнтом зони.</param>
/// <param name="WeightCost">Вартість додаткових кілограмів з коефіцієнтом зони.</param>
/// <param name="CrossRiverCost">Доплата за переїзд через Дніпро.</param>
/// <param name="InsuranceCost">Страхування оголошеної цінності.</param>
/// <param name="FragileCost">Доплата за крихкість.</param>
public sealed record PriceBreakdown(
    int VolumetricWeightGrams,
    int ChargeableWeightGrams,
    int ExtraKilograms,
    int ZoneCoefficientPercent,
    bool CrossesRiver,
    Money BaseCost,
    Money WeightCost,
    Money CrossRiverCost,
    Money InsuranceCost,
    Money FragileCost)
{
    /// <summary>Підсумкова вартість доставки.</summary>
    public Money Total => BaseCost + WeightCost + CrossRiverCost + InsuranceCost + FragileCost;
}

/// <summary>
/// Розрахунок вартості доставки за параметрами тарифу й зон (рішення D-06).
/// </summary>
public interface IPriceCalculator
{
    /// <summary>
    /// Обчислює вартість доставки з розбивкою.
    /// </summary>
    /// <param name="tariff">Тариф.</param>
    /// <param name="pickupZone">Зона забору.</param>
    /// <param name="deliveryZone">Зона доставки.</param>
    /// <param name="parcel">Параметри посилки (вже перевірені).</param>
    /// <returns>Розбивка й підсумок.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Посилка порушує обмеження тарифу або служби.</exception>
    PriceBreakdown Calculate(Tariff tariff, Zone pickupZone, Zone deliveryZone, ParcelParameters parcel);
}

/// <summary>
/// Параметрична формула вартості (рішення D-06). Чиста функція: не звертається до БД
/// й не залежить від часу, тому перевіряється тестами на будь-яких даних.
/// </summary>
/// <remarks>
/// Обмеження посилки (сторона, вага тарифу) перевіряє валідатор замовлення з повідомленнями
/// біля полів; калькулятор лише захищається від некоректного виклику.
/// </remarks>
public sealed class PriceCalculator : IPriceCalculator
{
    /// <summary>Найбільша сторона посилки, яку приймає служба, см.</summary>
    public const int MaxSideCm = 120;

    /// <summary>Об'ємний дільник: Д × Ш × В (см) / 4 = грами (еквівалент «/4000» у кг).</summary>
    public const int VolumetricDivisor = 4;

    private const int GramsPerKilogram = 1000;

    /// <summary>
    /// Об'ємна вага посилки, округлена вгору до грама.
    /// </summary>
    /// <param name="lengthCm">Довжина, см.</param>
    /// <param name="widthCm">Ширина, см.</param>
    /// <param name="heightCm">Висота, см.</param>
    /// <returns>Об'ємна вага, г.</returns>
    public static int VolumetricWeightGrams(int lengthCm, int widthCm, int heightCm)
    {
        long volume = (long)lengthCm * widthCm * heightCm;
        return checked((int)((volume + VolumetricDivisor - 1) / VolumetricDivisor));
    }

    /// <summary>
    /// Розрахункова вага — більша з фактичної та об'ємної.
    /// </summary>
    /// <param name="parcel">Параметри посилки.</param>
    /// <returns>Розрахункова вага, г.</returns>
    public static int ChargeableWeightGrams(ParcelParameters parcel)
    {
        ArgumentNullException.ThrowIfNull(parcel);
        return Math.Max(parcel.WeightGrams,
            VolumetricWeightGrams(parcel.LengthCm, parcel.WidthCm, parcel.HeightCm));
    }

    /// <inheritdoc />
    public PriceBreakdown Calculate(Tariff tariff, Zone pickupZone, Zone deliveryZone, ParcelParameters parcel)
    {
        ArgumentNullException.ThrowIfNull(tariff);
        ArgumentNullException.ThrowIfNull(pickupZone);
        ArgumentNullException.ThrowIfNull(deliveryZone);
        ArgumentNullException.ThrowIfNull(parcel);
        EnsureParcelAccepted(tariff, parcel);

        // 1–2. Об'ємна й розрахункова вага
        int volumetric = VolumetricWeightGrams(parcel.LengthCm, parcel.WidthCm, parcel.HeightCm);
        int chargeable = Math.Max(parcel.WeightGrams, volumetric);

        // 3. Кожен розпочатий кілограм понад включену вагу
        int overGrams = Math.Max(0, chargeable - tariff.IncludedWeightGrams);
        int extraKilograms = (overGrams + GramsPerKilogram - 1) / GramsPerKilogram;

        // 4. Ціну визначає найвіддаленіша від центру точка маршруту
        int coefficient = Math.Max(pickupZone.CoefficientPercent, deliveryZone.CoefficientPercent);

        // 5–8. Складові; кожна округлюється до копійки окремо
        Money baseCost = tariff.BasePrice.ApplyPercent(coefficient);
        Money weightCost = tariff.PricePerExtraKg.MultiplyBy(extraKilograms).ApplyPercent(coefficient);

        bool crossesRiver = pickupZone.Bank != deliveryZone.Bank;
        Money crossRiverCost = crossesRiver ? tariff.CrossRiverSurcharge : Money.Zero;

        Money insuredPart = parcel.DeclaredValue > tariff.InsuranceFreeLimit
            ? parcel.DeclaredValue - tariff.InsuranceFreeLimit
            : Money.Zero;
        Money insuranceCost = insuredPart.ApplyPermille(tariff.InsurancePermille);

        Money fragileCost = parcel.IsFragile ? tariff.FragileSurcharge : Money.Zero;

        return new PriceBreakdown(
            volumetric, chargeable, extraKilograms, coefficient, crossesRiver,
            baseCost, weightCost, crossRiverCost, insuranceCost, fragileCost);
    }

    private static void EnsureParcelAccepted(Tariff tariff, ParcelParameters parcel)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(parcel.WeightGrams, nameof(parcel));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(parcel.LengthCm, nameof(parcel));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(parcel.WidthCm, nameof(parcel));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(parcel.HeightCm, nameof(parcel));

        if (parcel.LengthCm > MaxSideCm || parcel.WidthCm > MaxSideCm || parcel.HeightCm > MaxSideCm)
        {
            throw new ArgumentOutOfRangeException(nameof(parcel), $"Сторона посилки більша за {MaxSideCm} см.");
        }

        if (parcel.DeclaredValue < Money.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(parcel), "Оголошена цінність від'ємна.");
        }

        if (ChargeableWeightGrams(parcel) > tariff.MaxWeightGrams)
        {
            throw new ArgumentOutOfRangeException(nameof(parcel), "Розрахункова вага більша за максимум тарифу.");
        }
    }
}
