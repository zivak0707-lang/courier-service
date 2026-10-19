using System.Globalization;
using CourierService.Core.Directories;
using CourierService.Core.Entities;
using CourierService.Core.Enums;
using CourierService.Core.Pricing;
using CourierService.Core.Validation;

namespace CourierService.Core.Orders;

/// <summary>
/// Дані форми нового замовлення.
/// </summary>
/// <param name="ClientId">Відправник (клієнт з довідника).</param>
/// <param name="RecipientName">ПІБ одержувача.</param>
/// <param name="RecipientPhone">Телефон одержувача в будь-якому допустимому форматі.</param>
/// <param name="PickupAddress">Адреса, звідки забрати посилку.</param>
/// <param name="PickupZoneId">Зона, звідки забрати.</param>
/// <param name="DeliveryAddress">Адреса доставки.</param>
/// <param name="DeliveryZoneId">Зона доставки.</param>
/// <param name="TariffId">Тариф.</param>
/// <param name="WeightGrams">Фактична вага, г.</param>
/// <param name="LengthCm">Довжина, см.</param>
/// <param name="WidthCm">Ширина, см.</param>
/// <param name="HeightCm">Висота, см.</param>
/// <param name="DeclaredValue">Оголошена цінність.</param>
/// <param name="IsFragile">Чи крихка посилка.</param>
/// <param name="Payer">Хто платить за доставку.</param>
/// <param name="Method">Спосіб оплати.</param>
/// <param name="Comment">Коментар (необов'язково).</param>
public sealed record OrderInput(
    int ClientId,
    string RecipientName,
    string RecipientPhone,
    string PickupAddress,
    int PickupZoneId,
    string DeliveryAddress,
    int DeliveryZoneId,
    int TariffId,
    int WeightGrams,
    int LengthCm,
    int WidthCm,
    int HeightCm,
    Money DeclaredValue,
    bool IsFragile,
    PaymentPayer Payer,
    PaymentMethod Method,
    string? Comment)
{
    /// <summary>Параметри посилки для калькулятора вартості.</summary>
    public ParcelParameters Parcel => new(WeightGrams, LengthCm, WidthCm, HeightCm, DeclaredValue, IsFragile);
}

/// <summary>
/// Результат оформлення замовлення.
/// </summary>
/// <param name="Id">Ідентифікатор замовлення.</param>
/// <param name="TrackingNumber">Трек-номер.</param>
/// <param name="Price">Вартість доставки.</param>
/// <param name="Deadline">Крайній термін доставки.</param>
public sealed record OrderCreated(int Id, string TrackingNumber, Money Price, DateTime Deadline);

/// <summary>
/// Які способи оплати допустимі для платника (рішення D-09).
/// </summary>
public static class PaymentRules
{
    /// <summary>
    /// Способи оплати для платника: одержувач — готівка або картка кур'єру,
    /// відправник — також безготівковий розрахунок.
    /// </summary>
    /// <param name="payer">Платник.</param>
    /// <returns>Допустимі способи.</returns>
    public static IReadOnlyList<PaymentMethod> AllowedMethods(PaymentPayer payer) => payer == PaymentPayer.Recipient
        ? [PaymentMethod.Cash, PaymentMethod.Card]
        : [PaymentMethod.Cash, PaymentMethod.Card, PaymentMethod.BankTransfer];

    /// <summary>
    /// Чи допустимий спосіб оплати для платника.
    /// </summary>
    /// <param name="payer">Платник.</param>
    /// <param name="method">Спосіб оплати.</param>
    /// <returns><c>true</c>, якщо поєднання дозволене.</returns>
    public static bool IsAllowed(PaymentPayer payer, PaymentMethod method) => AllowedMethods(payer).Contains(method);
}

