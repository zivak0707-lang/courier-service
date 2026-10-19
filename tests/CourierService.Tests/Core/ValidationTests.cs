using CourierService.Core;
using CourierService.Core.Directories;
using CourierService.Core.Enums;
using CourierService.Core.Exceptions;
using CourierService.Core.Validation;

namespace CourierService.Tests.Core;

public class PhoneNumberTests
{
    [Theory]
    [InlineData("+380501234567", "+380501234567")]
    [InlineData("+380 50 123 45 67", "+380501234567")]
    [InlineData("+38 (050) 123-45-67", "+380501234567")]
    [InlineData("050 123 45 67", "+380501234567")]
    [InlineData("(067)1234567", "+380671234567")]
    [InlineData("380931234567", "+380931234567")]
    [InlineData("  0931234567  ", "+380931234567")]
    public void TryNormalize_AcceptsCommonFormats(string input, string expected)
    {
        Assert.True(PhoneNumber.TryNormalize(input, out string normalized));
        Assert.Equal(expected, normalized);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("050123456")]          // 9 цифр
    [InlineData("05012345678")]        // 11 цифр
    [InlineData("+0501234567")]        // «+» без коду країни
    [InlineData("+380 05 123 45 67")]  // код оператора з нуля
    [InlineData("050-ABC-45-67")]      // літери
    [InlineData("+48 501 234 567")]    // іноземний номер
    [InlineData("050 123 45 67 доб. 2")]
    public void TryNormalize_RejectsInvalidNumbers(string? input)
    {
        Assert.False(PhoneNumber.TryNormalize(input, out _));
    }

    [Fact]
    public void Format_ShowsReadableNumber()
    {
        Assert.Equal("+380 (50) 123-45-67", PhoneNumber.Format("0501234567"));
        Assert.Equal("не номер", PhoneNumber.Format("не номер"));
    }
}

public class InputParsingTests
{
    [Theory]
    [InlineData("110", 11000)]
    [InlineData("110,5", 11050)]
    [InlineData("110.50", 11050)]
    [InlineData("1 234,56", 123456)]
    [InlineData("25 грн", 2500)]
    [InlineData("0", 0)]
    [InlineData("-5", -500)]           // знак розбирається, межі перевіряє валідатор
    public void TryParseMoney_AcceptsUkrainianInput(string text, long expectedKopecks)
    {
        Assert.True(InputParsing.TryParseMoney(text, out Money money));
        Assert.Equal(expectedKopecks, money.Kopecks);
    }

    [Theory]
    [InlineData("")]
    [InlineData("абв")]
    [InlineData("10,555")]             // більше двох знаків після коми
    [InlineData("1,2,3")]
    [InlineData("99999999999999")]
    public void TryParseMoney_RejectsInvalidInput(string text)
    {
        Assert.False(InputParsing.TryParseMoney(text, out _));
    }

    [Theory]
    [InlineData("2", 2000)]
    [InlineData("0,5", 500)]
    [InlineData("1.25", 1250)]
    [InlineData("0,001", 1)]
    public void TryParseKilograms_ConvertsToGrams(string text, int expectedGrams)
    {
        Assert.True(InputParsing.TryParseKilograms(text, out int grams));
        Assert.Equal(expectedGrams, grams);
    }

    [Theory]
    [InlineData("0,0005")]
    [InlineData("кг")]
    public void TryParseKilograms_RejectsInvalidInput(string text)
    {
        Assert.False(InputParsing.TryParseKilograms(text, out _));
    }

    [Theory]
    [InlineData("1,15", 115)]
    [InlineData("1", 100)]
    [InlineData("1.3", 130)]
    public void TryParseCoefficient_ConvertsToPercent(string text, int expected)
    {
        Assert.True(InputParsing.TryParseCoefficient(text, out int percent));
        Assert.Equal(expected, percent);
    }

    [Fact]
    public void TryParseCoefficient_TooManyDecimals_IsRejected()
    {
        Assert.False(InputParsing.TryParseCoefficient("1,155", out _));
    }

    [Theory]
    [InlineData("12", 12)]
    [InlineData(" 7 ", 7)]
    public void TryParseInt_AcceptsWholeNumbers(string text, int expected)
    {
        Assert.True(InputParsing.TryParseInt(text, out int value));
        Assert.Equal(expected, value);
    }

    [Theory]
    [InlineData("1,5")]
    [InlineData("99999999999")]
    public void TryParseInt_RejectsFractionsAndOverflow(string text)
    {
        Assert.False(InputParsing.TryParseInt(text, out _));
    }

