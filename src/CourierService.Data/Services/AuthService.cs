using CourierService.Core.Abstractions;
using CourierService.Core.Enums;
using CourierService.Core.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CourierService.Data.Services;

/// <summary>
/// Вхід у програму: перевірка логіна й пароля за даними БД.
/// </summary>
public sealed class AuthService : IAuthService
{
    /// <summary>
    /// Однакове повідомлення для невідомого логіна й неправильного пароля:
    /// так сторонній не дізнається, які логіни існують.
    /// </summary>
    public const string InvalidCredentialsMessage = "Неправильний логін або пароль.";

    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<AuthService> _logger;

    /// <summary>
    /// Створює сервіс входу.
    /// </summary>
    /// <param name="contextFactory">Фабрика контекстів БД.</param>
    /// <param name="passwordHasher">Перевірка паролів.</param>
    /// <param name="currentUser">Поточний користувач, якому встановлюється сеанс.</param>
    /// <param name="logger">Журнал.</param>
    public AuthService(
        IDbContextFactory<AppDbContext> contextFactory,
        IPasswordHasher passwordHasher,
        ICurrentUser currentUser,
        ILogger<AuthService> logger)
    {
        _contextFactory = contextFactory;
        _passwordHasher = passwordHasher;
        _currentUser = currentUser;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<SignInResult> SignInAsync(string login, string password, CancellationToken cancellationToken = default)
    {
        string normalizedLogin = login?.Trim() ?? string.Empty;
        if (normalizedLogin.Length == 0 || string.IsNullOrEmpty(password))
        {
            return SignInResult.Failure("Введіть логін і пароль.");
        }

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        // Колонка Login має порівняння NOCASE, тому регістр не має значення
        var user = await context.Users
            .AsNoTracking()
            .Include(u => u.Courier)
            .SingleOrDefaultAsync(u => u.Login == normalizedLogin, cancellationToken);

        // PBKDF2 навмисно повільний, тому виконується у фоновому потоці, щоб не блокувати інтерфейс
        bool passwordValid = user is not null
            && await Task.Run(() => _passwordHasher.Verify(password, user.PasswordHash, user.PasswordSalt), cancellationToken);

        if (user is null || !passwordValid)
        {
            _logger.LogWarning("Невдала спроба входу з логіном {Login}", normalizedLogin);
            return SignInResult.Failure(InvalidCredentialsMessage);
        }

        if (!user.IsActive)
        {
            _logger.LogWarning("Спроба входу у вимкнений обліковий запис {Login}", user.Login);
            return SignInResult.Failure("Обліковий запис вимкнено. Зверніться до адміністратора.");
        }

        if (user.Role == UserRole.Courier && user.Courier is not { IsActive: true })
        {
            _logger.LogWarning("Кур'єр {Login} без активного профілю кур'єра", user.Login);
            return SignInResult.Failure("Профіль кур'єра вимкнено або не створено. Зверніться до адміністратора.");
        }

        _currentUser.SignIn(new UserSession(user.Id, user.Login, user.FullName, user.Role, user.Courier?.Id));
        _logger.LogInformation("Вхід користувача {Login} з роллю {Role}", user.Login, user.Role);
        return SignInResult.Success;
    }
}
