using CommunityToolkit.Mvvm.ComponentModel;
using CourierService.App.Services;
using CourierService.Core.Directories;
using CourierService.Core.Entities;
using CourierService.Core.Enums;
using CourierService.Core.Security;
using CourierService.Core.Validation;

namespace CourierService.App.ViewModels.Directories;

/// <summary>
/// Довідник зон доставки (райони Києва). Змінює адміністратор, оператор лише переглядає.
/// </summary>
public sealed class ZonesViewModel : ReferenceListViewModel<Zone>
{
    private readonly IZoneService _service;
    private readonly IValidator<ZoneInput> _validator;

    /// <summary>
    /// Створює сторінку.
    /// </summary>
    /// <param name="service">Сервіс зон.</param>
    /// <param name="validator">Правила зони.</param>
    /// <param name="dialogs">Повідомлення.</param>
    /// <param name="editDialogs">Вікно редагування.</param>
    /// <param name="currentUser">Поточний користувач.</param>
    public ZonesViewModel(
        IZoneService service,
        IValidator<ZoneInput> validator,
        IDialogService dialogs,
        IEditDialogService editDialogs,
        ICurrentUser currentUser)
        : base(dialogs, editDialogs, currentUser)
    {
        _service = service;
        _validator = validator;
    }

    /// <inheritdoc />
    public override string Title => "Зони доставки";

    /// <inheritdoc />
    protected override UserRole[] EditRoles => [UserRole.Admin];

    /// <inheritdoc />
    protected override Task<IReadOnlyList<Zone>> LoadItemsAsync() => _service.GetAllAsync();

    /// <inheritdoc />
    protected override bool Matches(Zone item, string search) =>
        TextSearch.Matches(search, item.Name, item.Bank.ToDisplayName());

    /// <inheritdoc />
    protected override EditViewModelBase CreateEditor(Zone? item) => new ZoneEditorViewModel(_service, _validator, item);

    /// <inheritdoc />
    protected override Task SetActiveAsync(int id, bool isActive) => _service.SetActiveAsync(id, isActive);

    /// <inheritdoc />
    protected override string Describe(Zone item) => item.Name;
}

/// <summary>
/// Форма зони доставки.
/// </summary>
public sealed partial class ZoneEditorViewModel : EditViewModelBase<ZoneInput>
{
    private readonly IZoneService _service;

    /// <summary>
    /// Створює форму.
    /// </summary>
    /// <param name="service">Сервіс зон.</param>
    /// <param name="validator">Правила зони.</param>
    /// <param name="zone">Зона для редагування або <c>null</c> для нової.</param>
    public ZoneEditorViewModel(IZoneService service, IValidator<ZoneInput> validator, Zone? zone)
        : base(validator, zone?.Id)
    {
        _service = service;
        Name = zone?.Name ?? string.Empty;
        Bank = zone?.Bank ?? RiverBank.Right;
        CoefficientPercent = InputParsing.FormatCoefficient(zone?.CoefficientPercent ?? 100);
    }

    /// <inheritdoc />
    public override string Title => IsNew ? "Нова зона" : "Зміна зони";

    /// <summary>Варіанти берега.</summary>
    public IReadOnlyList<Option<RiverBank>> Banks => Options.Banks;

    /// <summary>Назва району.</summary>
    [ObservableProperty]
    public partial string Name { get; set; }

    /// <summary>Берег Дніпра.</summary>
    [ObservableProperty]
    public partial RiverBank Bank { get; set; }

    /// <summary>Коефіцієнт як текст («1,15»); назва збігається з полем <see cref="ZoneInput"/> для показу помилок.</summary>
    [ObservableProperty]
    public partial string CoefficientPercent { get; set; }

    /// <inheritdoc />
    protected override ZoneInput BuildInput(ValidationErrors parseErrors)
    {
        if (!InputParsing.TryParseCoefficient(CoefficientPercent, out int percent))
        {
            parseErrors.Add(nameof(CoefficientPercent), "Коефіцієнт має бути числом, наприклад 1,15.");
        }

        return new ZoneInput(Id, Name, Bank, percent);
    }

    /// <inheritdoc />
    protected override Task SaveInputAsync(ZoneInput input) => _service.SaveAsync(input);
}
