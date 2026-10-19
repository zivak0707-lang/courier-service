using CourierService.Core.Assignment;
using CourierService.Core.Enums;

namespace CourierService.Tests.Core;

public class CourierAssignmentTests
{
    // Зони: 1 — Шевченківський (правий), 9 — Дарницький (лівий), 10 — Деснянський (лівий)
    private const int Shevchenkivskyi = 1;
    private const int Darnytskyi = 9;
    private const int Desnianskyi = 10;

    private readonly CourierAssignment _assignment = new();

    private static CourierCandidate Candidate(
        int id, string name, int zoneId, int active = 0, int maxKg = 30, bool isActive = true) => new(
        id, name, zoneId, zoneId switch { Shevchenkivskyi => "Шевченківський", Darnytskyi => "Дарницький", _ => "Деснянський" },
        zoneId == Shevchenkivskyi ? RiverBank.Right : RiverBank.Left, maxKg * 1000, active, isActive);

    private static AssignmentRequest PickupIn(int zoneId, int kg = 2) =>
        new(zoneId, zoneId == Shevchenkivskyi ? RiverBank.Right : RiverBank.Left, kg * 1000);

    [Fact]
    public void SameZone_WinsEvenWithMoreDeliveries()
    {
        var suggestion = _assignment.Suggest(PickupIn(Darnytskyi),
        [
            Candidate(1, "Андрій Ткаченко", Desnianskyi, active: 0),
            Candidate(2, "Марія Коваль", Darnytskyi, active: 3),
            Candidate(3, "Олег Савчук", Shevchenkivskyi, active: 0),
        ]);

        Assert.Equal(2, suggestion.Courier?.CourierId);
        Assert.Equal("Марія Коваль: та сама зона (Дарницький), 3 активні доставки.", suggestion.Explanation);
    }

    [Fact]
    public void SameBank_PreferredOverOtherBank()
    {
        var suggestion = _assignment.Suggest(PickupIn(Darnytskyi),
        [
            Candidate(3, "Олег Савчук", Shevchenkivskyi, active: 0),
            Candidate(1, "Андрій Ткаченко", Desnianskyi, active: 5),
        ]);

        Assert.Equal(1, suggestion.Courier?.CourierId);
        Assert.Contains("той самий берег (лівий)", suggestion.Explanation);
    }

    [Fact]
    public void OtherBank_ChosenWhenNobodyOnPickupBank()
    {
        var suggestion = _assignment.Suggest(PickupIn(Darnytskyi), [Candidate(3, "Олег Савчук", Shevchenkivskyi, active: 1)]);

        Assert.Equal(3, suggestion.Courier?.CourierId);
        Assert.Equal("Олег Савчук: на березі забору вільних кур'єрів немає, 1 активна доставка.", suggestion.Explanation);
    }

    [Fact]
    public void AmongEqual_FewerDeliveriesThenNameDecide()
    {
        var fewer = _assignment.Suggest(PickupIn(Shevchenkivskyi),
        [
            Candidate(1, "Андрій Ткаченко", Shevchenkivskyi, active: 2),
            Candidate(2, "Марія Коваль", Shevchenkivskyi, active: 1),
        ]);
        var byName = _assignment.Suggest(PickupIn(Shevchenkivskyi),
        [
            Candidate(2, "Марія Коваль", Shevchenkivskyi, active: 1),
            Candidate(1, "Андрій Ткаченко", Shevchenkivskyi, active: 1),
        ]);

        Assert.Equal(2, fewer.Courier?.CourierId);
        Assert.Equal(1, byName.Courier?.CourierId);
    }

    [Fact]
    public void InactiveAndTooWeakCouriers_AreSkipped()
    {
        var suggestion = _assignment.Suggest(PickupIn(Shevchenkivskyi, kg: 12),
        [
            Candidate(1, "Андрій Ткаченко", Shevchenkivskyi, isActive: false),
            Candidate(2, "Марія Коваль", Shevchenkivskyi, maxKg: 10),
            Candidate(3, "Олег Савчук", Darnytskyi, maxKg: 30),
        ]);

        Assert.Equal(3, suggestion.Courier?.CourierId);
    }

    [Theory]
    [InlineData(7, true)]
    [InlineData(8, false)]    // ліміт активних доставок
    public void CourierAtLimit_IsNotSuggested(int active, bool suggested)
    {
        var suggestion = _assignment.Suggest(PickupIn(Shevchenkivskyi), [Candidate(1, "Андрій Ткаченко", Shevchenkivskyi, active)]);

        Assert.Equal(suggested, suggestion.Courier is not null);
        if (!suggested)
        {
            Assert.Contains("Призначте вручну", suggestion.Explanation);
        }
    }

    [Fact]
    public void NoActiveCouriers_IsExplained()
    {
        var suggestion = _assignment.Suggest(PickupIn(Shevchenkivskyi), [Candidate(1, "Андрій Ткаченко", 1, isActive: false)]);

        Assert.Null(suggestion.Courier);
        Assert.Equal("Немає активних кур'єрів.", suggestion.Explanation);
    }

    [Fact]
    public void TooHeavyForEveryone_IsExplained()
    {
        var suggestion = _assignment.Suggest(PickupIn(Shevchenkivskyi, kg: 25), [Candidate(1, "Андрій Ткаченко", 1, maxKg: 10)]);

        Assert.Null(suggestion.Courier);
        Assert.Contains("25 кг", suggestion.Explanation);
    }

    [Theory]
    [InlineData(0, "немає активних доставок")]
    [InlineData(1, "1 активна доставка")]
    [InlineData(2, "2 активні доставки")]
    [InlineData(4, "4 активні доставки")]
    [InlineData(5, "5 активних доставок")]
    [InlineData(11, "11 активних доставок")]
    [InlineData(12, "12 активних доставок")]
    [InlineData(21, "21 активна доставка")]
    [InlineData(22, "22 активні доставки")]
    public void ActiveDeliveriesText_UsesUkrainianPlural(int count, string expected)
    {
        Assert.Equal(expected, CourierAssignment.ActiveDeliveriesText(count));
    }
}
