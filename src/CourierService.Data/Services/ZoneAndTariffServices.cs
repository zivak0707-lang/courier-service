using CourierService.Core.Directories;
using CourierService.Core.Entities;
using CourierService.Core.Enums;
using CourierService.Core.Exceptions;
using CourierService.Core.Security;
using CourierService.Core.Validation;
using Microsoft.EntityFrameworkCore;

namespace CourierService.Data.Services;

/// <summary>
/// Довідник зон доставки: перегляд — адміністратор і оператор, зміна — адміністратор.
/// </summary>
public sealed class ZoneService : DirectoryServiceBase<Zone>, IZoneService
{
    private readonly IValidator<ZoneInput> _validator;

    /// <summary>
    /// Створює сервіс.
    /// </summary>
    /// <param name="contextFactory">Фабрика контекстів БД.</param>
    /// <param name="currentUser">Поточний користувач.</param>
    /// <param name="validator">Правила зони.</param>
    public ZoneService(IDbContextFactory<AppDbContext> contextFactory, ICurrentUser currentUser, IValidator<ZoneInput> validator)
        : base(contextFactory, currentUser)
    {
        _validator = validator;
    }

    /// <inheritdoc />
    protected override UserRole[] EditRoles => [UserRole.Admin];

    /// <inheritdoc />
    protected override string NotFoundMessage => "Зону не знайдено.";

    /// <inheritdoc />
    public async Task<IReadOnlyList<Zone>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        CurrentUser.RequireRole(ReadRoles);
        await using var context = await ContextFactory.CreateDbContextAsync(cancellationToken);
        var zones = await context.Zones.AsNoTracking().ToListAsync(cancellationToken);
        return zones.OrderBy(z => z.Name, TextSearch.UkrainianComparer).ToList();
    }

    /// <inheritdoc />
    public async Task<int> SaveAsync(ZoneInput input, CancellationToken cancellationToken = default)
    {
        _validator.Validate(input).ThrowIfInvalid();
        CurrentUser.RequireRole(EditRoles);

        await using var context = await ContextFactory.CreateDbContextAsync(cancellationToken);
        string name = input.Name.Trim();
        var otherNames = await context.Zones.Where(z => z.Id != input.Id).Select(z => z.Name).ToListAsync(cancellationToken);
        if (otherNames.Any(other => TextSearch.SameText(other, name)))
        {
            throw new DomainValidationException($"Зона «{name}» вже існує.", nameof(input.Name));
        }

        Zone zone = input.Id is { } id ? await FindOrThrowAsync(context, id, cancellationToken) : context.Zones.Add(new Zone()).Entity;
        zone.Name = name;
        zone.Bank = input.Bank;
        zone.CoefficientPercent = input.CoefficientPercent;

        await SaveChangesAsync(context, cancellationToken);
        return zone.Id;
    }
}

/// <summary>
/// Довідник тарифів: перегляд — адміністратор і оператор, зміна — адміністратор.
/// Зміна тарифу не впливає на вже оформлені замовлення (ціна в них — знімок).
/// </summary>
public sealed class TariffService : DirectoryServiceBase<Tariff>, ITariffService
{
    private readonly IValidator<TariffInput> _validator;

    /// <summary>
    /// Створює сервіс.
    /// </summary>
    /// <param name="contextFactory">Фабрика контекстів БД.</param>
    /// <param name="currentUser">Поточний користувач.</param>
    /// <param name="validator">Правила тарифу.</param>
    public TariffService(IDbContextFactory<AppDbContext> contextFactory, ICurrentUser currentUser, IValidator<TariffInput> validator)
        : base(contextFactory, currentUser)
    {
        _validator = validator;
    }

    /// <inheritdoc />
    protected override UserRole[] EditRoles => [UserRole.Admin];

    /// <inheritdoc />
    protected override string NotFoundMessage => "Тариф не знайдено.";

    /// <inheritdoc />
    public async Task<IReadOnlyList<Tariff>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        CurrentUser.RequireRole(ReadRoles);
        await using var context = await ContextFactory.CreateDbContextAsync(cancellationToken);
        // Від дешевшого до дорожчого — так тарифи читаються природно
        return await context.Tariffs.AsNoTracking().OrderBy(t => t.BasePrice).ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<int> SaveAsync(TariffInput input, CancellationToken cancellationToken = default)
    {
        _validator.Validate(input).ThrowIfInvalid();
        CurrentUser.RequireRole(EditRoles);

        await using var context = await ContextFactory.CreateDbContextAsync(cancellationToken);
        string name = input.Name.Trim();
        var otherNames = await context.Tariffs.Where(t => t.Id != input.Id).Select(t => t.Name).ToListAsync(cancellationToken);
        if (otherNames.Any(other => TextSearch.SameText(other, name)))
        {
            throw new DomainValidationException($"Тариф «{name}» вже існує.", nameof(input.Name));
        }

        Tariff tariff = input.Id is { } id ? await FindOrThrowAsync(context, id, cancellationToken) : context.Tariffs.Add(new Tariff()).Entity;
        tariff.Name = name;
        tariff.BasePrice = input.BasePrice;
        tariff.IncludedWeightGrams = input.IncludedWeightGrams;
        tariff.PricePerExtraKg = input.PricePerExtraKg;
        tariff.MaxWeightGrams = input.MaxWeightGrams;
        tariff.DeliveryHours = input.DeliveryHours;
        tariff.CrossRiverSurcharge = input.CrossRiverSurcharge;
        tariff.InsuranceFreeLimit = input.InsuranceFreeLimit;
        tariff.InsurancePermille = input.InsurancePermille;
        tariff.FragileSurcharge = input.FragileSurcharge;

        await SaveChangesAsync(context, cancellationToken);
        return tariff.Id;
    }
}
