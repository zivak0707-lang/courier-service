using CourierService.Core.Orders;

namespace CourierService.Tests.Core;

public class OrderQueryTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_MeansFiltersOnly(string? text)
    {
        Assert.Equal(OrderQueryKind.None, OrderQuery.Parse(text).Kind);
    }

    [Theory]
    [InlineData("KV2610190042-6")]
    [InlineData("kv 2610190042 6")]
    [InlineData("2610190042-6")]
    [InlineData(" 2610190042 - 6 ")]
    public void TrackingNumber_IsRecognizedAndNormalized(string text)
    {
        OrderQuery query = OrderQuery.Parse(text);

        Assert.Equal(OrderQueryKind.TrackingNumber, query.Kind);
        Assert.Equal("KV2610190042-6", query.Value);
    }

    [Fact]
    public void TrackingNumberWithTypo_ExplainsCheckDigit()
    {
        OrderQuery query = OrderQuery.Parse("KV2610190042-7");

        Assert.Equal(OrderQueryKind.Invalid, query.Kind);
        Assert.Contains("контрольна цифра", query.Message);
    }

    [Theory]
    [InlineData("KV12")]
    [InlineData("KV2610190042")]
    public void IncompleteTrackingNumber_ShowsExpectedFormat(string text)
    {
        OrderQuery query = OrderQuery.Parse(text);

        Assert.Equal(OrderQueryKind.Invalid, query.Kind);
        Assert.Contains("KV2610190042-6", query.Message);
    }

    [Theory]
    [InlineData("050 123 45 67", "0501234567")]
    [InlineData("+38 (050) 123-45-67", "380501234567")]
    [InlineData("4567", "4567")]
    [InlineData("26101900426", "26101900426")]   // 11 цифр без дефіса й KV — це телефон, а не трек-номер
    [InlineData("123", "123")]
    public void Digits_AreTreatedAsPhoneFragment(string text, string expectedDigits)
    {
        OrderQuery query = OrderQuery.Parse(text);

        Assert.Equal(OrderQueryKind.Phone, query.Kind);
        Assert.Equal(expectedDigits, query.Value);
    }

    [Theory]
    [InlineData("12")]
    [InlineData("Руденко")]
    [InlineData("050-абв")]
    public void UnrecognizedQuery_ExplainsWhatToEnter(string text)
    {
        OrderQuery query = OrderQuery.Parse(text);

        Assert.Equal(OrderQueryKind.Invalid, query.Kind);
        Assert.Contains("трек-номер", query.Message);
    }

    [Fact]
    public void LastDays_CoversThirtyDaysIncludingToday()
    {
        OrderFilter filter = OrderFilter.LastDays(new DateTime(2026, 10, 19, 15, 45, 0));

        Assert.Equal(new DateTime(2026, 9, 20), filter.From);
        Assert.Equal(new DateTime(2026, 10, 19), filter.To);
    }
}
