using CourierService.App.ViewModels.Orders;
using CourierService.Core;
using CourierService.Core.Entities;
using CourierService.Core.Enums;
using CourierService.Core.Exceptions;
using CourierService.Core.Orders;
using CourierService.Core.Pricing;
using CourierService.Core.Sla;
using CourierService.Data.Seed;
using CourierService.Tests.Data;

namespace CourierService.Tests.App;

/// <summary>Заглушка сервісу замовлень: запам'ятовує дані форми, повертає заданий результат або помилку.</summary>
internal sealed class FakeOrderService : IOrderService
{
    public List<OrderInput> Created { get; } = [];
    public Exception? ThrowOnCreate { get; set; }
    public OrderSearchResult SearchResult { get; set; } = new([], false, null);
    public List<OrderFilter> Searches { get; } = [];

    public Task<OrderCreated> CreateAsync(OrderInput input, CancellationToken cancellationToken = default)
    {
        if (ThrowOnCreate is not null)
        {
            return Task.FromException<OrderCreated>(ThrowOnCreate);
        }

        Created.Add(input);
        return Task.FromResult(new OrderCreated(1, "KV2610190001-2", Money.FromHryvnias(157m), new DateTime(2026, 10, 20, 12, 0, 0)));
    }

    public Task<OrderSearchResult> SearchAsync(OrderFilter filter, CancellationToken cancellationToken = default)
    {
        Searches.Add(filter);
        return Task.FromResult(SearchResult);
    }

    public Dictionary<int, Order> Orders { get; } = [];
    public List<OrderListItem> MyActive { get; set; } = [];
    public List<OrderListItem> MyCompleted { get; set; } = [];
    public List<bool> MyDeliveryRequests { get; } = [];

    public Task<Order> GetOrderAsync(int orderId, CancellationToken cancellationToken = default) =>
        Orders.TryGetValue(orderId, out Order? order)
            ? Task.FromResult(order)
            : Task.FromException<Order>(new EntityNotFoundException("Замовлення не знайдено."));

    public Task<IReadOnlyList<OrderListItem>> GetMyDeliveriesAsync(bool active, CancellationToken cancellationToken = default)
    {
        MyDeliveryRequests.Add(active);
        return Task.FromResult<IReadOnlyList<OrderListItem>>(active ? MyActive : MyCompleted);
    }
}

/// <summary>Дані форми з ідентифікаторами, як після завантаження з БД.</summary>
internal static class OrderTestData
{
    public static OrderFormData Create()
    {
        var zones = DatabaseSeeder.CreateKyivZones().ToList();
        for (int i = 0; i < zones.Count; i++)
        {
            zones[i].Id = i + 1;
        }

        var tariffs = DatabaseSeeder.CreateTariffs().ToList();
        for (int i = 0; i < tariffs.Count; i++)
        {
            tariffs[i].Id = i + 1;
        }

        List<Client> clients =
        [
            new() { Id = 1, Name = "Наталія Руденко", Phone = "+380930000204", Address = "вул. Яблунева, 3", DefaultZoneId = 5 },
            new() { Id = 2, Name = "ТОВ «Квітковий двір»", Phone = "+380670000201", Address = "вул. Каштанова, 12", DefaultZoneId = 2 },
        ];
        return new OrderFormData(clients, zones, tariffs);
    }

    public static int ZoneId(OrderFormData data, string name) => data.Zones.Single(z => z.Name == name).Id;

    public static int TariffId(OrderFormData data, string name) => data.Tariffs.Single(t => t.Name == name).Id;
}

public class OrderEditorViewModelTests
{
    private static readonly DateTime Now = new(2026, 10, 19, 12, 0, 0);

    private readonly FakeOrderService _orders = new();
    private readonly OrderFormData _data = OrderTestData.Create();

    private OrderEditorViewModel CreateEditor() => new(
        _orders, new OrderValidator(), new PriceCalculator(), new DeadlineCalculator(), new FixedClock(Now), _data);

    /// <summary>Приклад із плану: Стандарт, 1,5 кг, Шевченківський → Дарницький = 157 грн.</summary>
    private OrderEditorViewModel FilledEditor()
    {
        var editor = CreateEditor();
        editor.ClientId = 1;
        editor.RecipientName = "Оксана Мельник";
        editor.RecipientPhone = "050 123 45 67";
        editor.DeliveryAddress = "просп. Умовний, 10";
        editor.PickupZoneId = OrderTestData.ZoneId(_data, "Шевченківський");
        editor.DeliveryZoneId = OrderTestData.ZoneId(_data, "Дарницький");
        editor.TariffId = OrderTestData.TariffId(_data, "Стандарт");
        editor.WeightGrams = "1,5";
        editor.LengthCm = "30";
        editor.WidthCm = "20";
        editor.HeightCm = "10";
        return editor;
    }

