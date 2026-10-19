using CourierService.Core.Directories;
using CourierService.Core.Enums;
using CourierService.Core.Validation;

namespace CourierService.Core.Assignment;

/// <summary>
/// Кур'єр як кандидат на призначення: усе, що потрібно алгоритму, без доступу до БД.
/// </summary>
/// <param name="CourierId">Ідентифікатор кур'єра.</param>
/// <param name="FullName">Ім'я.</param>
/// <param name="HomeZoneId">Домашня зона.</param>
/// <param name="HomeZoneName">Назва домашньої зони.</param>
/// <param name="HomeBank">Берег домашньої зони.</param>
/// <param name="MaxWeightGrams">Вантажопідйомність, г.</param>
/// <param name="ActiveDeliveries">Кількість призначених і забраних, але ще не доставлених замовлень.</param>
/// <param name="IsActive">Чи активний кур'єр.</param>
public sealed record CourierCandidate(
    int CourierId,
    string FullName,
    int HomeZoneId,
    string HomeZoneName,
    RiverBank HomeBank,
    int MaxWeightGrams,
    int ActiveDeliveries,
    bool IsActive);

/// <summary>
/// Що потрібно знати про замовлення для вибору кур'єра.
/// </summary>
/// <param name="PickupZoneId">Зона, звідки забрати.</param>
/// <param name="PickupBank">Берег зони, звідки забрати.</param>
/// <param name="ChargeableWeightGrams">Розрахункова вага посилки, г.</param>
public sealed record AssignmentRequest(int PickupZoneId, RiverBank PickupBank, int ChargeableWeightGrams);

/// <summary>
/// Пропозиція автопризначення: кандидат (або <c>null</c>) і пояснення для оператора.
/// </summary>
/// <param name="Courier">Запропонований кур'єр або <c>null</c>, якщо підходящого немає.</param>
/// <param name="Explanation">Чому обрано саме його або чому нікого не обрано.</param>
public sealed record AssignmentSuggestion(CourierCandidate? Courier, string Explanation);

/// <summary>
/// Вибір кур'єра для замовлення (рішення D-27).
/// </summary>
public interface ICourierAssignmentService
{
    /// <summary>
    /// Пропонує кур'єра з поясненням вибору.
    /// </summary>
    /// <param name="request">Дані замовлення.</param>
    /// <param name="candidates">Усі кур'єри з кількістю активних доставок.</param>
    /// <returns>Пропозиція.</returns>
    AssignmentSuggestion Suggest(AssignmentRequest request, IEnumerable<CourierCandidate> candidates);
}

/// <summary>
/// Алгоритм автопризначення: з активних кур'єрів, що витримують вагу й мають менше
/// <see cref="MaxActiveDeliveries"/> активних доставок, обирається той, чия домашня зона
/// збігається із зоною, звідки забрати; далі — той самий берег Дніпра; далі — менше активних доставок; далі — за ім'ям.
/// </summary>
/// <remarks>Чиста функція: однакові дані завжди дають однаковий вибір, тому алгоритм перевіряється тестами.</remarks>
public sealed class CourierAssignment : ICourierAssignmentService
{
    /// <summary>Скільки активних доставок кур'єр може мати одночасно, щоб отримати нову автоматично.</summary>
    public const int MaxActiveDeliveries = 8;

    /// <inheritdoc />
    public AssignmentSuggestion Suggest(AssignmentRequest request, IEnumerable<CourierCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(candidates);

        List<CourierCandidate> active = candidates.Where(c => c.IsActive).ToList();
        if (active.Count == 0)
        {
            return new AssignmentSuggestion(null, "Немає активних кур'єрів.");
        }

        List<CourierCandidate> strongEnough = active.Where(c => c.MaxWeightGrams >= request.ChargeableWeightGrams).ToList();
        if (strongEnough.Count == 0)
        {
            return new AssignmentSuggestion(null,
                $"Жоден активний кур'єр не перевозить {InputParsing.FormatKilograms(request.ChargeableWeightGrams)} кг.");
        }

        List<CourierCandidate> available = strongEnough.Where(c => c.ActiveDeliveries < MaxActiveDeliveries).ToList();
        if (available.Count == 0)
        {
            return new AssignmentSuggestion(null,
                $"Усі кур'єри, що можуть узяти посилку, мають по {MaxActiveDeliveries} і більше активних доставок. Призначте вручну.");
        }

        CourierCandidate best = available
            .OrderByDescending(c => c.HomeZoneId == request.PickupZoneId)
            .ThenByDescending(c => c.HomeBank == request.PickupBank)
            .ThenBy(c => c.ActiveDeliveries)
            .ThenBy(c => c.FullName, TextSearch.UkrainianComparer)
            .First();

        string location = best.HomeZoneId == request.PickupZoneId
            ? $"та сама зона ({best.HomeZoneName})"
            : best.HomeBank == request.PickupBank
                ? $"той самий берег ({best.HomeBank.ToDisplayName().ToLowerInvariant()})"
                : "на березі, звідки забрати, вільних кур'єрів немає";
        return new AssignmentSuggestion(best, $"{best.FullName}: {location}, {ActiveDeliveriesText(best.ActiveDeliveries)}.");
    }

    /// <summary>
    /// «немає активних доставок», «1 активна доставка», «3 активні доставки», «5 активних доставок».
    /// </summary>
    /// <param name="count">Кількість.</param>
    /// <returns>Текст українською з правильною формою слова.</returns>
    public static string ActiveDeliveriesText(int count)
    {
        if (count == 0)
        {
            return "немає активних доставок";
        }

        int lastTwo = count % 100;
        int last = count % 10;
        string words = last == 1 && lastTwo != 11
            ? "активна доставка"
            : last is >= 2 and <= 4 && lastTwo is < 12 or > 14
                ? "активні доставки"
                : "активних доставок";
        return $"{count} {words}";
    }
}
