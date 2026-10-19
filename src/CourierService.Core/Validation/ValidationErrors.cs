using CourierService.Core.Exceptions;

namespace CourierService.Core.Validation;

/// <summary>
/// Результат перевірки введених даних: помилки, згруповані за назвою поля.
/// </summary>
/// <remarks>
/// Назви полів збігаються з назвами властивостей вхідних даних і полів форми,
/// тому форма показує кожну помилку біля відповідного поля.
/// </remarks>
public sealed class ValidationErrors
{
    private readonly Dictionary<string, List<string>> _errors = new(StringComparer.Ordinal);

    /// <summary>Чи немає помилок.</summary>
    public bool IsValid => _errors.Count == 0;

    /// <summary>Поля, для яких є помилки.</summary>
    public IReadOnlyCollection<string> PropertyNames => _errors.Keys;

    /// <summary>Перша помилка (для короткого повідомлення).</summary>
    public string? FirstMessage => _errors.Values.SelectMany(messages => messages).FirstOrDefault();

    /// <summary>
    /// Помилки поля.
    /// </summary>
    /// <param name="propertyName">Назва поля.</param>
    /// <returns>Список помилок (порожній, якщо помилок немає).</returns>
    public IReadOnlyList<string> this[string propertyName] =>
        _errors.TryGetValue(propertyName, out var messages) ? messages : [];

    /// <summary>
    /// Додає помилку поля.
    /// </summary>
    /// <param name="propertyName">Назва поля.</param>
    /// <param name="message">Опис помилки українською.</param>
    public void Add(string propertyName, string message)
    {
        if (!_errors.TryGetValue(propertyName, out var messages))
        {
            messages = [];
            _errors[propertyName] = messages;
        }

        messages.Add(message);
    }

    /// <summary>
    /// Додає всі помилки іншого результату.
    /// </summary>
    /// <param name="other">Інший результат перевірки.</param>
    public void Merge(ValidationErrors other)
    {
        ArgumentNullException.ThrowIfNull(other);
        foreach (var (propertyName, messages) in other._errors)
        {
            foreach (string message in messages)
            {
                Add(propertyName, message);
            }
        }
    }

    /// <summary>
    /// Кидає <see cref="DomainValidationException"/>, якщо є помилки.
    /// </summary>
    /// <exception cref="DomainValidationException">Дані некоректні.</exception>
    public void ThrowIfInvalid()
    {
        if (!IsValid)
        {
            throw new DomainValidationException(this);
        }
    }
}

/// <summary>
/// Перевірка вхідних даних певного типу.
/// </summary>
/// <typeparam name="T">Тип вхідних даних.</typeparam>
public interface IValidator<in T>
{
    /// <summary>
    /// Перевіряє дані за правилами предметної області.
    /// </summary>
    /// <param name="input">Дані для перевірки.</param>
    /// <returns>Знайдені помилки.</returns>
    ValidationErrors Validate(T input);
}
