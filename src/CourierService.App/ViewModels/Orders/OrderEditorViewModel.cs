using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CourierService.App.ViewModels.Directories;
using CourierService.Core;
using CourierService.Core.Abstractions;
using CourierService.Core.Entities;
using CourierService.Core.Enums;
using CourierService.Core.Orders;
using CourierService.Core.Pricing;
using CourierService.Core.Sla;
using CourierService.Core.Validation;

namespace CourierService.App.ViewModels.Orders;

/// <summary>
/// Довідники для форми замовлення (лише активні записи), завантажені один раз при відкритті.
/// </summary>
/// <param name="Clients">Відправники.</param>
/// <param name="Zones">Зони доставки.</param>
/// <param name="Tariffs">Тарифи.</param>
public sealed record OrderFormData(IReadOnlyList<Client> Clients, IReadOnlyList<Zone> Zones, IReadOnlyList<Tariff> Tariffs);

/// <summary>
/// Форма нового замовлення (рішення D-21). Вартість перераховується в пам'яті під час кожної зміни
/// полів посилки, зон чи тарифу; остаточну вартість під час збереження рахує сервіс.
/// </summary>
/// <remarks>
/// Поля-рядки мають ті самі назви, що й <see cref="OrderInput"/>, — помилки валідатора
/// з'являються біля відповідних полів.
/// </remarks>
public sealed partial class OrderEditorViewModel : EditViewModelBase
{
    // Зміна цих полів змінює вартість
    private static readonly HashSet<string> PriceTriggers =
        [.. OrderValidator.PriceFields, nameof(IsFragile)];

    private readonly IOrderService _orders;
    private readonly OrderValidator _validator;
    private readonly IPriceCalculator _calculator;
    private readonly IDeadlineCalculator _deadlines;
    private readonly IClock _clock;
    private readonly OrderFormData _data;
    private OrderInput? _validInput;
    private string? _autoPickupAddress;

    /// <summary>
    /// Створює форму.
    /// </summary>
    /// <param name="orders">Сервіс замовлень.</param>
    /// <param name="validator">Правила замовлення.</param>
    /// <param name="calculator">Калькулятор вартості.</param>
    /// <param name="deadlines">Розрахунок дедлайну.</param>
    /// <param name="clock">Поточний час.</param>
    /// <param name="data">Активні клієнти, зони й тарифи.</param>
    public OrderEditorViewModel(
        IOrderService orders,
        OrderValidator validator,
        IPriceCalculator calculator,
        IDeadlineCalculator deadlines,
        IClock clock,
        OrderFormData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        _orders = orders;
        _validator = validator;
        _calculator = calculator;
        _deadlines = deadlines;
        _clock = clock;
        _data = data;

        Clients = data.Clients
            .Select(c => new Option<int>(c.Id, $"{c.Name} · {PhoneNumber.Format(c.Phone)}"))
            .ToList();
        Zones = data.Zones
            .Select(z => new Option<int>(z.Id, $"{z.Name} ({z.Bank.ToDisplayName().ToLowerInvariant()} берег)"))
            .ToList();
        Tariffs = data.Tariffs
            .Select(t => new Option<int>(t.Id,
                $"{t.Name} — {t.DeliveryHours} роб. год, до {InputParsing.FormatKilograms(t.MaxWeightGrams)} кг, від {t.BasePrice}"))
            .ToList();

        // Типово — найдешевший тариф і оплата готівкою відправником
        TariffId = data.Tariffs.OrderBy(t => t.BasePrice).Select(t => t.Id).FirstOrDefault();
        Payer = PaymentPayer.Sender;
        Method = PaymentMethod.Cash;
        PaymentMethods = MethodsFor(Payer);
        DeclaredValue = "0";
        Quote = CalculateQuote();
    }

    /// <inheritdoc />
    public override string Title => "Нове замовлення";

    /// <inheritdoc />
    public override double DialogWidth => 980;

    /// <summary>Результат оформлення (після успішного збереження).</summary>
    public OrderCreated? Created { get; private set; }

    /// <summary>Варіанти відправника.</summary>
    public IReadOnlyList<Option<int>> Clients { get; }

    /// <summary>Варіанти зон (звідки забрати й куди доставити).</summary>
    public IReadOnlyList<Option<int>> Zones { get; }

    /// <summary>Варіанти тарифу.</summary>
    public IReadOnlyList<Option<int>> Tariffs { get; }

    /// <summary>Варіанти платника.</summary>
    public IReadOnlyList<Option<PaymentPayer>> Payers => Options.Payers;

    /// <summary>Способи оплати, допустимі для обраного платника (рішення D-09).</summary>
    [ObservableProperty]
    public partial IReadOnlyList<Option<PaymentMethod>> PaymentMethods { get; private set; }

    /// <summary>Вартість для показу: розбивка або підказка.</summary>
    [ObservableProperty]
    public partial PriceQuote Quote { get; private set; }

    /// <summary>Відправник.</summary>
    [ObservableProperty]
    public partial int ClientId { get; set; }

    /// <summary>ПІБ одержувача.</summary>
    [ObservableProperty]
    public partial string RecipientName { get; set; } = string.Empty;

    /// <summary>Телефон одержувача.</summary>
    [ObservableProperty]
    public partial string RecipientPhone { get; set; } = string.Empty;

    /// <summary>Звідки забрати (типово — адреса відправника).</summary>
    [ObservableProperty]
    public partial string PickupAddress { get; set; } = string.Empty;

    /// <summary>Зона, звідки забрати.</summary>
    [ObservableProperty]
    public partial int PickupZoneId { get; set; }

    /// <summary>Адреса доставки.</summary>
    [ObservableProperty]
    public partial string DeliveryAddress { get; set; } = string.Empty;

