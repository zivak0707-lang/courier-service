using CourierService.Core.Entities;
using CourierService.Core.Enums;
using CourierService.Core.Validation;

namespace CourierService.Core.Directories;

/// <summary>
/// Дані форми зони доставки.
/// </summary>
/// <param name="Id">Ідентифікатор або <c>null</c> для нової зони.</param>
/// <param name="Name">Назва району.</param>
/// <param name="Bank">Берег Дніпра.</param>
/// <param name="CoefficientPercent">Коефіцієнт у відсотках (115 = ×1,15).</param>
public sealed record ZoneInput(int? Id, string Name, RiverBank Bank, int CoefficientPercent) : IDirectoryInput;

/// <summary>
/// Правила зони доставки (узгоджені з обмеженнями CHECK у БД).
/// </summary>
public sealed class ZoneValidator : IValidator<ZoneInput>
{
    /// <summary>Максимальна довжина назви.</summary>
    public const int NameMaxLength = 50;

    /// <inheritdoc />
    public ValidationErrors Validate(ZoneInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var errors = new ValidationErrors();
        errors.RequiredText(nameof(input.Name), input.Name, "Назва", NameMaxLength);
        errors.Defined(nameof(input.Bank), input.Bank, "Берег");
        errors.InRange(nameof(input.CoefficientPercent), input.CoefficientPercent, 100, 300,
            "Коефіцієнт має бути від 1,00 до 3,00.");
        return errors;
    }
}

/// <summary>
/// Довідник зон доставки. Змінювати може лише адміністратор.
/// </summary>
public interface IZoneService : IDirectoryService<Zone, ZoneInput>
{
}
