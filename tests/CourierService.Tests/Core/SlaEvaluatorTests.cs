using System.Globalization;
using CourierService.Core.Enums;
using CourierService.Core.Sla;
using CourierService.Tests.Data;

namespace CourierService.Tests.Core;

public class SlaEvaluatorTests
{
    private static DateTime At(string text) =>
        DateTime.ParseExact(text, "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

    private static SlaResult Evaluate(string now, OrderStatus status, string created, string deadline, string? delivered = null) =>
        new SlaEvaluator(new FixedClock(At(now)))
            .Evaluate(status, At(created), At(deadline), delivered is null ? null : At(delivered));

    [Theory]
    [InlineData("2026-10-19 10:00", "2026-10-19 12:00", 120)]
    [InlineData("2026-10-19 20:00", "2026-10-20 12:00", 240)]   // 1 год сьогодні + 3 год завтра (приклад D-14)
    [InlineData("2026-10-19 22:00", "2026-10-20 10:00", 60)]    // ніч не рахується
    [InlineData("2026-10-19 07:00", "2026-10-19 09:00", 0)]
    [InlineData("2026-10-19 12:00", "2026-10-19 12:00", 0)]
    [InlineData("2026-10-19 12:00", "2026-10-19 11:00", 0)]     // кінець раніше початку
    [InlineData("2026-10-19 15:00", "2026-10-21 21:00", 1800)]  // 6 + 12 + 12 год
    public void WorkingTimeBetween_CountsOnlyWorkingHours(string from, string to, int expectedMinutes)
    {
        Assert.Equal(TimeSpan.FromMinutes(expectedMinutes), DeadlineCalculator.WorkingTimeBetween(At(from), At(to)));
    }

    [Theory]
    [InlineData("2026-10-19 09:00", 12)]
    [InlineData("2026-10-19 20:00", 4)]
    [InlineData("2026-10-19 23:30", 2)]
    [InlineData("2026-10-19 15:15", 30)]
    public void WorkingTimeBetween_IsInverseOfDeadline(string created, int hours)
    {
        DateTime deadline = new DeadlineCalculator().CalculateDeadline(At(created), hours);

        Assert.Equal(TimeSpan.FromHours(hours),
            DeadlineCalculator.WorkingTimeBetween(DeadlineCalculator.FirstWorkingMoment(At(created)), deadline));
    }

    [Fact]
    public void PlentyOfTimeLeft_IsOnTime()
    {
        // Експрес 4 год: 10:00 → 14:00, зараз 11:00 — лишилося 3 год (75 %)
        SlaResult sla = Evaluate("2026-10-19 11:00", OrderStatus.Assigned, "2026-10-19 10:00", "2026-10-19 14:00");

        Assert.Equal(SlaState.OnTime, sla.State);
        Assert.Equal("Вчасно: лишилося 3 год", sla.Text);
    }

    [Theory]
    [InlineData("2026-10-19 13:00", SlaState.OnTime)]    // рівно 25 % (1 год із 4) — ще вчасно
    [InlineData("2026-10-19 13:01", SlaState.AtRisk)]    // менше чверті
    [InlineData("2026-10-19 14:00", SlaState.AtRisk)]    // рівно на дедлайні — ще не прострочено
    [InlineData("2026-10-19 14:01", SlaState.Overdue)]
    public void AtRisk_WhenLessThanQuarterOfTermLeft(string now, SlaState expected)
    {
        Assert.Equal(expected, Evaluate(now, OrderStatus.PickedUp, "2026-10-19 10:00", "2026-10-19 14:00").State);
    }

    [Fact]
    public void Evening_RemainingCountedInWorkingHours()
    {
        // Стандарт 12 год: 10:00 → завтра 10:00; о 20:30 лишилося 0,5 + 1 = 1,5 год із 12 — під загрозою,
        // хоча за календарем до дедлайну ще 13,5 год
        SlaResult sla = Evaluate("2026-10-19 20:30", OrderStatus.Assigned, "2026-10-19 10:00", "2026-10-20 10:00");

        Assert.Equal(SlaState.AtRisk, sla.State);
        Assert.Equal("Під загрозою: лишилося 1 год 30 хв", sla.Text);
    }

    [Fact]
    public void Overdue_ShowsWorkingTimeSinceDeadline()
    {
        // Дедлайн 20:00, зараз завтра 09:30 — прострочено на 1 год (до 21:00) + 30 хв
        SlaResult sla = Evaluate("2026-10-20 09:30", OrderStatus.DeliveryFailed, "2026-10-19 16:00", "2026-10-19 20:00");

        Assert.Equal(SlaState.Overdue, sla.State);
        Assert.Equal("Прострочено на 1 год 30 хв", sla.Text);
    }

    [Theory]
    [InlineData("2026-10-19 13:59", SlaState.OnTime, "Доставлено вчасно")]
    [InlineData("2026-10-19 14:00", SlaState.OnTime, "Доставлено вчасно")]
    [InlineData("2026-10-19 14:40", SlaState.Overdue, "Доставлено із запізненням 40 хв")]
    public void Delivered_ComparedWithDeadline_RegardlessOfNow(string delivered, SlaState expected, string text)
    {
        SlaResult sla = Evaluate("2026-10-25 12:00", OrderStatus.Delivered, "2026-10-19 10:00", "2026-10-19 14:00", delivered);

        Assert.Equal(expected, sla.State);
        Assert.Equal(text, sla.Text);
    }

    [Theory]
    [InlineData(OrderStatus.Cancelled)]
    [InlineData(OrderStatus.Returned)]
    public void CancelledAndReturned_AreNotTracked(OrderStatus status)
    {
        SlaResult sla = Evaluate("2026-10-25 12:00", status, "2026-10-19 10:00", "2026-10-19 14:00");

        Assert.Same(SlaResult.NotTracked, sla);
    }

    [Theory]
    [InlineData(0, "менше хвилини")]
    [InlineData(40, "40 хв")]
    [InlineData(60, "1 год")]
    [InlineData(135, "2 год 15 хв")]
    public void FormatDuration_IsReadable(int minutes, string expected)
    {
        Assert.Equal(expected, SlaEvaluator.FormatDuration(TimeSpan.FromMinutes(minutes)));
    }
}
