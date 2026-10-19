using CommunityToolkit.Mvvm.ComponentModel;
using CourierService.App.Services;
using CourierService.Core.Directories;
using CourierService.Core.Entities;
using CourierService.Core.Enums;
using CourierService.Core.Security;
using CourierService.Core.Validation;

namespace CourierService.App.ViewModels.Directories;

/// <summary>
/// Довідник клієнтів-відправників. Змінює оператор, адміністратор лише переглядає.
/// </summary>
public sealed class ClientsViewModel : ReferenceListViewModel<Client>
{
    private readonly IClientService _service;
    private readonly IValidator<ClientInput> _validator;

    /// <summary>
    /// Створює сторінку.
    /// </summary>
    /// <param name="service">Сервіс клієнтів.</param>
    /// <param name="validator">Правила клієнта.</param>
    /// <param name="dialogs">Повідомлення.</param>
    /// <param name="editDialogs">Вікно редагування.</param>
    /// <param name="currentUser">Поточний користувач.</param>
    public ClientsViewModel(
        IClientService service,
        IValidator<ClientInput> validator,
        IDialogService dialogs,
        IEditDialogService editDialogs,
        ICurrentUser currentUser)
        : base(dialogs, editDialogs, currentUser)
    {
        _service = service;
        _validator = validator;
    }

    /// <inheritdoc />
    public override string Title => "Клієнти";

    /// <inheritdoc />
    protected override UserRole[] EditRoles => [UserRole.Operator];

    /// <inheritdoc />
    protected override Task<IReadOnlyList<Client>> LoadItemsAsync() => _service.GetAllAsync();

    /// <inheritdoc />
    protected override bool Matches(Client item, string search) =>
        TextSearch.MatchesPhone(search, item.Phone)
        || TextSearch.Matches(search, item.Name, item.Email, item.Address);

    /// <inheritdoc />
    protected override EditViewModelBase CreateEditor(Client? item) => new ClientEditorViewModel(_service, _validator, item);

    /// <inheritdoc />
    protected override Task SetActiveAsync(int id, bool isActive) => _service.SetActiveAsync(id, isActive);

    /// <inheritdoc />
    protected override string Describe(Client item) => item.Name;
}

/// <summary>
/// Форма клієнта.
/// </summary>
public sealed partial class ClientEditorViewModel : EditViewModelBase<ClientInput>
{
    private readonly IClientService _service;

    /// <summary>
    /// Створює форму.
    /// </summary>
    /// <param name="service">Сервіс клієнтів.</param>
    /// <param name="validator">Правила клієнта.</param>
    /// <param name="client">Клієнт для редагування або <c>null</c> для нового.</param>
    public ClientEditorViewModel(IClientService service, IValidator<ClientInput> validator, Client? client)
        : base(validator, client?.Id)
    {
        _service = service;
        ClientType = client?.ClientType ?? ClientType.Individual;
        Name = client?.Name ?? string.Empty;
        Phone = client is null ? string.Empty : PhoneNumber.Format(client.Phone);
        Email = client?.Email ?? string.Empty;
        Address = client?.Address ?? string.Empty;
        Notes = client?.Notes ?? string.Empty;
    }

    /// <inheritdoc />
    public override string Title => IsNew ? "Новий клієнт" : "Зміна клієнта";

    /// <summary>Варіанти типу клієнта.</summary>
    public IReadOnlyList<Option<ClientType>> ClientTypes => Options.ClientTypes;

    /// <summary>Фізособа чи компанія.</summary>
    [ObservableProperty]
    public partial ClientType ClientType { get; set; }

    /// <summary>ПІБ або назва компанії.</summary>
    [ObservableProperty]
    public partial string Name { get; set; }

    /// <summary>Телефон у будь-якому зручному форматі.</summary>
    [ObservableProperty]
    public partial string Phone { get; set; }

    /// <summary>Електронна пошта.</summary>
    [ObservableProperty]
    public partial string Email { get; set; }

    /// <summary>Адреса забору за замовчуванням.</summary>
    [ObservableProperty]
    public partial string Address { get; set; }

    /// <summary>Примітки.</summary>
    [ObservableProperty]
    public partial string Notes { get; set; }

    /// <inheritdoc />
    protected override ClientInput BuildInput(ValidationErrors parseErrors) =>
        new(Id, ClientType, Name, Phone, Email, Address, Notes);

    /// <inheritdoc />
    protected override Task SaveInputAsync(ClientInput input) => _service.SaveAsync(input);
}
