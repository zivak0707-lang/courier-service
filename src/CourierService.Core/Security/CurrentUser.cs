using CourierService.Core.Enums;
using CourierService.Core.Exceptions;

namespace CourierService.Core.Security;

/// <summary>
/// Дані сеансу користувача, що увійшов у програму.
/// </summary>
/// <param name="UserId">Ідентифікатор користувача.</param>
/// <param name="Login">Логін.</param>
/// <param name="FullName">Прізвище та ім'я.</param>
/// <param name="Role">Роль.</param>
/// <param name="CourierId">Ідентифікатор профілю кур'єра (лише для ролі «Кур'єр»).</param>
public sealed record UserSession(int UserId, string Login, string FullName, UserRole Role, int? CourierId);

/// <summary>
/// Поточний користувач програми. Сервіси перевіряють права через нього (рішення D-08).
/// </summary>
public interface ICurrentUser
{
    /// <summary>Сеанс поточного користувача або <c>null</c>, якщо вхід не виконано.</summary>
    UserSession? Session { get; }

    /// <summary>Чи виконано вхід.</summary>
    bool IsAuthenticated { get; }

    /// <summary>Виникає після входу або виходу.</summary>
    event EventHandler? Changed;

    /// <summary>Починає сеанс.</summary>
    /// <param name="session">Дані користувача.</param>
    void SignIn(UserSession session);

    /// <summary>Завершує сеанс.</summary>
    void SignOut();

    /// <summary>Виникає, коли змінилися дані поточного сеансу без виходу (наприклад, ПІБ).</summary>
    event EventHandler? SessionUpdated;

    /// <summary>
    /// Оновлює дані поточного сеансу (того самого користувача) без виходу з програми.
    /// </summary>
    /// <param name="session">Нові дані сеансу.</param>
    /// <exception cref="InvalidOperationException">Вхід не виконано або це інший користувач.</exception>
    void UpdateSession(UserSession session);

    /// <summary>
    /// Повертає сеанс, якщо роль користувача входить до дозволених.
    /// </summary>
    /// <param name="allowedRoles">Ролі, яким дозволено дію.</param>
    /// <returns>Сеанс поточного користувача.</returns>
    /// <exception cref="AccessDeniedException">Вхід не виконано або роль не дозволена.</exception>
    UserSession RequireRole(params UserRole[] allowedRoles);
}

/// <summary>
/// Реалізація <see cref="ICurrentUser"/>: зберігає сеанс у пам'яті на час роботи програми.
/// </summary>
public sealed class CurrentUser : ICurrentUser
{
    /// <inheritdoc />
    public UserSession? Session { get; private set; }

    /// <inheritdoc />
    public bool IsAuthenticated => Session is not null;

    /// <inheritdoc />
    public event EventHandler? Changed;

    /// <inheritdoc />
    public void SignIn(UserSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        Session = session;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc />
    public void SignOut()
    {
        Session = null;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc />
    public event EventHandler? SessionUpdated;

    /// <inheritdoc />
    public void UpdateSession(UserSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (Session is null || Session.UserId != session.UserId)
        {
            throw new InvalidOperationException("Оновити можна лише сеанс поточного користувача.");
        }

        Session = session;
        SessionUpdated?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc />
    public UserSession RequireRole(params UserRole[] allowedRoles)
    {
        UserSession session = Session
            ?? throw new AccessDeniedException("Спершу увійдіть у програму.");

        if (allowedRoles.Length > 0 && !allowedRoles.Contains(session.Role))
        {
            throw new AccessDeniedException(
                $"Дія недоступна для ролі «{session.Role.ToDisplayName()}».");
        }

        return session;
    }
}
