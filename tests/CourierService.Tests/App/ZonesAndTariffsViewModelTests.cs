using CourierService.App.ViewModels.Directories;
using CourierService.Core;
using CourierService.Core.Directories;
using CourierService.Core.Entities;
using CourierService.Core.Enums;
using CourierService.Core.Security;

namespace CourierService.Tests.App;

/// <summary>Заглушка сервісу довідника: повертає задані записи й запам'ятовує збережені дані.</summary>
internal class FakeDirectoryService<TItem, TInput> : IDirectoryService<TItem, TInput>
    where TItem : IReferenceItem
    where TInput : IDirectoryInput
{
    public List<TItem> Items { get; set; } = [];
    public List<TInput> Saved { get; } = [];
    public List<(int Id, bool IsActive)> ActiveChanges { get; } = [];

    public Task<IReadOnlyList<TItem>> GetAllAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<TItem>>(Items);

    public Task<int> SaveAsync(TInput input, CancellationToken cancellationToken = default)
    {
        Saved.Add(input);
        return Task.FromResult(input.Id ?? 100);
    }

    public Task SetActiveAsync(int id, bool isActive, CancellationToken cancellationToken = default)
    {
        ActiveChanges.Add((id, isActive));
        return Task.CompletedTask;
    }
}

internal sealed class FakeZoneService : FakeDirectoryService<Zone, ZoneInput>, IZoneService;

internal sealed class FakeTariffService : FakeDirectoryService<Tariff, TariffInput>, ITariffService;

public class ZonesViewModelTests
{
    private readonly FakeZoneService _service = new()
    {
        Items =
        [
            new Zone { Id = 1, Name = "Печерський", Bank = RiverBank.Right, CoefficientPercent = 100 },
            new Zone { Id = 2, Name = "Дарницький", Bank = RiverBank.Left, CoefficientPercent = 120 },
        ],
    };

    private readonly CurrentUser _currentUser = new();

    private ZonesViewModel CreateList(UserRole role)
    {
        _currentUser.SignIn(new UserSession(1, "user", "Користувач", role, null));
        return new ZonesViewModel(_service, new ZoneValidator(), new FakeDialogService(), new FakeEditDialogService(), _currentUser);
    }

    [Theory]
    [InlineData(UserRole.Admin, true)]
    [InlineData(UserRole.Operator, false)]
    public void CanEdit_OnlyForAdmin(UserRole role, bool expected)
    {
        Assert.Equal(expected, CreateList(role).CanEdit);
    }

    [Fact]
    public async Task Search_FindsZonesByBank()
    {
        var list = CreateList(UserRole.Operator);
        await list.LoadCommand.ExecuteAsync(null);

        list.SearchText = "лівий";

        Assert.Equal("Дарницький", Assert.Single(list.Items).Name);
    }

    [Fact]
    public void Editor_ShowsCoefficientAsDecimal()
    {
        var editor = new ZoneEditorViewModel(_service, new ZoneValidator(), _service.Items[1]);

        Assert.Equal("1,20", editor.CoefficientPercent);
        Assert.Equal("Зміна зони", editor.Title);
    }

    [Fact]
    public async Task Editor_SavesParsedCoefficient()
    {
        var editor = new ZoneEditorViewModel(_service, new ZoneValidator(), null)
        {
            Name = "Бориспільська траса",
            Bank = RiverBank.Left,
            CoefficientPercent = "1,5",
        };

        await editor.SaveCommand.ExecuteAsync(null);

        Assert.Equal(new ZoneInput(null, "Бориспільська траса", RiverBank.Left, 150), Assert.Single(_service.Saved));
    }

