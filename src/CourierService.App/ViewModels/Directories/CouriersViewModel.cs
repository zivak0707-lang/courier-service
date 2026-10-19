using CommunityToolkit.Mvvm.ComponentModel;
using CourierService.App.Services;
using CourierService.Core.Directories;
using CourierService.Core.Entities;
using CourierService.Core.Enums;
using CourierService.Core.Security;
using CourierService.Core.Validation;

namespace CourierService.App.ViewModels.Directories;

/// <summary>
/// Довідник кур'єрів. Змінює оператор, адміністратор лише переглядає.
/// </summary>
public sealed class CouriersViewModel : ReferenceListViewModel<CourierListItem>
{
    private readonly ICourierService _service;
    private readonly IZoneService _zoneService;
    private readonly IValidator<CourierInput> _validator;
    private IReadOnlyList<Zone> _zones = [];

    /// <summary>
    /// Створює сторінку.
    /// </summary>
    /// <param name="service">Сервіс кур'єрів.</param>
    /// <param name="zoneService">Сервіс зон (для вибору домашньої зони).</param>
    /// <param name="validator">Правила кур'єра.</param>
    /// <param name="dialogs">Повідомлення.</param>
    /// <param name="editDialogs">Вікно редагування.</param>
    /// <param name="currentUser">Поточний користувач.</param>
    public CouriersViewModel(
        ICourierService service,
        IZoneService zoneService,
        IValidator<CourierInput> validator,
        IDialogService dialogs,
        IEditDialogService editDialogs,
        ICurrentUser currentUser)
        : base(dialogs, editDialogs, currentUser)
    {
        _service = service;
        _zoneService = zoneService;
        _validator = validator;
    }

    /// <inheritdoc />
    public override string Title => "Кур'єри";

    /// <inheritdoc />
    protected override UserRole[] EditRoles => [UserRole.Operator];

    /// <inheritdoc />
    protected override async Task<IReadOnlyList<CourierListItem>> LoadItemsAsync()
    {
        // Зони потрібні формі для вибору домашньої зони
        _zones = await _zoneService.GetAllAsync();
        return await _service.GetAllAsync();
    }

    /// <inheritdoc />
    protected override bool Matches(CourierListItem item, string search) =>
        TextSearch.MatchesPhone(search, item.Phone)
        || TextSearch.Matches(search, item.FullName, item.Login, item.HomeZoneName);

    /// <inheritdoc />
    protected override EditViewModelBase CreateEditor(CourierListItem? item)
    {
        // Пропонуються активні зони; поточна зона кур'єра лишається у списку, навіть якщо її деактивовано
        var zones = _zones
            .Where(zone => zone.IsActive || zone.Id == item?.HomeZoneId)
            .Select(zone => new Option<int>(zone.Id, zone.IsActive ? zone.Name : zone.Name + " (неактивна)"))
            .ToList();
        return new CourierEditorViewModel(_service, _validator, zones, item);
    }

    /// <inheritdoc />
    protected override Task SetActiveAsync(int id, bool isActive) => _service.SetActiveAsync(id, isActive);

    /// <inheritdoc />
    protected override string Describe(CourierListItem item) => item.FullName;
}

/// <summary>
/// Форма кур'єра разом з обліковим записом для входу.
/// </summary>
public sealed partial class CourierEditorViewModel : EditViewModelBase<CourierInput>
{
    private readonly ICourierService _service;

    /// <summary>
    /// Створює форму.
    /// </summary>
    /// <param name="service">Сервіс кур'єрів.</param>
    /// <param name="validator">Правила кур'єра.</param>
    /// <param name="zones">Варіанти домашньої зони.</param>
    /// <param name="courier">Кур'єр для редагування або <c>null</c> для нового.</param>
    public CourierEditorViewModel(
        ICourierService service,
        IValidator<CourierInput> validator,
        IReadOnlyList<Option<int>> zones,
        CourierListItem? courier)
        : base(validator, courier?.Id)
    {
        _service = service;
        Zones = zones;
        FullName = courier?.FullName ?? string.Empty;
        Login = courier?.Login ?? string.Empty;
        Phone = courier is null ? string.Empty : PhoneNumber.Format(courier.Phone);
        TransportType = courier?.TransportType ?? TransportType.Car;
        MaxWeightGrams = InputParsing.FormatKilograms(courier?.MaxWeightGrams ?? 30_000);
        HomeZoneId = courier?.HomeZoneId ?? 0;
        PayoutPercent = (courier?.PayoutPercent ?? 50).ToString();

        ErrorsChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(Password))
            {
                OnPropertyChanged(nameof(PasswordError));
            }
        };
    }

    /// <inheritdoc />
    public override string Title => IsNew ? "Новий кур'єр" : "Зміна кур'єра";

    /// <summary>Варіанти домашньої зони.</summary>
    public IReadOnlyList<Option<int>> Zones { get; }

    /// <summary>Варіанти транспорту.</summary>
    public IReadOnlyList<Option<TransportType>> TransportTypes => Options.TransportTypes;

    /// <summary>Підпис поля пароля.</summary>
    public string PasswordLabel => IsNew
        ? "Пароль для входу (щонайменше 8 символів, літери й цифри)"
        : "Новий пароль (залиште порожнім, щоб не змінювати)";

    /// <summary>Помилка пароля (PasswordBox не показує помилки сам).</summary>
    public string? PasswordError => FirstError(nameof(Password));

    /// <summary>ПІБ.</summary>
    [ObservableProperty]
    public partial string FullName { get; set; }

    /// <summary>Логін для входу.</summary>
    [ObservableProperty]
    public partial string Login { get; set; }

    /// <summary>Пароль (передається з PasswordBox у коді вікна).</summary>
    [ObservableProperty]
    public partial string Password { get; set; } = string.Empty;

    /// <summary>Телефон.</summary>
    [ObservableProperty]
    public partial string Phone { get; set; }

    /// <summary>Вид транспорту.</summary>
    [ObservableProperty]
    public partial TransportType TransportType { get; set; }

    /// <summary>Вантажопідйомність, кг.</summary>
    [ObservableProperty]
    public partial string MaxWeightGrams { get; set; }

    /// <summary>Домашня зона.</summary>
    [ObservableProperty]
    public partial int HomeZoneId { get; set; }

    /// <summary>Відсоток виплати від вартості доставки.</summary>
    [ObservableProperty]
    public partial string PayoutPercent { get; set; }

    /// <inheritdoc />
    protected override CourierInput BuildInput(ValidationErrors parseErrors) => new(
        Id,
        FullName,
        Login,
        Password,
        Phone,
        TransportType,
        ParseKilograms(MaxWeightGrams, nameof(MaxWeightGrams), "Вантажопідйомність", parseErrors),
        HomeZoneId,
        ParseInt(PayoutPercent, nameof(PayoutPercent), "Відсоток виплати", parseErrors));

    /// <inheritdoc />
    protected override Task SaveInputAsync(CourierInput input) => _service.SaveAsync(input);
}