/// <summary>
/// Правила нового замовлення. Обмеження, що залежать від тарифу (максимальна вага),
/// перевіряє перевантаження з тарифом — його викликають і форма, і сервіс.
/// </summary>
public sealed class OrderValidator : IValidator<OrderInput>
{
    /// <summary>Максимальна довжина ПІБ одержувача.</summary>
    public const int RecipientNameMaxLength = 100;

    /// <summary>Максимальна довжина коментаря.</summary>
    public const int CommentMaxLength = 500;

    /// <summary>Найбільша оголошена цінність.</summary>
    public static readonly Money MaxDeclaredValue = Money.FromHryvnias(1_000_000m);

    private static readonly CultureInfo Ukrainian = CultureInfo.GetCultureInfo("uk-UA");

    /// <inheritdoc />
    public ValidationErrors Validate(OrderInput input) => Validate(input, tariff: null);

    /// <summary>
    /// Перевіряє замовлення разом з обмеженнями обраного тарифу.
    /// </summary>
    /// <param name="input">Дані форми.</param>
    /// <param name="tariff">Обраний тариф або <c>null</c>, якщо ще не відомий.</param>
    /// <returns>Помилки за полями.</returns>
    public ValidationErrors Validate(OrderInput input, Tariff? tariff)
    {
        ArgumentNullException.ThrowIfNull(input);
        var errors = new ValidationErrors();

        Selected(errors, nameof(input.ClientId), input.ClientId, "Оберіть відправника.");
        errors.RequiredText(nameof(input.RecipientName), input.RecipientName, "Одержувач", RecipientNameMaxLength);
        errors.Phone(nameof(input.RecipientPhone), input.RecipientPhone);
        errors.RequiredText(nameof(input.PickupAddress), input.PickupAddress, "Звідки забрати", Order.AddressMaxLength);
        Selected(errors, nameof(input.PickupZoneId), input.PickupZoneId, "Оберіть зону, звідки забрати.");
        errors.RequiredText(nameof(input.DeliveryAddress), input.DeliveryAddress, "Куди доставити", Order.AddressMaxLength);
        Selected(errors, nameof(input.DeliveryZoneId), input.DeliveryZoneId, "Оберіть зону, куди доставити.");
        Selected(errors, nameof(input.TariffId), input.TariffId, "Оберіть тариф.");

        errors.InRange(nameof(input.WeightGrams), input.WeightGrams, 1, TariffValidator.MaxParcelWeightGrams,
            "Вага має бути від 0,001 до 100 кг.");
        string sideMessage = $"Кожна сторона — від 1 до {PriceCalculator.MaxSideCm} см.";
        errors.InRange(nameof(input.LengthCm), input.LengthCm, 1, PriceCalculator.MaxSideCm, sideMessage);
        errors.InRange(nameof(input.WidthCm), input.WidthCm, 1, PriceCalculator.MaxSideCm, sideMessage);
        errors.InRange(nameof(input.HeightCm), input.HeightCm, 1, PriceCalculator.MaxSideCm, sideMessage);
        errors.MoneyInRange(nameof(input.DeclaredValue), input.DeclaredValue, Money.Zero, MaxDeclaredValue,
            "Оголошена цінність");

        errors.Defined(nameof(input.Payer), input.Payer, "Хто платить");
        errors.Defined(nameof(input.Method), input.Method, "Спосіб оплати");
        if (Enum.IsDefined(input.Payer) && Enum.IsDefined(input.Method) && !PaymentRules.IsAllowed(input.Payer, input.Method))
        {
            errors.Add(nameof(input.Method), "Безготівковий розрахунок доступний лише відправнику.");
        }

        errors.OptionalText(nameof(input.Comment), input.Comment, "Коментар", CommentMaxLength);

        // Обмеження тарифу має сенс, лише коли вага й габарити самі по собі правильні
        if (tariff is not null && ParcelFields.All(field => errors[field].Count == 0))
        {
            CheckTariffWeight(errors, input, tariff);
        }

        return errors;
    }

