using CourierService.Core.Entities;
using CourierService.Core.Enums;
using CourierService.Core.Exceptions;
using CourierService.Core.Security;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace CourierService.Data.Services;

/// <summary>
/// Спільна частина сервісів довідників (узагальнення й шаблонний метод):
/// перевірка прав, пошук запису, деактивація, обробка порушення унікальності.
/// </summary>
/// <typeparam name="TEntity">Сутність довідника.</typeparam>
public abstract class DirectoryServiceBase<TEntity>
    where TEntity : ReferenceEntity
{
    /// <summary>Ролі, яким дозволено переглядати довідники.</summary>
    protected static readonly UserRole[] ReadRoles = [UserRole.Admin, UserRole.Operator];

    /// <summary>
    /// Створює сервіс.
    /// </summary>
    /// <param name="contextFactory">Фабрика контекстів БД.</param>
    /// <param name="currentUser">Поточний користувач для перевірки прав.</param>
    protected DirectoryServiceBase(IDbContextFactory<AppDbContext> contextFactory, ICurrentUser currentUser)
    {
        ContextFactory = contextFactory;
        CurrentUser = currentUser;
    }

    /// <summary>Фабрика контекстів БД.</summary>
    protected IDbContextFactory<AppDbContext> ContextFactory { get; }

    /// <summary>Поточний користувач.</summary>
    protected ICurrentUser CurrentUser { get; }

    /// <summary>Ролі, яким дозволено змінювати довідник.</summary>
    protected abstract UserRole[] EditRoles { get; }

    /// <summary>Повідомлення, якщо запис не знайдено (наприклад, «Зону не знайдено.»).</summary>
    protected abstract string NotFoundMessage { get; }

    /// <summary>
    /// Активує або деактивує запис.
    /// </summary>
    /// <param name="id">Ідентифікатор запису.</param>
    /// <param name="isActive">Нова ознака активності.</param>
    /// <param name="cancellationToken">Скасування операції.</param>
    /// <returns>Задача.</returns>
    public virtual async Task SetActiveAsync(int id, bool isActive, CancellationToken cancellationToken = default)
    {
        CurrentUser.RequireRole(EditRoles);

        await using var context = await ContextFactory.CreateDbContextAsync(cancellationToken);
        TEntity entity = await FindOrThrowAsync(context, id, cancellationToken);
        entity.IsActive = isActive;
        await SaveChangesAsync(context, cancellationToken);
    }

    /// <summary>
    /// Знаходить запис для зміни або кидає <see cref="EntityNotFoundException"/>.
    /// </summary>
    /// <param name="context">Контекст БД.</param>
    /// <param name="id">Ідентифікатор.</param>
    /// <param name="cancellationToken">Скасування операції.</param>
    /// <returns>Запис, який відстежує контекст.</returns>
    protected async Task<TEntity> FindOrThrowAsync(AppDbContext context, int id, CancellationToken cancellationToken) =>
        await context.Set<TEntity>().SingleOrDefaultAsync(entity => entity.Id == id, cancellationToken)
        ?? throw new EntityNotFoundException(NotFoundMessage);

    /// <summary>
    /// Зберігає зміни. Якщо унікальність порушено одночасним записом, повідомляє зрозуміло.
    /// </summary>
    /// <param name="context">Контекст БД.</param>
    /// <param name="cancellationToken">Скасування операції.</param>
    /// <returns>Задача.</returns>
    /// <exception cref="DomainValidationException">Такий запис уже існує.</exception>
    protected static async Task SaveChangesAsync(AppDbContext context, CancellationToken cancellationToken)
    {
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqliteException { SqliteExtendedErrorCode: 2067 })
        {
            // 2067 = SQLITE_CONSTRAINT_UNIQUE: запис з таким значенням з'явився між перевіркою й збереженням
            throw new DomainValidationException("Запис з такими даними вже існує.");
        }
    }

    /// <summary>
    /// Прибирає пробіли на краях; порожній текст перетворює на <c>null</c> (для необов'язкових полів).
    /// </summary>
    /// <param name="value">Введений текст.</param>
    /// <returns>Очищений текст або <c>null</c>.</returns>
    protected static string? TrimToNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
