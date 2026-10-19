using CourierService.Core;
using CourierService.Core.Entities;
using CourierService.Core.Enums;
using CourierService.Core.Pricing;
using CourierService.Data.Seed;

namespace CourierService.Tests.Core;

/// <summary>
/// Калькулятор перевіряється на справжніх тарифах і зонах із початкових даних (D-07, D-13).
/// </summary>
public class PriceCalculatorTests
{
    private static readonly IReadOnlyList<Tariff> Tariffs = DatabaseSeeder.CreateTariffs();
    private static readonly IReadOnlyList<Zone> Zones = DatabaseSeeder.CreateKyivZones();

    private readonly PriceCalculator _calculator = new();

    private static Tariff Standard => Tariffs.Single(t => t.Name == "Стандарт");

    private static Tariff Urgent => Tariffs.Single(t => t.Name == "Термінова");

    private static Zone ZoneNamed(string name) => Zones.Single(z => z.Name == name);

    private static ParcelParameters Parcel(
        int weightGrams = 1_500, int length = 20, int width = 15, int height = 10,
        decimal declaredHryvnias = 0, bool fragile = false) =>
        new(weightGrams, length, width, height, Money.FromHryvnias(declaredHryvnias), fragile);

    private PriceBreakdown Calculate(ParcelParameters parcel, string from = "Шевченківський", string to = "Печерський",
        Tariff? tariff = null) =>
        _calculator.Calculate(tariff ?? Standard, ZoneNamed(from), ZoneNamed(to), parcel);

    [Fact]
    public void ExampleFromPlan_Standard_ShevchenkivskyiToDarnytskyi_Is157()
    {
        // 110 × 1,20 = 132 грн + 25 грн за переїзд через Дніпро
        PriceBreakdown price = Calculate(Parcel(weightGrams: 1_500), to: "Дарницький");

        Assert.Equal(Money.FromHryvnias(132m), price.BaseCost);
        Assert.Equal(Money.FromHryvnias(25m), price.CrossRiverCost);
        Assert.Equal(Money.FromHryvnias(157m), price.Total);
        Assert.True(price.CrossesRiver);
        Assert.Equal(120, price.ZoneCoefficientPercent);
    }

    [Fact]
    public void MinimalParcel_CostsBasePriceOnly()
    {
        PriceBreakdown price = Calculate(Parcel(weightGrams: 1, length: 1, width: 1, height: 1));

        Assert.Equal(1, price.ChargeableWeightGrams);
        Assert.Equal(0, price.ExtraKilograms);
        Assert.Equal(Money.FromHryvnias(110m), price.Total);
    }

    [Theory]
    [InlineData(2_000, 0, 110)]    // рівно на межі включеної ваги
    [InlineData(2_001, 1, 122)]    // межа + 1 г — уже розпочатий кілограм
    [InlineData(3_000, 1, 122)]
    [InlineData(3_001, 2, 134)]
    [InlineData(30_000, 28, 446)]  // максимум тарифу: 110 + 28 × 12
    public void ExtraWeight_ChargesEveryStartedKilogram(int weightGrams, int expectedExtraKg, int expectedTotal)
    {
        PriceBreakdown price = Calculate(Parcel(weightGrams: weightGrams));

        Assert.Equal(expectedExtraKg, price.ExtraKilograms);
        Assert.Equal(Money.FromHryvnias(expectedTotal), price.Total);
    }