    /// <summary>Поля, від яких залежить вартість доставки (для живого перерахунку у формі).</summary>
    public static IReadOnlyList<string> PriceFields { get; } =
    [
        nameof(OrderInput.PickupZoneId), nameof(OrderInput.DeliveryZoneId), nameof(OrderInput.TariffId),
        nameof(OrderInput.WeightGrams), nameof(OrderInput.LengthCm), nameof(OrderInput.WidthCm),
        nameof(OrderInput.HeightCm), nameof(OrderInput.DeclaredValue),
    ];

    private static IReadOnlyList<string> ParcelFields { get; } =
    [
        nameof(OrderInput.WeightGrams), nameof(OrderInput.LengthCm), nameof(OrderInput.WidthCm),
        nameof(OrderInput.HeightCm),
    ];

    private static void CheckTariffWeight(ValidationErrors errors, OrderInput input, Tariff tariff)
    {
        int chargeable = PriceCalculator.ChargeableWeightGrams(input.Parcel);
        if (chargeable <= tariff.MaxWeightGrams)
        {
            return;
        }

        string limit = $"максимум тарифу «{tariff.Name}» — {Kilograms(tariff.MaxWeightGrams)} кг";
        string message = chargeable > input.WeightGrams
            ? $"Об'ємна вага {Kilograms(chargeable)} кг (Д × Ш × В / 4000) перевищує {limit}. Оберіть інший тариф або зменшіть габарити."
            : $"Вага перевищує {limit}. Оберіть інший тариф.";
        errors.Add(nameof(input.WeightGrams), message);
    }

    private static void Selected(ValidationErrors errors, string property, int id, string message)
    {
        if (id <= 0)
        {
            errors.Add(property, message);
        }
    }

    private static string Kilograms(int grams) => (grams / 1000m).ToString("0.###", Ukrainian);
}

/// <summary>
/// Робота із замовленнями: оформлення, пошук.
/// </summary>
public interface IOrderService
{
    /// <summary>
    /// Оформлює замовлення: перевіряє дані й права, рахує вартість і дедлайн, призначає трек-номер,
    /// створює оплату (очікує) і перший запис історії статусів. Доступно оператору.
    /// </summary>
    /// <param name="input">Дані форми.</param>
    /// <param name="cancellationToken">Скасування операції.</param>
    /// <returns>Трек-номер, вартість і дедлайн нового замовлення.</returns>
    Task<OrderCreated> CreateAsync(OrderInput input, CancellationToken cancellationToken = default);

    /// <summary>
    /// Шукає замовлення за умовами фільтра в БД (рішення D-20). Правильний трек-номер шукається
    /// за весь час без інших умов. Доступно адміністратору й оператору.
    /// </summary>
    /// <param name="filter">Умови пошуку.</param>
    /// <param name="cancellationToken">Скасування операції.</param>
    /// <returns>Не більше <see cref="OrderFilter.MaxResults"/> замовлень, новіші першими.</returns>
    Task<OrderSearchResult> SearchAsync(OrderFilter filter, CancellationToken cancellationToken = default);

    /// <summary>
    /// Повні дані замовлення для картки: відправник, зони, тариф, кур'єр, оплата, історія статусів.
    /// Адміністратор і оператор бачать будь-яке замовлення, кур'єр — лише призначене йому.
    /// </summary>
    /// <param name="orderId">Замовлення.</param>
    /// <param name="cancellationToken">Скасування операції.</param>
    /// <returns>Замовлення з пов'язаними даними (лише для читання).</returns>
    Task<Order> GetOrderAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Доставки поточного кур'єра: активні (призначені й забрані, найтерміновіші першими)
    /// або решта (історія, новіші першими).
    /// </summary>
    /// <param name="active">Активні чи виконані.</param>
    /// <param name="cancellationToken">Скасування операції.</param>
    /// <returns>Доставки кур'єра.</returns>
    Task<IReadOnlyList<OrderListItem>> GetMyDeliveriesAsync(bool active, CancellationToken cancellationToken = default);
}
