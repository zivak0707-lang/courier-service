using CourierService.Core.Orders;

namespace CourierService.Tests.Core;

public class TrackingNumberTests
{
    private static readonly DateTime Date = new(2026, 10, 19, 14, 30, 0);

    [Theory]
    [InlineData("7992739871", 3)]   // класичний приклад алгоритму Луна
    [InlineData("2610190042", 6)]
    [InlineData("0000000000", 0)]
    public void LuhnCheckDigit_MatchesKnownValues(string digits, int expected)
    {
        Assert.Equal(expected, TrackingNumber.LuhnCheckDigit(digits));
    }

    [Fact]
    public void LuhnCheckDigit_RejectsNonDigits()
    {
        Assert.Throws<ArgumentException>(() => TrackingNumber.LuhnCheckDigit("12a4"));
    }

    [Theory]
    [InlineData(42, "KV2610190042-6")]
    [InlineData(1, "KV2610190001-2")]
    [InlineData(9999, "KV2610199999-8")]
    public void Create_ComposesPrefixDateSequenceAndCheckDigit(int sequence, string expected)
    {
        string number = TrackingNumber.Create(Date, sequence);

        Assert.Equal(expected, number);
        Assert.Equal(TrackingNumber.Length, number.Length);
        Assert.Equal(TrackingNumberCheck.Valid, TrackingNumber.Check(number, out _));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(10_000)]
    public void Create_RejectsSequenceOutOfRange(int sequence)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => TrackingNumber.Create(Date, sequence));
    }

    [Fact]
    public void DayPrefix_IsLettersAndDate()
    {
        Assert.Equal("KV261019", TrackingNumber.DayPrefix(Date));
        Assert.StartsWith(TrackingNumber.DayPrefix(Date), TrackingNumber.Create(Date, 7));
    }

    [Theory]
    [InlineData("KV2610190042-6", 42)]
    [InlineData("KV2610199999-8", 9999)]
    public void TryGetSequence_ReadsDailyNumber(string number, int expected)
    {
        Assert.True(TrackingNumber.TryGetSequence(number, out int sequence));
        Assert.Equal(expected, sequence);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("KV261019")]
    [InlineData("KV26101900AB-6")]
    public void TryGetSequence_FailsOnMalformedNumber(string? number)
    {
        Assert.False(TrackingNumber.TryGetSequence(number, out _));
    }

    [Theory]
    [InlineData("KV2610190042-6")]
    [InlineData("kv2610190042-6")]
    [InlineData("  KV 261019 0042 6 ")]
    [InlineData("2610190042-6")]        // без літер
    [InlineData("26101900426")]
    public void Check_AcceptsDifferentInputForms(string input)
    {
        Assert.Equal(TrackingNumberCheck.Valid, TrackingNumber.Check(input, out string normalized));
        Assert.Equal("KV2610190042-6", normalized);
    }

    [Theory]
    [InlineData("KV2610190042-7")]   // неправильна контрольна цифра
    [InlineData("KV2610190043-6")]   // описка в одній цифрі
    [InlineData("KV2610190024-6")]   // переставлені сусідні цифри
    public void Check_DetectsTypos(string input)
    {
        Assert.Equal(TrackingNumberCheck.WrongCheckDigit, TrackingNumber.Check(input, out string normalized));
        Assert.Equal(string.Empty, normalized);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("KV2610190042")]       // без контрольної цифри
    [InlineData("KV2610190042-61")]
    [InlineData("AB2610190042-6")]
    [InlineData("Шевченко")]
    [InlineData("0501234567")]
    public void Check_RejectsWrongFormat(string? input)
    {
        Assert.Equal(TrackingNumberCheck.InvalidFormat, TrackingNumber.Check(input, out _));
    }
}
