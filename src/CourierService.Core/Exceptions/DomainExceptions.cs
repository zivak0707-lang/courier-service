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
    /// Створює виняток.
    /// </summary>
    /// <param name="message">Що саме введено неправильно.</param>
    public DomainValidationException(string message)
        : base(message)
    {
    }

    /// <inheritdoc />
    public override string UserMessage => "Перевірте введені дані. " + Message;
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