    [Theory]
    [InlineData("абв", "числом")]
    [InlineData("3,5", "від 1,00 до 3,00")]
    [InlineData("1,155", "числом")]
    public async Task Editor_InvalidCoefficient_ShowsErrorNextToField(string coefficient, string expectedFragment)
    {
        var editor = new ZoneEditorViewModel(_service, new ZoneValidator(), null) { Name = "Тест", CoefficientPercent = coefficient };

        await editor.SaveCommand.ExecuteAsync(null);

        string error = editor.GetErrors(nameof(ZoneEditorViewModel.CoefficientPercent)).Cast<string>().Single();
        Assert.Contains(expectedFragment, error);
        Assert.Empty(_service.Saved);
    }
}

public class TariffEditorViewModelTests
{
    private readonly FakeTariffService _service = new();

    [Fact]
    public void NewTariff_HasSensibleDefaults()
    {
        var editor = new TariffEditorViewModel(_service, new TariffValidator(), null);

        Assert.Equal("Новий тариф", editor.Title);
        Assert.Equal("2", editor.IncludedWeightGrams);
        Assert.Equal("30", editor.MaxWeightGrams);
        Assert.Equal("500,00", editor.InsuranceFreeLimit);
        Assert.Equal("5", editor.InsurancePermille);
    }

    [Fact]
    public void ExistingTariff_ShowsValuesInHryvniasAndKilograms()
    {
        var tariff = new Tariff
        {
            Id = 3, Name = "Експрес", BasePrice = Money.FromHryvnias(150m), IncludedWeightGrams = 2_000,
            PricePerExtraKg = Money.FromHryvnias(15m), MaxWeightGrams = 20_000, DeliveryHours = 4,
            CrossRiverSurcharge = Money.FromHryvnias(25m), InsuranceFreeLimit = Money.FromHryvnias(500m),
            InsurancePermille = 5, FragileSurcharge = Money.FromHryvnias(40m),
        };

        var editor = new TariffEditorViewModel(_service, new TariffValidator(), tariff);

        Assert.Equal("150,00", editor.BasePrice);
        Assert.Equal("20", editor.MaxWeightGrams);
        Assert.Equal("4", editor.DeliveryHours);
    }

    [Fact]
    public async Task Save_ParsesUkrainianNumbers()
    {
        var editor = new TariffEditorViewModel(_service, new TariffValidator(), null)
        {
            Name = "Економ",
            BasePrice = "89,90",
            PricePerExtraKg = "9.5",
            IncludedWeightGrams = "1,5",
            CrossRiverSurcharge = "25",
            FragileSurcharge = "30 грн",
        };

        await editor.SaveCommand.ExecuteAsync(null);

        TariffInput saved = Assert.Single(_service.Saved);
        Assert.Equal(Money.FromHryvnias(89.90m), saved.BasePrice);
        Assert.Equal(Money.FromHryvnias(9.5m), saved.PricePerExtraKg);
        Assert.Equal(1_500, saved.IncludedWeightGrams);
        Assert.Equal(Money.FromHryvnias(30m), saved.FragileSurcharge);
    }

    [Fact]
    public async Task Save_InvalidFields_ShowsEachErrorNextToItsField()
    {
        var editor = new TariffEditorViewModel(_service, new TariffValidator(), null)
        {
            Name = "",
            BasePrice = "сто",
            IncludedWeightGrams = "40",
            DeliveryHours = "1,5",
            CrossRiverSurcharge = "0",
            FragileSurcharge = "0",
            PricePerExtraKg = "0",
        };

        await editor.SaveCommand.ExecuteAsync(null);

        Assert.NotEmpty(editor.GetErrors(nameof(TariffEditorViewModel.Name)).Cast<string>());
        Assert.Contains("сума в гривнях", editor.GetErrors(nameof(TariffEditorViewModel.BasePrice)).Cast<string>().Single());
        Assert.Contains("меншою за максимальну", editor.GetErrors(nameof(TariffEditorViewModel.IncludedWeightGrams)).Cast<string>().Single());
        Assert.Contains("ціле число", editor.GetErrors(nameof(TariffEditorViewModel.DeliveryHours)).Cast<string>().Single());
        Assert.Empty(_service.Saved);
    }
}
