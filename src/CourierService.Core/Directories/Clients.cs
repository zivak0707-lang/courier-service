using CourierService.Core.Entities;
using CourierService.Core.Enums;
using CourierService.Core.Validation;

namespace CourierService.Core.Directories;

/// <summary>
/// Дані форми клієнта.
/// </summary>
/// <param name="Id">Ідентифікатор або <c>null</c> для нового клієнта.</param>
/// <param name="ClientType">Фізособа чи компанія.</param>
/// <param name="Name">ПІБ або назва.</param>
/// <param name="Phone">Телефон у будь-якому допустимому форматі.</param>
/// <param name="Email">Електронна пошта (необов'язково).</param>
/// <param name="Address">Типова адреса, звідки забирати посилки клієнта.</param>
/// <param name="Notes">Примітки.</param>
public sealed record ClientInput(
    int? Id,
    ClientType ClientType,
    string Name,
    string Phone,
    string? Email,
    string Address,
    string? Notes) : IDirectoryInput;

/// <summary>
/// Правила клієнта (довжини узгоджені з колонками БД).
/// </summary>
public sealed class ClientValidator : IValidator<ClientInput>
{
    /// <inheritdoc />
    public ValidationErrors Validate(ClientInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var errors = new ValidationErrors();
        errors.Defined(nameof(input.ClientType), input.ClientType, "Тип клієнта");
        errors.RequiredText(nameof(input.Name), input.Name, "Назва / ПІБ", 150);
        errors.Phone(nameof(input.Phone), input.Phone);
        errors.OptionalEmail(nameof(input.Email), input.Email, 100);
        errors.RequiredText(nameof(input.Address), input.Address, "Типова адреса", 200);
        errors.OptionalText(nameof(input.Notes), input.Notes, "Примітки", 500);
        return errors;
    }
}

/// <summary>
/// Довідник клієнтів. Змінювати може оператор.
/// </summary>
public interface IClientService : IDirectoryService<Client, ClientInput>
{
}
