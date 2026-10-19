namespace CourierService.Core.Directories;

/// <summary>
/// Запис довідника для показу в списку: має ідентифікатор і ознаку активності.
/// </summary>
public interface IReferenceItem
{
    /// <summary>Ідентифікатор запису.</summary>
    int Id { get; }

    /// <summary>Чи активний запис.</summary>
    bool IsActive { get; }
}

/// <summary>
/// Дані форми довідника. <see cref="Id"/> = <c>null</c> означає новий запис.
/// </summary>
public interface IDirectoryInput
{
    /// <summary>Ідентифікатор запису, що редагується, або <c>null</c> для нового.</summary>
    int? Id { get; }
}

/// <summary>
/// Спільні операції всіх довідників (узагальнений інтерфейс).
/// </summary>
/// <typeparam name="TItem">Запис для показу в списку.</typeparam>
/// <typeparam name="TInput">Дані форми редагування.</typeparam>
public interface IDirectoryService<TItem, in TInput>
    where TItem : IReferenceItem
    where TInput : IDirectoryInput
{
    /// <summary>
    /// Повертає всі записи довідника, зокрема неактивні.
    /// </summary>
    /// <param name="cancellationToken">Скасування операції.</param>
    /// <returns>Записи довідника.</returns>
    Task<IReadOnlyList<TItem>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Створює новий запис або змінює наявний після перевірки даних і прав.
    /// </summary>
    /// <param name="input">Дані форми.</param>
    /// <param name="cancellationToken">Скасування операції.</param>
    /// <returns>Ідентифікатор збереженого запису.</returns>
    Task<int> SaveAsync(TInput input, CancellationToken cancellationToken = default);

    /// <summary>
    /// Активує або деактивує запис (фізично записи не видаляються).
    /// </summary>
    /// <param name="id">Ідентифікатор запису.</param>
    /// <param name="isActive">Нова ознака активності.</param>
    /// <param name="cancellationToken">Скасування операції.</param>
    /// <returns>Задача.</returns>
    Task SetActiveAsync(int id, bool isActive, CancellationToken cancellationToken = default);
}
