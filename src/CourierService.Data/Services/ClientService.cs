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
/// Довідник клієнтів: перегляд — адміністратор і оператор, зміна — оператор.
/// </summary>
public sealed class ClientService : DirectoryServiceBase<Client>, IClientService
{
    private readonly IValidator<ClientInput> _validator;
    private readonly IClock _clock;

    /// <summary>
    /// Створює сервіс.
    /// </summary>
    /// <param name="contextFactory">Фабрика контекстів БД.</param>
    /// <param name="currentUser">Поточний користувач.</param>
    /// <param name="validator">Правила клієнта.</param>
    /// <param name="clock">Поточний час для дати реєстрації.</param>
    public ClientService(
        IDbContextFactory<AppDbContext> contextFactory,
        ICurrentUser currentUser,
        IValidator<ClientInput> validator,
        IClock clock)
        : base(contextFactory, currentUser)
    {
        _validator = validator;
        _clock = clock;
    }

    /// <inheritdoc />
    protected override UserRole[] EditRoles => [UserRole.Operator];

    /// <inheritdoc />
    protected override string NotFoundMessage => "Клієнта не знайдено.";

    /// <inheritdoc />
    public async Task<IReadOnlyList<Client>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        CurrentUser.RequireRole(ReadRoles);
        await using var context = await ContextFactory.CreateDbContextAsync(cancellationToken);
        var clients = await context.Clients.AsNoTracking().ToListAsync(cancellationToken);
        return clients.OrderBy(c => c.Name, TextSearch.UkrainianComparer).ToList();
    }

    /// <inheritdoc />
    public async Task<int> SaveAsync(ClientInput input, CancellationToken cancellationToken = default)
    {
        _validator.Validate(input).ThrowIfInvalid();
        CurrentUser.RequireRole(EditRoles);
        PhoneNumber.TryNormalize(input.Phone, out string phone); // валідатор уже перевірив номер

        await using var context = await ContextFactory.CreateDbContextAsync(cancellationToken);
        string? owner = await context.Clients
            .Where(c => c.Phone == phone && c.Id != input.Id)
            .Select(c => c.Name)
            .FirstOrDefaultAsync(cancellationToken);
        if (owner is not null)
        {
            throw new DomainValidationException($"Цей телефон уже має клієнт «{owner}».", nameof(input.Phone));
        }

        Client client;
        if (input.Id is { } id)
        {
            client = await FindOrThrowAsync(context, id, cancellationToken);
        }
        else
        {
            client = context.Clients.Add(new Client { CreatedAt = _clock.Now }).Entity;
        }

        client.ClientType = input.ClientType;
        client.Name = input.Name.Trim();
        client.Phone = phone;
        client.Email = TrimToNull(input.Email);
        client.Address = input.Address.Trim();
        client.Notes = TrimToNull(input.Notes);

        await SaveChangesAsync(context, cancellationToken);
        return client.Id;
    }
}
