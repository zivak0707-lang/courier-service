using CourierService.Core.Enums;

namespace CourierService.App.ViewModels.Directories;

/// <summary>
/// Варіант вибору у випадному списку: значення й підпис українською.
/// </summary>
/// <typeparam name="T">Тип значення.</typeparam>
/// <param name="Value">Значення.</param>
/// <param name="Display">Підпис.</param>
public sealed record Option<T>(T Value, string Display);

/// <summary>
/// Готові списки варіантів для полів форм.
/// </summary>
public static class Options
{
    /// <summary>Береги Дніпра.</summary>
    public static IReadOnlyList<Option<RiverBank>> Banks { get; } =
        Enum.GetValues<RiverBank>().Select(bank => new Option<RiverBank>(bank, bank.ToDisplayName())).ToList();

    /// <summary>Типи клієнтів.</summary>
    public static IReadOnlyList<Option<ClientType>> ClientTypes { get; } =
        Enum.GetValues<ClientType>().Select(type => new Option<ClientType>(type, type.ToDisplayName())).ToList();

    /// <summary>Види транспорту.</summary>
    public static IReadOnlyList<Option<TransportType>> TransportTypes { get; } =
        Enum.GetValues<TransportType>().Select(type => new Option<TransportType>(type, type.ToDisplayName())).ToList();

    /// <summary>Хто платить за доставку.</summary>
    public static IReadOnlyList<Option<PaymentPayer>> Payers { get; } =
        Enum.GetValues<PaymentPayer>().Select(payer => new Option<PaymentPayer>(payer, payer.ToDisplayName())).ToList();

    /// <summary>Ролі, які можна призначити в довіднику користувачів (кур'єрів створюють окремо).</summary>
    public static IReadOnlyList<Option<UserRole>> AssignableRoles { get; } =
        [new(UserRole.Admin, UserRole.Admin.ToDisplayName()), new(UserRole.Operator, UserRole.Operator.ToDisplayName())];
}
