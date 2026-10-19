using System.Globalization;
using CourierService.Core.Sla;

namespace CourierService.Tests.Core;

public class DeadlineCalculatorTests
{
    private readonly DeadlineCalculator _calculator = new();

    private static DateTime At(string text) =>
        DateTime.ParseExact(text, "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

    [Theory]
    // У робочий час, у межах дня
    [InlineData("2026-10-19 10:00", 4, "2026-10-19 14:00")]
    [InlineData("2026-10-19 09:00", 2, "2026-10-19 11:00")]
    // Закінчується рівно о 21:00 — дедлайн сьогодні, а не завтра о 09:00
    [InlineData("2026-10-19 17:00", 4, "2026-10-19 21:00")]
    [InlineData("2026-10-19 09:00", 12, "2026-10-19 21:00")]
    // Перехід на наступний день (приклад з D-14)
    [InlineData("2026-10-19 20:00", 4, "2026-10-20 12:00")]
    [InlineData("2026-10-19 10:00", 12, "2026-10-20 10:00")]
    [InlineData("2026-10-19 20:59", 2, "2026-10-20 10:59")]
    // Поза робочим часом — відлік з 09:00
    [InlineData("2026-10-19 21:00", 2, "2026-10-20 11:00")]
    [InlineData("2026-10-19 23:30", 4, "2026-10-20 13:00")]
    [InlineData("2026-10-19 00:00", 2, "2026-10-19 11:00")]
    [InlineData("2026-10-19 08:59", 2, "2026-10-19 11:00")]
    // Кілька днів і кінець місяця/року
    [InlineData("2026-10-19 15:00", 30, "2026-10-21 21:00")]  // 6 + 12 + 12
    [InlineData("2026-12-31 20:00", 4, "2027-01-01 12:00")]
    public void Deadline_CountsOnlyWorkingHours(string created, int hours, string expected)
    {
        Assert.Equal(At(expected), _calculator.CalculateDeadline(At(created), hours));
    }

    [Fact]
    public void Deadline_KeepsMinutesAndSeconds()
    {
        var created = new DateTime(2026, 10, 19, 10, 15, 42);

        Assert.Equal(new DateTime(2026, 10, 19, 12, 15, 42), _calculator.CalculateDeadline(created, 2));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void NonPositiveHours_AreRejected(int hours)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => _calculator.CalculateDeadline(At("2026-10-19 10:00"), hours));
    }

    [Theory]
    [InlineData("2026-10-19 08:00", "2026-10-19 09:00")]
    [InlineData("2026-10-19 09:00", "2026-10-19 09:00")]
    [InlineData("2026-10-19 20:59", "2026-10-19 20:59")]
    [InlineData("2026-10-19 21:00", "2026-10-20 09:00")]
    public void FirstWorkingMoment_MovesOutsideHoursTo0900(string moment, string expected)
    {
        Assert.Equal(At(expected), DeadlineCalculator.FirstWorkingMoment(At(moment)));
    }
}
