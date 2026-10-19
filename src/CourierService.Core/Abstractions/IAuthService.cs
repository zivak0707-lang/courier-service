namespace CourierService.Core.Abstractions;

/// <summary>
/// Результат спроби входу.
/// </summary>
/// <param name="Succeeded">Чи вдалося увійти.</param>
/// <param name="ErrorMessage">Пояснення для користувача, якщо вхід не вдався.</param>
public sealed record SignInResult(bool Succeeded, string? ErrorMessage)
{
    /// <summary>Успішний вхід.</summary>
    public static SignInResult Success { get; } = new(true, null);

    /// <summary>Невдалий вхід із поясненням.</summary>
    /// <param name="message">Пояснення для користувача.</param>
    /// <returns>Результат.</returns>
    public static SignInResult Failure(string message) => new(false, message);
}

/// <summary>
/// Вхід у програму за логіном і паролем.
/// </summary>
public interface IAuthService
{
    /// <summary>
    /// Перевіряє логін і пароль та в разі успіху починає сеанс користувача.
    /// </summary>
    /// <param name="login">Логін (регістр не має значення).</param>
    /// <param name="password">Пароль.</param>
    /// <param name="cancellationToken">Скасування операції.</param>
    /// <returns>Результат входу.</returns>
    Task<SignInResult> SignInAsync(string login, string password, CancellationToken cancellationToken = default);
}
