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
/// Довідник кур'єрів: перегляд — адміністратор і оператор, зміна — оператор.
/// Кур'єр створюється разом з обліковим записом для входу (роль «Кур'єр»).
/// </summary>
public sealed class CourierDirectoryService : DirectoryServiceBase<Courier>, ICourierService
{
    private readonly IValidator<CourierInput> _validator;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IClock _clock;

    /// <summary>
    /// Створює сервіс.
    /// </summary>
    /// <param name="contextFactory">Фабрика контекстів БД.</param>
    /// <param name="currentUser">Поточний користувач.</param>
    /// <param name="validator">Правила кур'єра.</param>
    /// <param name="passwordHasher">Хешування пароля облікового запису.</param>
    /// <param name="clock">Поточний час.</param>
    public CourierDirectoryService(
        IDbContextFactory<AppDbContext> contextFactory,
        ICurrentUser currentUser,
        IValidator<CourierInput> validator,
        IPasswordHasher passwordHasher,
        IClock clock)
        : base(contextFactory, currentUser)
    {
        _validator = validator;
        _passwordHasher = passwordHasher;
        _clock = clock;
    }

    /// <inheritdoc />
    protected override UserRole[] EditRoles => [UserRole.Operator];

    /// <inheritdoc />
    protected override string NotFoundMessage => "Кур'єра не знайдено.";

    /// <inheritdoc />
    public async Task<IReadOnlyList<CourierListItem>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        CurrentUser.RequireRole(ReadRoles);
        await using var context = await ContextFactory.CreateDbContextAsync(cancellationToken);
        var couriers = await context.Couriers
            .AsNoTracking()
            .Select(c => new CourierListItem(
                c.Id, c.UserId, c.User.FullName, c.User.Login, c.Phone, c.TransportType,
                c.MaxWeightGrams, c.HomeZoneId, c.HomeZone.Name, c.PayoutPercent, c.IsActive))
            .ToListAsync(cancellationToken);
        return couriers.OrderBy(c => c.FullName, TextSearch.UkrainianComparer).ToList();
    }

    /// <inheritdoc />
    /// <remarks>Кур'єра з незавершеними доставками деактивувати не можна — вони «зависли» б.</remarks>
    public override async Task SetActiveAsync(int id, bool isActive, CancellationToken cancellationToken = default)
    {
        CurrentUser.RequireRole(EditRoles);

        await using var context = await ContextFactory.CreateDbContextAsync(cancellationToken);
        Courier courier = await FindOrThrowAsync(context, id, cancellationToken);
        if (!isActive)
        {
            await CourierWorkload.EnsureNoHeldOrdersAsync(context, courier.Id, cancellationToken);
        }

        courier.IsActive = isActive;
        await SaveChangesAsync(context, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<int> SaveAsync(CourierInput input, CancellationToken cancellationToken = default)
    {
        _validator.Validate(input).ThrowIfInvalid();
        CurrentUser.RequireRole(EditRoles);
        PhoneNumber.TryNormalize(input.Phone, out string phone);
        string login = input.Login.Trim();

        await using var context = await ContextFactory.CreateDbContextAsync(cancellationToken);

        Courier courier;
        if (input.Id is { } id)
        {
            courier = await context.Couriers.Include(c => c.User).SingleOrDefaultAsync(c => c.Id == id, cancellationToken)
                ?? throw new EntityNotFoundException(NotFoundMessage);
        }
        else
        {
            courier = new Courier { User = new User { Role = UserRole.Courier, CreatedAt = _clock.Now } };
            context.Couriers.Add(courier);
        }

        // Логін унікальний серед усіх користувачів (порівняння NOCASE у БД)
        bool loginTaken = await context.Users.AnyAsync(u => u.Login == login && u.Id != courier.UserId, cancellationToken);
        if (loginTaken)
        {
            throw new DomainValidationException($"Логін «{login}» уже зайнятий.", nameof(input.Login));
        }

        bool zoneIsActive = await context.Zones.AnyAsync(z => z.Id == input.HomeZoneId && z.IsActive, cancellationToken);
        if (!zoneIsActive)
        {
            throw new DomainValidationException("Оберіть активну домашню зону.", nameof(input.HomeZoneId));
        }

        courier.User.FullName = input.FullName.Trim();
        courier.User.Login = login;
        if (!string.IsNullOrEmpty(input.Password))
        {
            // PBKDF2 навмисно повільний — не блокуємо потік інтерфейсу
            var password = await Task.Run(() => _passwordHasher.Hash(input.Password), cancellationToken);
            courier.User.PasswordHash = password.Hash;
            courier.User.PasswordSalt = password.Salt;
        }

        courier.Phone = phone;
        courier.TransportType = input.TransportType;
        courier.MaxWeightGrams = input.MaxWeightGrams;
        courier.HomeZoneId = input.HomeZoneId;
        courier.PayoutPercent = input.PayoutPercent;

        // Профіль і обліковий запис зберігаються одним SaveChanges — тобто в одній транзакції
        await SaveChangesAsync(context, cancellationToken);
        return courier.Id;
    }
}
