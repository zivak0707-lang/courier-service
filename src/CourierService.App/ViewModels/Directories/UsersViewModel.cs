using CommunityToolkit.Mvvm.ComponentModel;
using CourierService.App.Services;
using CourierService.Core.Directories;
using CourierService.Core.Enums;
using CourierService.Core.Security;
using CourierService.Core.Validation;

namespace CourierService.App.ViewModels.Directories;

/// <summary>
/// Керування обліковими записами (лише адміністратор).
/// </summary>
public sealed class UsersViewModel : ReferenceListViewModel<UserListItem>
{
    private readonly IUserService _service;
    private readonly IValidator<UserInput> _validator;

    /// <summary>
    /// Створює сторінку.
    /// </summary>
    /// <param name="service">Сервіс користувачів.</param>
    /// <param name="validator">Правила облікового запису.</param>
    /// <param name="dialogs">Повідомлення.</param>
    /// <param name="editDialogs">Вікно редагування.</param>
    /// <param name="currentUser">Поточний користувач.</param>
    public UsersViewModel(
        IUserService service,
        IValidator<UserInput> validator,
        IDialogService dialogs,
        IEditDialogService editDialogs,
        ICurrentUser currentUser)
        : base(dialogs, editDialogs, currentUser)
    {
        _service = service;
        _validator = validator;
    }

    /// <inheritdoc />
    public override string Title => "Користувачі";

    /// <inheritdoc />
    protected override UserRole[] EditRoles => [UserRole.Admin];

    /// <inheritdoc />
    protected override Task<IReadOnlyList<UserListItem>> LoadItemsAsync() => _service.GetAllAsync();

    /// <inheritdoc />
    protected override bool Matches(UserListItem item, string search) =>
        TextSearch.Matches(search, item.FullName, item.Login, item.Role.ToDisplayName());

    /// <inheritdoc />
    protected override EditViewModelBase CreateEditor(UserListItem? item) =>
        new UserEditorViewModel(_service, _validator, item, isOwnAccount: item?.Id == CurrentUser.Session?.UserId);

    /// <inheritdoc />
    protected override Task SetActiveAsync(int id, bool isActive) => _service.SetActiveAsync(id, isActive);

    /// <inheritdoc />
    protected override string Describe(UserListItem item) => $"{item.FullName} ({item.Login})";
}

/// <summary>
/// Форма облікового запису: ПІБ, логін, роль, новий пароль.
/// </summary>
public sealed partial class UserEditorViewModel : EditViewModelBase<UserInput>
{
    private readonly IUserService _service;

    /// <summary>
    /// Створює форму.
    /// </summary>
    /// <param name="service">Сервіс користувачів.</param>
    /// <param name="validator">Правила облікового запису.</param>
    /// <param name="user">Користувач для редагування або <c>null</c> для нового.</param>
    /// <param name="isOwnAccount">Чи редагує адміністратор власний обліковий запис.</param>
    public UserEditorViewModel(IUserService service, IValidator<UserInput> validator, UserListItem? user, bool isOwnAccount)
        : base(validator, user?.Id)
    {
        _service = service;
        FullName = user?.FullName ?? string.Empty;
        Login = user?.Login ?? string.Empty;
        Role = user?.Role ?? UserRole.Operator;

        bool isCourier = user?.Role == UserRole.Courier;
        Roles = isCourier ? [new Option<UserRole>(UserRole.Courier, UserRole.Courier.ToDisplayName())] : Options.AssignableRoles;
        IsRoleEditable = !isCourier && !isOwnAccount;
        RoleHint = isCourier ? "Роль кур'єра змінити не можна; профіль кур'єра — у довіднику «Кур'єри»."
            : isOwnAccount ? "Власну роль змінити не можна."
            : "Кур'єрів створюють у довіднику «Кур'єри».";

        ErrorsChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(Password))
            {
                OnPropertyChanged(nameof(PasswordError));
            }
        };
    }

    /// <inheritdoc />
    public override string Title => IsNew ? "Новий користувач" : "Зміна користувача";

    /// <summary>Ролі, доступні у формі.</summary>
    public IReadOnlyList<Option<UserRole>> Roles { get; }

    /// <summary>Чи можна змінити роль.</summary>
    public bool IsRoleEditable { get; }

    /// <summary>Пояснення щодо ролі.</summary>
    public string RoleHint { get; }

    /// <summary>Підпис поля пароля.</summary>
    public string PasswordLabel => IsNew
        ? "Пароль (щонайменше 8 символів, літери й цифри)"
        : "Новий пароль (залиште порожнім, щоб не змінювати)";

    /// <summary>Помилка пароля (PasswordBox не показує помилки сам).</summary>
    public string? PasswordError => FirstError(nameof(Password));

    /// <summary>ПІБ.</summary>
    [ObservableProperty]
    public partial string FullName { get; set; }

    /// <summary>Логін.</summary>
    [ObservableProperty]
    public partial string Login { get; set; }

    /// <summary>Пароль (передається з PasswordBox у коді вікна).</summary>
    [ObservableProperty]
    public partial string Password { get; set; } = string.Empty;

    /// <summary>Роль.</summary>
    [ObservableProperty]
    public partial UserRole Role { get; set; }

    /// <inheritdoc />
    protected override UserInput BuildInput(ValidationErrors parseErrors) => new(Id, FullName, Login, Password, Role);

    /// <inheritdoc />
    protected override Task SaveInputAsync(UserInput input) => _service.SaveAsync(input);
}