    [Fact]
    public void Formatting_IsInverseOfParsing()
    {
        Assert.Equal("0,5", InputParsing.FormatKilograms(500));
        Assert.Equal("1,15", InputParsing.FormatCoefficient(115));
        Assert.Equal("110,50", InputParsing.FormatMoneyForInput(new Money(11050)));
    }
}

public class ValidationRulesTests
{
    [Theory]
    [InlineData("admin", true)]
    [InlineData("courier.5", true)]
    [InlineData("o_bondar-2", true)]
    [InlineData("ab", false)]           // закоротко
    [InlineData("5courier", false)]     // починається з цифри
    [InlineData("кур'єр", false)]       // кирилиця
    [InlineData("user name", false)]    // пробіл
    [InlineData("", false)]
    public void Login_Rules(string login, bool valid)
    {
        var errors = new ValidationErrors();

        errors.Login("Login", login);

        Assert.Equal(valid, errors.IsValid);
    }

    [Theory]
    [InlineData("admin123", true)]
    [InlineData("Пароль2026", true)]
    [InlineData("short1", false)]       // 6 символів
    [InlineData("onlyletters", false)]
    [InlineData("12345678", false)]
    public void Password_Rules(string password, bool valid)
    {
        var errors = new ValidationErrors();

        errors.Password("Password", password, required: true);

        Assert.Equal(valid, errors.IsValid);
    }

    [Fact]
    public void Password_EmptyAndNotRequired_IsValid()
    {
        var errors = new ValidationErrors();

        errors.Password("Password", "", required: false);

        Assert.True(errors.IsValid);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("shop@example.com", true)]
    [InlineData("no-at-sign.com", false)]
    [InlineData("name@domain", false)]
    [InlineData("two words@example.com", false)]
    public void OptionalEmail_Rules(string? email, bool valid)
    {
        var errors = new ValidationErrors();

        errors.OptionalEmail("Email", email, 100);

        Assert.Equal(valid, errors.IsValid);
    }

    [Fact]
    public void ThrowIfInvalid_CarriesErrorsByField()
    {
        var errors = new ValidationErrors();
        errors.Add("Name", "Заповніть поле «Назва».");
        errors.Add("Phone", "Невірний телефон.");

        var exception = Assert.Throws<DomainValidationException>(errors.ThrowIfInvalid);

        Assert.Equal("Заповніть поле «Назва».", exception.Message);
        Assert.Equal(["Невірний телефон."], exception.Errors["Phone"]);
    }
}

public class DirectoryValidatorsTests
{
    private static TariffInput ValidTariff() => new(
        null, "Стандарт", Money.FromHryvnias(110m), 2_000, Money.FromHryvnias(12m), 30_000, 12,
        Money.FromHryvnias(25m), Money.FromHryvnias(500m), 5, Money.FromHryvnias(30m));

    private static CourierInput ValidCourier(int? id = null, string? password = "courier123") => new(
        id, "Андрій Ткаченко", "courier9", password, "050 000 01 09", TransportType.Car, 30_000, 1, 50);

    [Theory]
    [InlineData("Печерський", 100, true)]
    [InlineData("Деснянський", 300, true)]
    [InlineData("", 100, false)]
    [InlineData("Тест", 99, false)]
    [InlineData("Тест", 301, false)]
    public void ZoneValidator_CheckNameAndCoefficient(string name, int coefficient, bool valid)
    {
        var errors = new ZoneValidator().Validate(new ZoneInput(null, name, RiverBank.Right, coefficient));

        Assert.Equal(valid, errors.IsValid);
    }

    [Fact]
    public void ZoneValidator_UnknownBank_IsRejected()
    {
        var errors = new ZoneValidator().Validate(new ZoneInput(null, "Тест", (RiverBank)9, 100));

        Assert.NotEmpty(errors[nameof(ZoneInput.Bank)]);
    }

    [Fact]
    public void TariffValidator_ValidTariff_HasNoErrors()
    {
        Assert.True(new TariffValidator().Validate(ValidTariff()).IsValid);
    }

    [Fact]
    public void TariffValidator_IncludedWeightNotLessThanMax_IsRejected()
    {
        var errors = new TariffValidator().Validate(ValidTariff() with { IncludedWeightGrams = 30_000 });

        Assert.Contains("меншою за максимальну", errors[nameof(TariffInput.IncludedWeightGrams)].Single());
    }

