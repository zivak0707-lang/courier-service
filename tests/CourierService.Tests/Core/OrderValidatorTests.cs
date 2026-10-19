using CourierService.Core;
using CourierService.Core.Entities;
using CourierService.Core.Enums;
using CourierService.Core.Orders;
using CourierService.Data.Seed;

namespace CourierService.Tests.Core;

public class OrderValidatorTests
{
    private static readonly Tariff Urgent = DatabaseSeeder.CreateTariffs().Single(t => t.Name == "Термінова");

    private readonly OrderValidator _validator = new();

    internal static OrderInput ValidInput() => new(
        ClientId: 1,
        RecipientName: "Оксана Мельник",
        RecipientPhone: "050 123 45 67",
        PickupAddress: "вул. Вигадана, 1",
        PickupZoneId: 1,
        DeliveryAddress: "просп. Умовний, 10, кв. 5",
        DeliveryZoneId: 2,
        TariffId: 1,
        WeightGrams: 1_500,
        LengthCm: 30,
        WidthCm: 20,
        HeightCm: 10,
        DeclaredValue: Money.FromHryvnias(300m),
        IsFragile: false,
        Payer: PaymentPayer.Sender,
        Method: PaymentMethod.Cash,
        Comment: null);

    [Fact]
    public void ValidInput_HasNoErrors()
    {
        Assert.True(_validator.Validate(ValidInput()).IsValid);
        Assert.True(_validator.Validate(ValidInput(), Urgent).IsValid);
    }

    [Fact]
    public void EmptyForm_ReportsEveryRequiredField()
    {
        var input = new OrderInput(0, "", "", " ", 0, "", 0, 0, 0, 0, 0, 0, Money.Zero, false, 0, 0, null);

        var errors = _validator.Validate(input);

        string[] expected =
        [
            nameof(OrderInput.ClientId), nameof(OrderInput.RecipientName), nameof(OrderInput.RecipientPhone),
            nameof(OrderInput.PickupAddress), nameof(OrderInput.PickupZoneId), nameof(OrderInput.DeliveryAddress),
            nameof(OrderInput.DeliveryZoneId), nameof(OrderInput.TariffId), nameof(OrderInput.WeightGrams),
            nameof(OrderInput.LengthCm), nameof(OrderInput.WidthCm), nameof(OrderInput.HeightCm),
            nameof(OrderInput.Payer), nameof(OrderInput.Method),
        ];
        Assert.Equal(expected.Order(), errors.PropertyNames.Order());
    }

    [Theory]
    [InlineData(120, true)]
    [InlineData(121, false)]
    public void Side_LimitedTo120cm(int length, bool valid)
    {
        var errors = _validator.Validate(ValidInput() with { LengthCm = length, WeightGrams = 100 });

        Assert.Equal(valid, errors[nameof(OrderInput.LengthCm)].Count == 0);
    }

    [Theory]
    [InlineData(100_000, true)]
    [InlineData(100_001, false)]
    [InlineData(-5, false)]
    public void Weight_LimitedTo100kg(int grams, bool valid)
    {
        Assert.Equal(valid, _validator.Validate(ValidInput() with { WeightGrams = grams }).IsValid);
    }

    [Theory]
    [InlineData(-0.01, false)]
    [InlineData(0, true)]
    [InlineData(1_000_000, true)]
    [InlineData(1_000_000.01, false)]
    public void DeclaredValue_Range(double hryvnias, bool valid)
    {
        var input = ValidInput() with { DeclaredValue = Money.FromHryvnias((decimal)hryvnias) };

        Assert.Equal(valid, _validator.Validate(input).IsValid);
    }

    [Theory]
    [InlineData(PaymentPayer.Sender, PaymentMethod.Cash, true)]
    [InlineData(PaymentPayer.Sender, PaymentMethod.Card, true)]
    [InlineData(PaymentPayer.Sender, PaymentMethod.BankTransfer, true)]
    [InlineData(PaymentPayer.Recipient, PaymentMethod.Cash, true)]
    [InlineData(PaymentPayer.Recipient, PaymentMethod.Card, true)]
    [InlineData(PaymentPayer.Recipient, PaymentMethod.BankTransfer, false)]   // D-09
    public void PaymentMethod_DependsOnPayer(PaymentPayer payer, PaymentMethod method, bool valid)
    {
        var errors = _validator.Validate(ValidInput() with { Payer = payer, Method = method });

        Assert.Equal(valid, errors.IsValid);
        Assert.Equal(valid, PaymentRules.IsAllowed(payer, method));
        Assert.Equal(valid, PaymentRules.AllowedMethods(payer).Contains(method));
    }

    [Fact]
    public void InvalidPhoneAndLongTexts_AreReported()
    {
        var input = ValidInput() with
        {
            RecipientPhone = "12345",
            RecipientName = new string('А', OrderValidator.RecipientNameMaxLength + 1),
            DeliveryAddress = new string('А', Order.AddressMaxLength + 1),
            Comment = new string('А', OrderValidator.CommentMaxLength + 1),
        };

        var errors = _validator.Validate(input);

        Assert.NotEmpty(errors[nameof(OrderInput.RecipientPhone)]);
        Assert.NotEmpty(errors[nameof(OrderInput.RecipientName)]);
        Assert.NotEmpty(errors[nameof(OrderInput.DeliveryAddress)]);
        Assert.NotEmpty(errors[nameof(OrderInput.Comment)]);
    }

    [Theory]
    [InlineData(10_000, true)]    // рівно максимум тарифу «Термінова»
    [InlineData(10_001, false)]
    public void TariffMaxWeight_CheckedOnlyWithTariff(int grams, bool valid)
    {
        var input = ValidInput() with { WeightGrams = grams };

        Assert.True(_validator.Validate(input).IsValid);
        var errors = _validator.Validate(input, Urgent);
        Assert.Equal(valid, errors.IsValid);
        if (!valid)
        {
            Assert.Contains("Термінова", errors[nameof(OrderInput.WeightGrams)].Single());
        }
    }

    [Fact]
    public void VolumetricWeightAboveTariff_ExplainsVolumetricWeight()
    {
        // 60 × 40 × 20 / 4 = 12 000 г при фактичних 2 кг
        var input = ValidInput() with { WeightGrams = 2_000, LengthCm = 60, WidthCm = 40, HeightCm = 20 };

        string message = _validator.Validate(input, Urgent)[nameof(OrderInput.WeightGrams)].Single();

        Assert.Contains("Об'ємна вага 12", message);
    }
}