    /// <summary>Зона доставки.</summary>
    [ObservableProperty]
    public partial int DeliveryZoneId { get; set; }

    /// <summary>Тариф.</summary>
    [ObservableProperty]
    public partial int TariffId { get; set; }

    /// <summary>Вага, кг.</summary>
    [ObservableProperty]
    public partial string WeightGrams { get; set; } = string.Empty;

    /// <summary>Довжина, см.</summary>
    [ObservableProperty]
    public partial string LengthCm { get; set; } = string.Empty;

    /// <summary>Ширина, см.</summary>
    [ObservableProperty]
    public partial string WidthCm { get; set; } = string.Empty;

    /// <summary>Висота, см.</summary>
    [ObservableProperty]
    public partial string HeightCm { get; set; } = string.Empty;

    /// <summary>Оголошена цінність, грн.</summary>
    [ObservableProperty]
    public partial string DeclaredValue { get; set; }

    /// <summary>Чи крихка посилка.</summary>
    [ObservableProperty]
    public partial bool IsFragile { get; set; }

    /// <summary>Хто платить.</summary>
    [ObservableProperty]
    public partial PaymentPayer Payer { get; set; }

    /// <summary>Спосіб оплати.</summary>
    [ObservableProperty]
    public partial PaymentMethod Method { get; set; }

    /// <summary>Коментар.</summary>
    [ObservableProperty]
    public partial string Comment { get; set; } = string.Empty;

    private Tariff? SelectedTariff => _data.Tariffs.FirstOrDefault(t => t.Id == TariffId);

    /// <inheritdoc />
    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        if (e.PropertyName is not null && PriceTriggers.Contains(e.PropertyName))
        {
            Quote = CalculateQuote();
        }
    }

    /// <inheritdoc />
    protected override ValidationErrors Validate()
    {
        var parseErrors = new ValidationErrors();
        OrderInput input = BuildInput(parseErrors);
        ValidationErrors errors = CombineErrors(parseErrors, _validator.Validate(input, SelectedTariff));
        _validInput = errors.IsValid ? input : null;
        return errors;
    }

    /// <inheritdoc />
    protected override async Task SaveCoreAsync() =>
        Created = await _orders.CreateAsync(_validInput ?? throw new InvalidOperationException("Дані форми не перевірено."));

    partial void OnClientIdChanged(int value)
    {
        // Адреса, звідки забрати, підставляється з картки клієнта, якщо оператор не ввів іншу
        string? address = _data.Clients.FirstOrDefault(c => c.Id == value)?.Address;
        if (address is not null && (string.IsNullOrWhiteSpace(PickupAddress) || PickupAddress == _autoPickupAddress))
        {
            PickupAddress = address;
            _autoPickupAddress = address;
        }
    }

    partial void OnPayerChanged(PaymentPayer value)
    {
        if (!PaymentRules.IsAllowed(value, Method))
        {
            Method = PaymentMethod.Cash;
        }

        PaymentMethods = MethodsFor(value);
    }

    private static List<Option<PaymentMethod>> MethodsFor(PaymentPayer payer) =>
        PaymentRules.AllowedMethods(payer).Select(m => new Option<PaymentMethod>(m, m.ToDisplayName())).ToList();

    private OrderInput BuildInput(ValidationErrors parseErrors) => new(
        ClientId,
        RecipientName,
        RecipientPhone,
        PickupAddress,
        PickupZoneId,
        DeliveryAddress,
        DeliveryZoneId,
        TariffId,
        ParseKilograms(WeightGrams, nameof(WeightGrams), "Вага", parseErrors),
        ParseInt(LengthCm, nameof(LengthCm), "Довжина", parseErrors),
        ParseInt(WidthCm, nameof(WidthCm), "Ширина", parseErrors),
        ParseInt(HeightCm, nameof(HeightCm), "Висота", parseErrors),
        // Порожня цінність означає «не оголошено»
        string.IsNullOrWhiteSpace(DeclaredValue)
            ? Money.Zero
            : ParseMoney(DeclaredValue, nameof(DeclaredValue), "Оголошена цінність", parseErrors),
        IsFragile,
        Payer,
        Method,
        Comment);

    private PriceQuote CalculateQuote()
    {
        Tariff? tariff = SelectedTariff;
        Zone? pickupZone = _data.Zones.FirstOrDefault(z => z.Id == PickupZoneId);
        Zone? deliveryZone = _data.Zones.FirstOrDefault(z => z.Id == DeliveryZoneId);
        if (tariff is null || pickupZone is null || deliveryZone is null)
        {
            return PriceQuote.NotReady("Оберіть тариф і обидві зони (звідки забрати й куди доставити) — вартість порахується автоматично.");
        }

        if (new[] { WeightGrams, LengthCm, WidthCm, HeightCm }.Any(string.IsNullOrWhiteSpace))
        {
            return PriceQuote.NotReady("Вкажіть вагу й габарити посилки.");
        }

        var parseErrors = new ValidationErrors();
        OrderInput input = BuildInput(parseErrors);
        ValidationErrors errors = CombineErrors(parseErrors, _validator.Validate(input, tariff));
        string? problem = OrderValidator.PriceFields.SelectMany(field => errors[field]).FirstOrDefault();
        if (problem is not null)
        {
            return PriceQuote.NotReady(problem);
        }

        PriceBreakdown price = _calculator.Calculate(tariff, pickupZone, deliveryZone, input.Parcel);
        DateTime deadline = _deadlines.CalculateDeadline(_clock.Now, tariff.DeliveryHours);
        return PriceQuote.From(price, tariff, input.Parcel, deadline);
    }
}
