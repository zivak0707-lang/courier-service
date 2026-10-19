using CourierService.Core.Entities;
using CourierService.Core.Enums;
using CourierService.Core.Validation;

namespace CourierService.Core.Directories;

/// <summary>
/// Обліковий запис у списку користувачів.
/// </summary>
/// <param name="Id">Ідентифікатор.</param>
/// <param name="Login">Логін.</param>
/// <param name="FullName">ПІБ.</param>
/// <param name="Role">Роль.</param>
/// <param name="IsActive">Чи активний.</param>
/// <param name="CreatedAt">Дата створення.</param>
public sealed record UserListItem(
    int Id,
    string Login,
    string FullName,
    UserRole Role,
    bool IsActive,
    DateTime CreatedAt) : IReferenceItem;

/// <summary>
/// Дані форми користувача.
/// </summary>
/// <param name="Id">Ідентифікатор або <c>null</c> для нового користувача.</param>
/// <param name="FullName">ПІБ.</param>
/// <param name="Login">Логін.</param>
/// <param name="Password">Новий пароль (для наявного користувача порожній — не змінювати).</param>
/// <param name="Role">Роль.</param>
public sealed record UserInput(int? Id, string FullName, string Login, string? Password, UserRole Role) : IDirectoryInput;

/// <summary>
/// Правила облікового запису.
/// </summary>
public sealed class UserValidator : IValidator<UserInput>
{
    /// <inheritdoc />
    public ValidationErrors Validate(UserInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var errors = new ValidationErrors();
        errors.RequiredText(nameof(input.FullName), input.FullName, "ПІБ", User.FullNameMaxLength);
        errors.Login(nameof(input.Login), input.Login);
        errors.Password(nameof(input.Password), input.Password, required: input.Id is null);
        errors.Defined(nameof(input.Role), input.Role, "Роль");
        return errors;
    }
}

/// <summary>
/// Керування обліковими записами. Доступне лише адміністратору.
/// </summary>
/// <remarks>
/// Правила: кур'єрів створюють у довіднику «Кур'єри»; адміністратор не може вимкнути себе
/// чи змінити собі роль; у системі завжди лишається хоча б один активний адміністратор.
/// </remarks>
public interface IUserService : IDirectoryService<UserListItem, UserInput>
{
}
