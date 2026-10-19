using CourierService.Core.Entities;
using CourierService.Core.Enums;
using CourierService.Core.Validation;

namespace CourierService.Core.Directories;

/// <summary>
/// Кур'єр у списку довідника: профіль разом з ім'ям і логіном облікового запису.
/// </summary>
/// <param name="Id">Ідентифікатор профілю кур'єра.</param>
/// <param name="UserId">Ідентифікатор облікового запису.</param>
/// <param name="FullName">ПІБ.</param>
/// <param name="Login">Логін.</param>
/// <param name="Phone">Телефон +380XXXXXXXXX.</param>
/// <param name="TransportType">Вид транспорту.</param>
/// <param name="MaxWeightGrams">Вантажопідйомність, г.</param>
/// <param name="HomeZoneId">Домашня зона.</param>
/// <param name="HomeZoneName">Назва домашньої зони.</param>
/// <param name="PayoutPercent">Відсоток виплати.</param>
/// <param name="IsActive">Чи активний кур'єр.</param>
public sealed record CourierListItem(
    int Id,
    int UserId,
    string FullName,
    string Login,
    string Phone,
    TransportType TransportType,
    int MaxWeightGrams,
    int HomeZoneId,
    string HomeZoneName,
    int PayoutPercent,
    bool IsActive) : IReferenceItem;

/// <summary>
/// Дані форми кур'єра разом з обліковим записом для входу.
/// </summary>
/// <param name="Id">Ідентифікатор профілю або <c>null</c> для нового кур'єра.</param>
/// <param name="FullName">ПІБ.</param>
/// <param name="Login">Логін.</param>
/// <param name="Password">Пароль (для наявного кур'єра порожній — не змінювати).</param>
/// <param name="Phone">Телефон.</param>
/// <param name="TransportType">Вид транспорту.</param>
/// <param name="MaxWeightGrams">Вантажопідйомність, г.</param>
/// <param name="HomeZoneId">Домашня зона.</param>
/// <param name="PayoutPercent">Відсоток виплати.</param>
public sealed record CourierInput(
    int? Id,
    string FullName,
    string Login,
    string? Password,
    string Phone,
    TransportType TransportType,
    int MaxWeightGrams,
    int HomeZoneId,
    int PayoutPercent) : IDirectoryInput;

/// <summary>
/// Правила кур'єра.
/// </summary>
public sealed class CourierValidator : IValidator<CourierInput>
{
    /// <inheritdoc />
    public ValidationErrors Validate(CourierInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var errors = new ValidationErrors();
        errors.RequiredText(nameof(input.FullName), input.FullName, "ПІБ", User.FullNameMaxLength);
        errors.Login(nameof(input.Login), input.Login);
        errors.Password(nameof(input.Password), input.Password, required: input.Id is null);
        errors.Phone(nameof(input.Phone), input.Phone);
        errors.Defined(nameof(input.TransportType), input.TransportType, "Транспорт");
        errors.InRange(nameof(input.MaxWeightGrams), input.MaxWeightGrams, 1, TariffValidator.MaxParcelWeightGrams,
            "Вантажопідйомність має бути від 0,001 до 100 кг.");
        if (input.HomeZoneId <= 0)
        {
            errors.Add(nameof(input.HomeZoneId), "Оберіть домашню зону.");
        }

        errors.InRange(nameof(input.PayoutPercent), input.PayoutPercent, 0, 100,
            "Відсоток виплати має бути від 0 до 100.");
        return errors;
    }
}

/// <summary>
/// Довідник кур'єрів. Змінювати може оператор; кожен кур'єр має обліковий запис для входу.
/// </summary>
public interface ICourierService : IDirectoryService<CourierListItem, CourierInput>
{
}
