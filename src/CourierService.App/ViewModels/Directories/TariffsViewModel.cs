using CommunityToolkit.Mvvm.ComponentModel;
using CourierService.App.Services;
using CourierService.Core;
using CourierService.Core.Directories;
using CourierService.Core.Entities;
using CourierService.Core.Enums;
using CourierService.Core.Security;
using CourierService.Core.Validation;

namespace CourierService.App.ViewModels.Directories;

/// <summary>
/// Довідник тарифів. Змінює адміністратор, оператор лише переглядає.
/// </summary>
public sealed class TariffsViewModel : ReferenceListViewModel<Tariff>
{
    private readonly ITariffService _service;
    private readonly IValidator<TariffInput> _validator;

    /// <summary>
    /// Створює сторінку.
    /// </summary>
    /// <param name="service">Сервіс тарифів.</param>
    /// <param name="validator">Правила тарифу.</param>
    /// <param name="dialogs">Повідомлення.</param>
    /// <param name="editDialogs">Вікно редагування.</param>
    /// <param name="currentUser">Поточний користувач.</param>
    public TariffsViewModel(
        ITariffService service,
        IValidator<TariffInput> validator,
        IDialogService dialogs,
        IEditDialogService editDialogs,
        ICurrentUser currentUser)
        : base(dialogs, editDialogs, currentUser)
    {
        _service = service;
        _validator = validator;
    }

    /// <inheritdoc />
    public override string Title => "Тарифи";

    /// <inheritdoc />
    protected override UserRole[] EditRoles => [UserRole.Admin];

    /// <inheritdoc />
    protected override Task<IReadOnlyList<Tariff>> LoadItemsAsync() => _service.GetAllAsync();

    /// <inheritdoc />
    protected override bool Matches(Tariff item, string search) => TextSearch.Matches(search, item.Name);

    /// <inheritdoc />
    protected override EditViewModelBase CreateEditor(Tariff? item) => new TariffEditorViewModel(_service, _validator, item);

    /// <inheritdoc />
    protected override Task SetActiveAsync(int id, bool isActive) => _service.SetActiveAsync(id, isActive);

    /// <inheritdoc />
    protected override string Describe(Tariff item) => item.Name;
}

/// <summary>
/// Форма тарифу. Поля-рядки мають ті самі назви, що й <see cref="TariffInput"/>, —
/// так помилки валідатора з'являються біля відповідних полів.
/// </summary>
public sealed partial class TariffEditorViewModel : EditViewModelBase<TariffInput>
{
    private readonly ITariffService _service;

    /// <summary>
    /// Створює форму.
    /// </summary>
    /// <param name="service">Сервіс тарифів.</param>
    /// <param name="validator">Правила тарифу.</param>
    /// <param name="tariff">Тариф для редагування або <c>null</c> для нового.</param>
    public TariffEditorViewModel(ITariffService service, IValidator<TariffInput> validator, Tariff? tariff)
        : base(validator, tariff?.Id)
    {
        _service = service;
        Name = tariff?.Name ?? string.Empty;
        BasePrice = Format(tariff?.BasePrice);
        IncludedWeightGrams = InputParsing.FormatKilograms(tariff?.IncludedWeightGrams ?? 2_000);
        PricePerExtraKg = Format(tariff?.PricePerExtraKg);
        MaxWeightGrams = InputParsing.FormatKilograms(tariff?.MaxWeightGrams ?? 30_000);
        DeliveryHours = (tariff?.DeliveryHours ?? 12).ToString();
        CrossRiverSurcharge = Format(tariff?.CrossRiverSurcharge);
        InsuranceFreeLimit = Format(tariff?.InsuranceFreeLimit ?? Money.FromHryvnias(500m));
        InsurancePermille = (tariff?.InsurancePermille ?? 5).ToString();
        FragileSurcharge = Format(tariff?.FragileSurcharge);
    }

    /// <inheritdoc />
    public override string Title => IsNew ? "Новий тариф" : "Зміна тарифу";

    /// <summary>Назва.</summary>
    [ObservableProperty]
    public partial string Name { get; set; }

    /// <summary>Базова ціна, грн.</summary>
    [ObservableProperty]
    public partial string BasePrice { get; set; }

    /// <summary>Включена вага, кг.</summary>
    [ObservableProperty]
    public partial string IncludedWeightGrams { get; set; }

    /// <summary>Ціна за кожен додатковий кг, грн.</summary>
    [ObservableProperty]
    public partial string PricePerExtraKg { get; set; }

    /// <summary>Максимальна вага, кг.</summary>
    [ObservableProperty]
    public partial string MaxWeightGrams { get; set; }

    /// <summary>Термін, робочих годин.</summary>
    [ObservableProperty]
    public partial string DeliveryHours { get; set; }

    /// <summary>Доплата за переїзд через Дніпро, грн.</summary>
    [ObservableProperty]
    public partial string CrossRiverSurcharge { get; set; }

    /// <summary>Цінність без страхової комісії, грн.</summary>
    [ObservableProperty]
    public partial string InsuranceFreeLimit { get; set; }

    /// <summary>Ставка страхування, ‰.</summary>
    [ObservableProperty]
    public partial string InsurancePermille { get; set; }

    /// <summary>Доплата за крихкість, грн.</summary>
    [ObservableProperty]
    public partial string FragileSurcharge { get; set; }

    /// <inheritdoc />
    protected override TariffInput BuildInput(ValidationErrors parseErrors) => new(
        Id,
        Name,
        ParseMoney(BasePrice, nameof(BasePrice), "Базова ціна", parseErrors),
        ParseKilograms(IncludedWeightGrams, nameof(IncludedWeightGrams), "Включена вага", parseErrors),
        ParseMoney(PricePerExtraKg, nameof(PricePerExtraKg), "Ціна за додатковий кг", parseErrors),
        ParseKilograms(MaxWeightGrams, nameof(MaxWeightGrams), "Максимальна вага", parseErrors),
        ParseInt(DeliveryHours, nameof(DeliveryHours), "Термін", parseErrors),
        ParseMoney(CrossRiverSurcharge, nameof(CrossRiverSurcharge), "Доплата за переїзд через Дніпро", parseErrors),
        ParseMoney(InsuranceFreeLimit, nameof(InsuranceFreeLimit), "Цінність без страхування", parseErrors),
        ParseInt(InsurancePermille, nameof(InsurancePermille), "Ставка страхування", parseErrors),
        ParseMoney(FragileSurcharge, nameof(FragileSurcharge), "Доплата за крихкість", parseErrors));

    /// <inheritdoc />
    protected override Task SaveInputAsync(TariffInput input) => _service.SaveAsync(input);

    private static string Format(Money? money) => money is { } value ? InputParsing.FormatMoneyForInput(value) : string.Empty;
}