    [Theory]
    [InlineData(nameof(TariffInput.BasePrice))]
    [InlineData(nameof(TariffInput.DeliveryHours))]
    [InlineData(nameof(TariffInput.InsurancePermille))]
    [InlineData(nameof(TariffInput.MaxWeightGrams))]
    [InlineData(nameof(TariffInput.FragileSurcharge))]
    public void TariffValidator_OutOfRangeValues_AreReportedOnTheirFields(string field)
    {
        TariffInput input = field switch
        {
            nameof(TariffInput.BasePrice) => ValidTariff() with { BasePrice = Money.Zero },
            nameof(TariffInput.DeliveryHours) => ValidTariff() with { DeliveryHours = 73 },
            nameof(TariffInput.InsurancePermille) => ValidTariff() with { InsurancePermille = 1001 },
            nameof(TariffInput.MaxWeightGrams) => ValidTariff() with { MaxWeightGrams = 100_001 },
            _ => ValidTariff() with { FragileSurcharge = Money.FromHryvnias(-1m) },
        };

        var errors = new TariffValidator().Validate(input);

        Assert.NotEmpty(errors[field]);
    }

    [Fact]
    public void ClientValidator_ReportsEveryInvalidField()
    {
        var input = new ClientInput(null, ClientType.Company, "", "123", "bad-email", " ", new string('x', 501));

        var errors = new ClientValidator().Validate(input);

        string[] expected = [nameof(input.Name), nameof(input.Phone), nameof(input.Email), nameof(input.Address), nameof(input.Notes)];
        Assert.Equal(expected.Order(), errors.PropertyNames.Order());
    }

    [Fact]
    public void ClientValidator_ValidClient_HasNoErrors()
    {
        var input = new ClientInput(null, ClientType.Individual, "Наталія Руденко", "093 000 02 04", null, "вул. Яблунева, 3", null);

        Assert.True(new ClientValidator().Validate(input).IsValid);
    }

    [Fact]
    public void CourierValidator_NewCourierWithoutPassword_IsRejected()
    {
        var errors = new CourierValidator().Validate(ValidCourier(password: null));

        Assert.NotEmpty(errors[nameof(CourierInput.Password)]);
    }

    [Fact]
    public void CourierValidator_ExistingCourierWithoutPassword_IsValid()
    {
        Assert.True(new CourierValidator().Validate(ValidCourier(id: 5, password: null)).IsValid);
    }

    [Fact]
    public void CourierValidator_ZonePayoutAndWeight_AreChecked()
    {
        var errors = new CourierValidator().Validate(ValidCourier() with { HomeZoneId = 0, PayoutPercent = 101, MaxWeightGrams = 0 });

        Assert.NotEmpty(errors[nameof(CourierInput.HomeZoneId)]);
        Assert.NotEmpty(errors[nameof(CourierInput.PayoutPercent)]);
        Assert.NotEmpty(errors[nameof(CourierInput.MaxWeightGrams)]);
    }

    [Fact]
    public void UserValidator_NewUser_RequiresAllFields()
    {
        var errors = new UserValidator().Validate(new UserInput(null, "", "", null, UserRole.Operator));

        Assert.Equal(["FullName", "Login", "Password"], errors.PropertyNames.Order());
    }
}

public class TextSearchTests
{
    [Theory]
    [InlineData("київ", "Київ", true)]
    [InlineData("КВІТ", "ТОВ «Квітковий двір»", true)]
    [InlineData("  двір ", "ТОВ «Квітковий двір»", true)]
    [InlineData("", "будь-що", true)]
    [InlineData("кава", "ТОВ «Квітковий двір»", false)]
    public void Matches_IgnoresCaseForCyrillic(string query, string field, bool expected)
    {
        Assert.Equal(expected, TextSearch.Matches(query, field));
    }

    [Theory]
    [InlineData("093 000 02 04", true)]
    [InlineData("0930000204", true)]
    [InlineData("+380 93 000", true)]
    [InlineData("000-02", true)]
    [InlineData("02", false)]          // замало цифр
    [InlineData("093 abc", false)]     // не схоже на номер
    [InlineData("067", false)]
    public void MatchesPhone_ComparesDigits(string query, bool expected)
    {
        Assert.Equal(expected, TextSearch.MatchesPhone(query, "+380930000204"));
    }

    [Fact]
    public void Matches_SearchesAllFields()
    {
        Assert.True(TextSearch.Matches("0002", "Наталія Руденко", "+380930000204"));
        Assert.False(TextSearch.Matches("0002", "Наталія Руденко", null));
    }
}
