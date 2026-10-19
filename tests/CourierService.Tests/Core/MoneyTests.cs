using CourierService.Core;
using CourierService.Core.Exceptions;

namespace CourierService.Tests.Core;

public class MoneyTests
{
    [Theory]
    [InlineData("110", 11000)]
    [InlineData("0.01", 1)]
    [InlineData("12.345", 1235)]   // половина копійки округлюється вгору
    [InlineData("12.344", 1234)]
    [InlineData("0", 0)]
    public void FromHryvnias_RoundsToKopecks(string hryvnias, long expectedKopecks)
    {
        Money money = Money.FromHryvnias(decimal.Parse(hryvnias, System.Globalization.CultureInfo.InvariantCulture));

        Assert.Equal(expectedKopecks, money.Kopecks);
    }

    [Fact]
    public void Hryvnias_ReturnsDecimalValue()
    {
        Assert.Equal(157.50m, new Money(15750).Hryvnias);
    }

    [Fact]
    public void Addition_And_Subtraction_AreExact()
    {
        // 0,1 + 0,2 = 0,3 — без похибок, на відміну від double
        Money sum = Money.FromHryvnias(0.1m) + Money.FromHryvnias(0.2m);

        Assert.Equal(Money.FromHryvnias(0.3m), sum);
        Assert.Equal(new Money(10), sum - new Money(20));
    }

    [Fact]
    public void Addition_Overflow_Throws()
    {
        Assert.Throws<OverflowException>(() => new Money(long.MaxValue) + new Money(1));
    }

    [Theory]
    [InlineData(11000, 120, 13200)]   // 110 грн × 1,20 = 132 грн
    [InlineData(11000, 115, 12650)]
    [InlineData(1000, 105, 1050)]
    [InlineData(15750, 50, 7875)]     // виплата кур'єру 50 %
    [InlineData(1, 50, 1)]            // 0,5 коп. → 1 коп. (половина вгору)
    public void ApplyPercent_RoundsHalfAwayFromZero(long kopecks, int percent, long expected)
    {
        Assert.Equal(expected, new Money(kopecks).ApplyPercent(percent).Kopecks);
    }

    [Theory]
    [InlineData(50000, 5, 250)]       // 5 ‰ від 500 грн = 2,50 грн
    [InlineData(100, 5, 1)]           // 0,5 коп. → 1 коп.
    [InlineData(0, 5, 0)]
    public void ApplyPermille_CalculatesInsurance(long kopecks, int permille, long expected)
    {
        Assert.Equal(expected, new Money(kopecks).ApplyPermille(permille).Kopecks);
    }

    [Fact]
    public void Comparison_Operators_Work()
    {
        var small = new Money(100);
        var big = new Money(200);

        Assert.True(small < big);
        Assert.True(big > small);
        Assert.True(small <= new Money(100));
        Assert.True(big >= small);
        Assert.Equal(-1, small.CompareTo(big));
    }

    [Theory]
    [InlineData(15700, "157,00 грн")]
    [InlineData(123450, "1 234,50 грн")]
    [InlineData(0, "0,00 грн")]
    public void ToString_UsesUkrainianFormat(long kopecks, string expected)
    {
        // Українська культура розділяє тисячі нерозривним пробілом
        string text = new Money(kopecks).ToString().Replace(' ', ' ');

        Assert.Equal(expected, text);
    }
}

public class DomainExceptionTests
{
    [Fact]
    public void UserMessage_IsDefinedBySubclass()
    {
        DomainException validation = new DomainValidationException("Вага має бути більшою за нуль.");
        DomainException access = new AccessDeniedException("Кур'єр не може змінювати чужі замовлення.");

        Assert.Equal("Перевірте введені дані. Вага має бути більшою за нуль.", validation.UserMessage);
        Assert.Equal("Недостатньо прав. Кур'єр не може змінювати чужі замовлення.", access.UserMessage);
    }
}
