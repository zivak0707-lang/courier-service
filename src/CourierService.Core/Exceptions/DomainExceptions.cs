using CourierService.Core.Validation;

namespace CourierService.Core.Exceptions;

/// <summary>
/// Базовий виняток предметної області: порушення бізнес-правила, про яке
/// треба зрозуміло повідомити користувача.
/// </summary>
/// <remarks>
/// Поліморфізм: глобальний обробник помилок показує <see cref="UserMessage"/>,
/// а кожен нащадок може визначити власний текст.
/// </remarks>
public abstract class DomainException : Exception
{
    /// <summary>
    /// Створює виняток.
    /// </summary>
    /// <param name="message">Опис порушеного правила українською.</param>
    protected DomainException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Текст для показу користувачу. За замовчуванням — текст винятку.
    /// </summary>
    public virtual string UserMessage => Message;
}

/// <summary>
/// Введені або передані дані не відповідають правилам предметної області.
/// </summary>
public sealed class DomainValidationException : DomainException
{
    /// <summary>
    /// Створює виняток з однією помилкою.
    /// </summary>
    /// <param name="message">Що саме введено неправильно.</param>
    /// <param name="propertyName">Поле, до якого належить помилка (якщо відоме).</param>
    public DomainValidationException(string message, string? propertyName = null)
        : base(message)
    {
        Errors = new ValidationErrors();
        if (propertyName is not null)
        {
            Errors.Add(propertyName, message);
        }
    }

    /// <summary>
    /// Створює виняток з результату перевірки.
    /// </summary>
    /// <param name="errors">Помилки за полями.</param>
    public DomainValidationException(ValidationErrors errors)
        : base(errors?.FirstMessage ?? "Дані некоректні.")
    {
        ArgumentNullException.ThrowIfNull(errors);
        Errors = errors;
    }

    /// <summary>Помилки за полями — форма показує їх біля відповідних полів.</summary>
    public ValidationErrors Errors { get; }

    /// <inheritdoc />
    /// <remarks>
    /// Префікс «Перевірте введені дані» додається, лише коли помилка стосується конкретних полів;
    /// порушене бізнес-правило («не можна вимкнути власний обліковий запис») показується як є.
    /// </remarks>
    public override string UserMessage => Errors.IsValid ? Message : "Перевірте введені дані. " + Message;
}

/// <summary>
/// Користувач намагається виконати дію, на яку його роль не має права.
/// </summary>
public sealed class AccessDeniedException : DomainException
{
    /// <summary>
    /// Створює виняток.
    /// </summary>
    /// <param name="message">Яку дію заборонено.</param>
    public AccessDeniedException(string message)
        : base(message)
    {
    }

    /// <inheritdoc />
    public override string UserMessage => "Недостатньо прав. " + Message;
}

/// <summary>
/// Запис, з яким працює користувач, не знайдено (наприклад, його вже змінив інший сеанс).
/// </summary>
public sealed class EntityNotFoundException : DomainException
{
    /// <summary>
    /// Створює виняток.
    /// </summary>
    /// <param name="message">Що саме не знайдено.</param>
    public EntityNotFoundException(string message)
        : base(message)
    {
    }

    /// <inheritdoc />
    public override string UserMessage => Message + " Оновіть список і спробуйте ще раз.";
}