    [Fact]
    public void NewForm_DefaultsToCheapestTariffAndSenderCash_AndAsksForZones()
    {
        var editor = CreateEditor();

        Assert.Equal(OrderTestData.TariffId(_data, "Стандарт"), editor.TariffId);
        Assert.Equal(PaymentPayer.Sender, editor.Payer);
        Assert.Equal(PaymentMethod.Cash, editor.Method);
        Assert.Equal(3, editor.PaymentMethods.Count);
        Assert.False(editor.Quote.IsReady);
        Assert.Contains("зони", editor.Quote.Hint);
        Assert.False(editor.HasErrors);   // порожня форма не «червоніє» одразу
    }

    [Fact]
    public void Quote_IsRecalculatedWhileTyping()
    {
        var editor = FilledEditor();

        Assert.True(editor.Quote.IsReady);
        Assert.Equal(Money.FromHryvnias(157m).ToString(), editor.Quote.Total);
        Assert.Contains(editor.Quote.Lines, line => line.Label == "Переїзд через Дніпро");
        Assert.Contains("20.10.2026 12:00", editor.Quote.DeadlineText);

        editor.IsFragile = true;
        Assert.Equal(Money.FromHryvnias(187m).ToString(), editor.Quote.Total);

        editor.DeliveryZoneId = OrderTestData.ZoneId(_data, "Печерський");
        Assert.Equal(Money.FromHryvnias(140m).ToString(), editor.Quote.Total);   // 110 + 30 за крихкість
        Assert.DoesNotContain(editor.Quote.Lines, line => line.Label == "Переїзд через Дніпро");
    }

    [Fact]
    public void Quote_ShowsBreakdownLinesThatSumToTotal()
    {
        var editor = FilledEditor();
        editor.TariffId = OrderTestData.TariffId(_data, "Експрес");
        editor.WeightGrams = "4,2";
        editor.DeclaredValue = "2000";

        Assert.Equal(
            ["Базова ціна (до 2 кг) × 1,20", "Додаткова вага: 3 кг × 15,00 грн × 1,20", "Переїзд через Дніпро",
             "Страхування: 5 ‰ від цінності понад 500,00 грн"],
            editor.Quote.Lines.Select(line => line.Label));
    }

    [Fact]
    public void Quote_WaitsForParcelSize()
    {
        var editor = FilledEditor();
        editor.HeightCm = "";

        Assert.False(editor.Quote.IsReady);
        Assert.Contains("габарити", editor.Quote.Hint);
    }

    [Theory]
    [InlineData("абв", "Вага")]
    [InlineData("150", "Вага має бути")]
    public void Quote_ExplainsInvalidParcelField(string weight, string expectedHint)
    {
        var editor = FilledEditor();
        editor.WeightGrams = weight;

        Assert.False(editor.Quote.IsReady);
        Assert.Contains(expectedHint, editor.Quote.Hint);
    }

    [Fact]
    public void Quote_ExplainsTariffWeightLimit_BeforeRecipientIsFilled()
    {
        var editor = CreateEditor();
        editor.PickupZoneId = 1;
        editor.DeliveryZoneId = 2;
        editor.TariffId = OrderTestData.TariffId(_data, "Термінова");
        editor.WeightGrams = "12";
        editor.LengthCm = editor.WidthCm = editor.HeightCm = "10";

        Assert.False(editor.Quote.IsReady);
        Assert.Contains("Термінова", editor.Quote.Hint);
    }

    [Fact]
    public void ChoosingClient_FillsPickupAddress_UnlessOperatorTypedAnother()
    {
        var editor = CreateEditor();

        editor.ClientId = 1;
        Assert.Equal("вул. Яблунева, 3", editor.PickupAddress);

        editor.ClientId = 2;
        Assert.Equal("вул. Каштанова, 12", editor.PickupAddress);

        editor.PickupAddress = "вул. Інша, 5";
        editor.ClientId = 1;
        Assert.Equal("вул. Інша, 5", editor.PickupAddress);
    }

    [Fact]
    public void ChoosingClient_FillsPickupZone_UnlessOperatorChoseAnother()
    {
        var data = OrderTestData.Create();
        var editor = CreateEditor();

        editor.ClientId = 1;
        Assert.Equal(OrderTestData.ZoneId(data, "Дніпровський"), editor.PickupZoneId);

        editor.ClientId = 2;
        Assert.Equal(OrderTestData.ZoneId(data, "Печерський"), editor.PickupZoneId);

        int chosen = OrderTestData.ZoneId(data, "Оболонський");
        editor.PickupZoneId = chosen;
        editor.ClientId = 1;
        Assert.Equal(chosen, editor.PickupZoneId);
    }

    [Fact]
    public void RecipientPays_BankTransferIsNotOffered()
    {
        var editor = CreateEditor();
        editor.Method = PaymentMethod.BankTransfer;

        editor.Payer = PaymentPayer.Recipient;

        Assert.Equal(PaymentMethod.Cash, editor.Method);
        Assert.DoesNotContain(editor.PaymentMethods, option => option.Value == PaymentMethod.BankTransfer);

        editor.Payer = PaymentPayer.Sender;
        Assert.Contains(editor.PaymentMethods, option => option.Value == PaymentMethod.BankTransfer);
    }