    [Fact]
    public void WeightAboveTariffMaximum_IsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Calculate(Parcel(weightGrams: 30_001)));
        Assert.Throws<ArgumentOutOfRangeException>(() => Calculate(Parcel(weightGrams: 10_001), tariff: Urgent));
    }

    [Fact]
    public void VolumetricWeight_UsedWhenGreaterThanActual()
    {
        // 40 × 30 × 20 / 4 = 6000 г — легка, але об'ємна посилка
        PriceBreakdown price = Calculate(Parcel(weightGrams: 1_000, length: 40, width: 30, height: 20));

        Assert.Equal(6_000, price.VolumetricWeightGrams);
        Assert.Equal(6_000, price.ChargeableWeightGrams);
        Assert.Equal(4, price.ExtraKilograms);
        Assert.Equal(Money.FromHryvnias(110m + 4 * 12m), price.Total);
    }

    [Fact]
    public void ActualWeight_UsedWhenGreaterThanVolumetric()
    {
        PriceBreakdown price = Calculate(Parcel(weightGrams: 5_000, length: 10, width: 10, height: 10));

        Assert.Equal(250, price.VolumetricWeightGrams);
        Assert.Equal(5_000, price.ChargeableWeightGrams);
    }

    [Theory]
    [InlineData(1, 1, 1, 1)]          // 0,25 г округлюється вгору до грама
    [InlineData(10, 10, 10, 250)]
    [InlineData(11, 11, 11, 333)]     // 332,75 → 333
    [InlineData(120, 120, 120, 432_000)]
    public void VolumetricWeight_RoundsUpToGram(int length, int width, int height, int expected)
    {
        Assert.Equal(expected, PriceCalculator.VolumetricWeightGrams(length, width, height));
    }

    [Fact]
    public void SideOf120cm_IsAccepted_121cm_IsRejected()
    {
        // 120 × 10 × 10 / 4 = 3000 г — у межах тарифу
        PriceBreakdown price = Calculate(Parcel(weightGrams: 1_000, length: 120, width: 10, height: 10));
        Assert.Equal(3_000, price.ChargeableWeightGrams);

        Assert.Throws<ArgumentOutOfRangeException>(() => Calculate(Parcel(length: 121, width: 10, height: 10)));
        Assert.Throws<ArgumentOutOfRangeException>(() => Calculate(Parcel(length: 10, width: 121, height: 10)));
        Assert.Throws<ArgumentOutOfRangeException>(() => Calculate(Parcel(length: 10, width: 10, height: 121)));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(500, 0)]        // перші 500 грн не тарифікуються
    [InlineData(500.01, 0)]     // 0,01 грн × 5 ‰ = 0,005 коп. → 0
    [InlineData(600, 0.50)]     // 100 грн × 0,5 %
    [InlineData(1000, 2.50)]
    [InlineData(10000, 47.50)]
    public void Insurance_ChargedOnlyAboveFreeLimit(double declared, double expectedInsurance)
    {
        PriceBreakdown price = Calculate(Parcel(declaredHryvnias: (decimal)declared));

        Assert.Equal(Money.FromHryvnias((decimal)expectedInsurance), price.InsuranceCost);
    }

    [Fact]
    public void Fragile_AddsTariffSurcharge()
    {
        Assert.Equal(Money.Zero, Calculate(Parcel(fragile: false)).FragileCost);
        Assert.Equal(Money.FromHryvnias(30m), Calculate(Parcel(fragile: true)).FragileCost);
        Assert.Equal(Money.FromHryvnias(50m), Calculate(Parcel(fragile: true), tariff: Urgent).FragileCost);
    }

    [Theory]
    [InlineData("Шевченківський", "Святошинський", 120)]
    [InlineData("Святошинський", "Шевченківський", 120)]   // напрямок не важить
    [InlineData("Подільський", "Голосіївський", 115)]
    [InlineData("Печерський", "Печерський", 100)]
    public void ZoneCoefficient_IsMaximumOfRoute(string from, string to, int expectedPercent)
    {
        PriceBreakdown price = Calculate(Parcel(weightGrams: 3_000), from, to);

        Assert.Equal(expectedPercent, price.ZoneCoefficientPercent);
        Assert.Equal(Money.FromHryvnias(110m).ApplyPercent(expectedPercent), price.BaseCost);
        Assert.Equal(Money.FromHryvnias(12m).ApplyPercent(expectedPercent), price.WeightCost);
    }

    [Theory]
    [InlineData("Шевченківський", "Оболонський", false)]   // обидві на правому березі
    [InlineData("Дарницький", "Деснянський", false)]       // обидві на лівому березі
    [InlineData("Дніпровський", "Подільський", true)]
    [InlineData("Голосіївський", "Дарницький", true)]
    public void CrossRiverSurcharge_OnlyWhenBanksDiffer(string from, string to, bool expected)
    {
        PriceBreakdown price = Calculate(Parcel(), from, to);

        Assert.Equal(expected, price.CrossesRiver);
        Assert.Equal(expected ? Money.FromHryvnias(25m) : Money.Zero, price.CrossRiverCost);
    }

    [Fact]
    public void AllComponents_Combined()
    {
        // Експрес, 4,2 кг, Деснянський (1,25) → Шевченківський, крихка, цінність 2000 грн:
        // база 150 × 1,25 = 187,50; вага 3 кг × 15 × 1,25 = 56,25; переїзд 25; страхування 1500 × 0,5 % = 7,50; крихкість 40
        Tariff express = Tariffs.Single(t => t.Name == "Експрес");
        PriceBreakdown price = Calculate(Parcel(weightGrams: 4_200, declaredHryvnias: 2_000, fragile: true),
            "Деснянський", "Шевченківський", express);

        Assert.Equal(Money.FromHryvnias(187.50m), price.BaseCost);
        Assert.Equal(Money.FromHryvnias(56.25m), price.WeightCost);
        Assert.Equal(Money.FromHryvnias(25m), price.CrossRiverCost);
        Assert.Equal(Money.FromHryvnias(7.50m), price.InsuranceCost);
        Assert.Equal(Money.FromHryvnias(40m), price.FragileCost);
        Assert.Equal(Money.FromHryvnias(316.25m), price.Total);
    }

    [Fact]
    public void EachComponent_IsRoundedSeparately_AndTotalIsTheirSum()
    {
        // Ціни по 1 коп. і коефіцієнт 1,50: кожна складова 1,5 коп. → 2 коп., підсумок 4 коп. (а не 3)
        var tariff = new Tariff
        {
            BasePrice = new Money(1),
            IncludedWeightGrams = 0,
            PricePerExtraKg = new Money(1),
            MaxWeightGrams = 10_000,
        };
        var zone = new Zone { Bank = RiverBank.Right, CoefficientPercent = 150 };

        PriceBreakdown price = _calculator.Calculate(tariff, zone, zone, Parcel(weightGrams: 1_000, length: 1, width: 1, height: 1));

        Assert.Equal(new Money(2), price.BaseCost);
        Assert.Equal(new Money(2), price.WeightCost);
        Assert.Equal(new Money(4), price.Total);
    }

    [Theory]
    [InlineData(0, 10, 10, 10)]
    [InlineData(-1, 10, 10, 10)]
    [InlineData(1_000, 0, 10, 10)]
    [InlineData(1_000, 10, -5, 10)]
    [InlineData(1_000, 10, 10, 0)]
    public void NonPositiveWeightOrDimensions_AreRejected(int weight, int length, int width, int height)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Calculate(Parcel(weight, length, width, height)));
    }

    [Fact]
    public void NegativeDeclaredValue_IsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Calculate(Parcel(declaredHryvnias: -1)));
    }
}
