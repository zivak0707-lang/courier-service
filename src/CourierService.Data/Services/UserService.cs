using CourierService.Core.Abstractions;
using CourierService.Core.Directories;
using CourierService.Core.Entities;
using CourierService.Core.Enums;
using CourierService.Core.Exceptions;
using CourierService.Core.Security;
using CourierService.Core.Validation;
using Microsoft.EntityFrameworkCore;

namespace CourierService.Data.Services;

/// <summary>
/// Керування обліковими записами (лише адміністратор).
/// </summary>
public sealed class UserService : DirectoryServiceBase<User>, IUserService
{
    private const string CourierRoleMessage = "Кур'єрів створюють у довіднику «Кур'єри».";
    private const string LastAdminMessage = "У системі має лишитися хоча б один активний адміністратор.";

    private readonly IValidator<UserInput> _validator;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IClock _clock;

    /// <summary>
    /// Створює сервіс.
    /// </summary>
    /// <param name="contextFactory">Фабрика контекстів БД.</param>
    /// <param name="currentUser">Поточний користувач.</param>
    /// <param name="validator">Правила облікового запису.</param>
    /// <param name="passwordHasher">Хешування паролів.</param>
    /// <param name="clock">Поточний час.</param>
    public UserService(
        IDbContextFactory<AppDbContext> contextFactory,
        ICurrentUser currentUser,
        IValidator<UserInput> validator,
        IPasswordHasher passwordHasher,
        IClock clock)
        : base(contextFactory, currentUser)
    {
        _validator = validator;
        _passwordHasher = passwordHasher;
        _clock = clock;
    }

    /// <inheritdoc />
    protected override UserRole[] EditRoles => [UserRole.Admin];

    /// <inheritdoc />
    protected override string NotFoundMessage => "Користувача не знайдено.";

    /// <inheritdoc />
    public async Task<IReadOnlyList<UserListItem>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        CurrentUser.RequireRole(EditRoles);
        await using var context = await ContextFactory.CreateDbContextAsync(cancellationToken);
        var users = await context.Users
            .AsNoTracking()
            .Select(u => new UserListItem(u.Id, u.Login, u.FullName, u.Role, u.IsActive, u.CreatedAt))
            .ToListAsync(cancellationToken);
        return users
            .OrderBy(u => u.Role)
            .ThenBy(u => u.FullName, SearchText.UkrainianComparer)
            .ToList();
    }

    /// <inheritdoc />
    public async Task<int> SaveAsync(UserInput input, CancellationToken cancellationToken = default)
    {
        _validator.Validate(input).ThrowIfInvalid();
        UserSession session = CurrentUser.RequireRole(EditRoles);
        string login = input.Login.Trim();

        await using var context = await ContextFactory.CreateDbContextAsync(cancellationToken);

        bool loginTaken = await context.Users.AnyAsync(u => u.Login == login && u.Id != input.Id, cancellationToken);
        if (loginTaken)
        {
            throw new DomainValidationException($"Логін «{login}» уже зайнятий.", nameof(input.Login));
        }

        User user;
        if (input.Id is { } id)
        {
            user = await FindOrThrowAsync(context, id, cancellationToken);
            await EnsureRoleChangeAllowedAsync(context, user, input.Role, session, cancellationToken);
        }
        else
        {
            if (input.Role == UserRole.Courier)
            {
                throw new DomainValidationException(CourierRoleMessage, nameof(input.Role));
            }

            user = context.Users.Add(new User { CreatedAt = _clock.Now }).Entity;
        }

        user.FullName = input.FullName.Trim();
        user.Login = login;
        user.Role = input.Role;
        if (!string.IsNullOrEmpty(input.Password))
        {
            var password = await Task.Run(() => _passwordHasher.Hash(input.Password), cancellationToken);
            user.PasswordHash = password.Hash;
            user.PasswordSalt = password.Salt;
        }

        await SaveChangesAsync(context, cancellationToken);
        return user.Id;
    }

    /// <inheritdoc />
    public override async Task SetActiveAsync(int id, bool isActive, CancellationToken cancellationToken = default)
    {
        UserSession session = CurrentUser.RequireRole(EditRoles);
        if (!isActive && id == session.UserId)
        {
            throw new DomainValidationException("Не можна вимкнути власний обліковий запис.");
        }

        await using var context = await ContextFactory.CreateDbContextAsync(cancellationToken);
        User user = await FindOrThrowAsync(context, id, cancellationToken);
        if (!isActive && user.Role == UserRole.Admin && !await HasOtherActiveAdminAsync(context, user.Id, cancellationToken))
        {
            throw new DomainValidationException(LastAdminMessage);
        }

        user.IsActive = isActive;
        await SaveChangesAsync(context, cancellationToken);
    }

    private static async Task EnsureRoleChangeAllowedAsync(
        AppDbContext context, User user, UserRole newRole, UserSession session, CancellationToken cancellationToken)
    {
        if (user.Role == newRole)
        {
            return;
        }

        if (user.Role == UserRole.Courier || newRole == UserRole.Courier)
        {
            throw new DomainValidationException(
                user.Role == UserRole.Courier ? "Роль кур'єра змінити не можна." : CourierRoleMessage,
                nameof(UserInput.Role));
        }

        if (user.Id == session.UserId)
        {
            throw new DomainValidationException("Не можна змінити роль власного облікового запису.", nameof(UserInput.Role));
        }

        if (user.Role == UserRole.Admin && user.IsActive && !await HasOtherActiveAdminAsync(context, user.Id, cancellationToken))
        {
            throw new DomainValidationException(LastAdminMessage, nameof(UserInput.Role));
        }
    }

    private static Task<bool> HasOtherActiveAdminAsync(AppDbContext context, int userId, CancellationToken cancellationToken) =>
        context.Users.AnyAsync(u => u.Role == UserRole.Admin && u.IsActive && u.Id != userId, cancellationToken);
}