    [Fact]
    public async Task Save_EmptyForm_ShowsErrorsNextToFieldsAndDoesNotCallService()
    {
        var editor = CreateEditor();

        await editor.SaveCommand.ExecuteAsync(null);

        Assert.True(editor.HasErrors);
        Assert.NotEmpty(editor.GetErrors(nameof(OrderInput.ClientId)).Cast<string>());
        Assert.NotEmpty(editor.GetErrors(nameof(OrderInput.RecipientPhone)).Cast<string>());
        Assert.NotEmpty(editor.GetErrors(nameof(OrderInput.WeightGrams)).Cast<string>());
        Assert.Empty(_orders.Created);
    }

    [Fact]
    public async Task Save_ValidForm_PassesParsedInputToService()
    {
        var editor = FilledEditor();
        editor.DeclaredValue = "750,50";
        editor.Payer = PaymentPayer.Recipient;
        editor.Method = PaymentMethod.Card;
        bool saved = false;
        editor.Saved += (_, _) => saved = true;

        await editor.SaveCommand.ExecuteAsync(null);

        Assert.True(saved);
        OrderInput input = Assert.Single(_orders.Created);
        Assert.Equal(1_500, input.WeightGrams);
        Assert.Equal((30, 20, 10), (input.LengthCm, input.WidthCm, input.HeightCm));
        Assert.Equal(Money.FromHryvnias(750.50m), input.DeclaredValue);
        Assert.Equal("вул. Яблунева, 3", input.PickupAddress);
        Assert.Equal((PaymentPayer.Recipient, PaymentMethod.Card), (input.Payer, input.Method));
        Assert.Equal("KV2610190001-2", editor.Created?.TrackingNumber);
    }

    [Fact]
    public async Task Save_EmptyDeclaredValue_MeansZero()
    {
        var editor = FilledEditor();
        editor.DeclaredValue = " ";

        await editor.SaveCommand.ExecuteAsync(null);

        Assert.Equal(Money.Zero, Assert.Single(_orders.Created).DeclaredValue);
    }

    [Fact]
    public async Task Save_ServiceFieldError_ShownNextToField()
    {
        _orders.ThrowOnCreate = new DomainValidationException("Тариф «Стандарт» деактивовано. Оберіть інший.", nameof(OrderInput.TariffId));
        var editor = FilledEditor();

        await editor.SaveCommand.ExecuteAsync(null);

        Assert.Contains("деактивовано", editor.GetErrors(nameof(OrderInput.TariffId)).Cast<string>().Single());
        Assert.Null(editor.Created);
    }
}

public class OrderFormLauncherTests
{
    private readonly FakeOrderService _orders = new();
    private readonly FakeDialogService _dialogs = new();
    private readonly FakeEditDialogService _editDialogs = new();
    private readonly FakeClientService _clients = new();
    private readonly FakeZoneService _zones = new();
    private readonly FakeTariffService _tariffs = new();

    public OrderFormLauncherTests()
    {
        OrderFormData data = OrderTestData.Create();
        _clients.Items = [.. data.Clients];
        _zones.Items = [.. data.Zones];
        _tariffs.Items = [.. data.Tariffs];
    }

    private OrderFormLauncher CreateLauncher() => new(
        _clients, _zones, _tariffs, _orders, new OrderValidator(), new PriceCalculator(), new DeadlineCalculator(),
        new FixedClock(new DateTime(2026, 10, 19, 12, 0, 0)), _editDialogs, _dialogs);

    [Fact]
    public async Task OffersOnlyActiveReferences()
    {
        _clients.Items[1].IsActive = false;
        _tariffs.Items[0].IsActive = false;

        await CreateLauncher().OpenNewOrderAsync();

        var editor = Assert.IsType<OrderEditorViewModel>(Assert.Single(_editDialogs.Editors));
        Assert.Single(editor.Clients);
        Assert.Equal(2, editor.Tariffs.Count);
        Assert.Equal(10, editor.Zones.Count);
    }

    [Fact]
    public async Task ClosedWithoutSaving_ReturnsNull()
    {
        _editDialogs.Result = false;

        Assert.Null(await CreateLauncher().OpenNewOrderAsync());
    }

    [Fact]
    public async Task NoActiveClients_WarnsWithoutOpeningForm()
    {
        _clients.Items.ForEach(c => c.IsActive = false);

        Assert.Null(await CreateLauncher().OpenNewOrderAsync());

        Assert.Empty(_editDialogs.Editors);
        Assert.Contains("Клієнти", Assert.Single(_dialogs.Warnings));
    }

    [Fact]
    public async Task NoActiveTariffs_WarnsWithoutOpeningForm()
    {
        _tariffs.Items.ForEach(t => t.IsActive = false);

        Assert.Null(await CreateLauncher().OpenNewOrderAsync());

        Assert.Empty(_editDialogs.Editors);
        Assert.Single(_dialogs.Warnings);
    }
}
