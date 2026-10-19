using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CourierService.App.Services;
using CourierService.Core.Directories;
using CourierService.Core.Enums;
using CourierService.Core.Exceptions;
using CourierService.Core.Security;

namespace CourierService.App.ViewModels.Directories;

/// <summary>
/// Сторінка довідника зі списком: пошук, фільтр «показувати неактивні», додавання,
/// редагування в модальному діалозі та деактивація з підтвердженням (рішення D-19).
/// </summary>
/// <typeparam name="TItem">Запис списку.</typeparam>
/// <remarks>
/// Шаблонний метод: загальний сценарій описано тут, а нащадки визначають лише,
/// звідки брати записи, як їх шукати й яку форму відкривати.
/// </remarks>
public abstract partial class ReferenceListViewModel<TItem> : ViewModelBase, ILoadable
    where TItem : class, IReferenceItem
{
    private IReadOnlyList<TItem> _allItems = [];

    /// <summary>
    /// Створює сторінку довідника.
    /// </summary>
    /// <param name="dialogs">Повідомлення й підтвердження.</param>
    /// <param name="editDialogs">Вікно редагування.</param>
    /// <param name="currentUser">Поточний користувач (визначає право змінювати довідник).</param>
    protected ReferenceListViewModel(IDialogService dialogs, IEditDialogService editDialogs, ICurrentUser currentUser)
    {
        Dialogs = dialogs;
        EditDialogs = editDialogs;
        CurrentUser = currentUser;
    }

    /// <summary>Повідомлення й підтвердження.</summary>
    protected IDialogService Dialogs { get; }

    /// <summary>Вікно редагування.</summary>
    protected IEditDialogService EditDialogs { get; }

    /// <summary>Поточний користувач.</summary>
    protected ICurrentUser CurrentUser { get; }

    /// <summary>Записи, що відповідають пошуку й фільтру.</summary>
    public ObservableCollection<TItem> Items { get; } = [];

    /// <summary>Текст пошуку.</summary>
    [ObservableProperty]
    public partial string SearchText { get; set; } = string.Empty;

    /// <summary>Чи показувати деактивовані записи.</summary>
    [ObservableProperty]
    public partial bool ShowInactive { get; set; }

    /// <summary>Вибраний запис.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(EditCommand), nameof(ToggleActiveCommand))]
    [NotifyPropertyChangedFor(nameof(ToggleActiveText))]
    public partial TItem? SelectedItem { get; set; }

    /// <summary>Чи триває завантаження.</summary>
    [ObservableProperty]
    public partial bool IsBusy { get; private set; }

    /// <summary>Чи може поточний користувач змінювати довідник (інакше — лише перегляд).</summary>
    public bool CanEdit => CurrentUser.Session is { } session && EditRoles.Contains(session.Role);

    /// <summary>Підпис кнопки деактивації залежно від стану вибраного запису.</summary>
    public string ToggleActiveText => SelectedItem is { IsActive: false } ? "Активувати" : "Деактивувати";

    /// <summary>Кількість показаних записів.</summary>
    public string CountText => $"Показано {Items.Count} з {_allItems.Count}";

    /// <summary>Ролі, яким дозволено змінювати довідник.</summary>
    protected abstract UserRole[] EditRoles { get; }

    /// <summary>Завантажує записи через сервіс довідника.</summary>
    /// <returns>Усі записи, зокрема неактивні.</returns>
    protected abstract Task<IReadOnlyList<TItem>> LoadItemsAsync();

    /// <summary>Чи відповідає запис тексту пошуку.</summary>
    /// <param name="item">Запис.</param>
    /// <param name="search">Текст пошуку.</param>
    /// <returns><c>true</c>, якщо відповідає.</returns>
    protected abstract bool Matches(TItem item, string search);

    /// <summary>Створює форму для нового (<c>null</c>) або наявного запису.</summary>
    /// <param name="item">Запис для редагування або <c>null</c>.</param>
    /// <returns>ViewModel форми.</returns>
    protected abstract EditViewModelBase CreateEditor(TItem? item);

    /// <summary>Активує або деактивує запис через сервіс.</summary>
    /// <param name="id">Ідентифікатор.</param>
    /// <param name="isActive">Нова ознака активності.</param>
    /// <returns>Задача.</returns>
    protected abstract Task SetActiveAsync(int id, bool isActive);

    /// <summary>Коротка назва запису для запитань підтвердження.</summary>
    /// <param name="item">Запис.</param>
    /// <returns>Назва.</returns>
    protected abstract string Describe(TItem item);

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    partial void OnShowInactiveChanged(bool value) => ApplyFilter();

    private bool CanChangeSelected() => CanEdit && SelectedItem is not null;

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsBusy = true;
        try
        {
            _allItems = await LoadItemsAsync();
            ApplyFilter();
        }
        catch (DomainException ex)
        {
            Dialogs.ShowError(ex.UserMessage);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private async Task AddAsync()
    {
        if (EditDialogs.ShowEditor(CreateEditor(null)))
        {
            await LoadAsync();
        }
    }

    [RelayCommand(CanExecute = nameof(CanChangeSelected))]
    private async Task EditAsync()
    {
        if (SelectedItem is { } item && EditDialogs.ShowEditor(CreateEditor(item)))
        {
            await LoadAsync();
        }
    }

    [RelayCommand(CanExecute = nameof(CanChangeSelected))]
    private async Task ToggleActiveAsync()
    {
        if (SelectedItem is not { } item)
        {
            return;
        }

        bool activate = !item.IsActive;
        string question = activate
            ? $"Активувати «{Describe(item)}»?"
            : $"Деактивувати «{Describe(item)}»?\n\nЗапис залишиться в історії замовлень, але не пропонуватиметься для нових.";
        if (!Dialogs.Confirm(question))
        {
            return;
        }

        try
        {
            await SetActiveAsync(item.Id, activate);
            await LoadAsync();
        }
        catch (DomainException ex)
        {
            Dialogs.ShowWarning(ex.UserMessage);
        }
    }

    private void ApplyFilter()
    {
        int? selectedId = SelectedItem?.Id;

        Items.Clear();
        foreach (TItem item in _allItems.Where(item => (ShowInactive || item.IsActive) && Matches(item, SearchText)))
        {
            Items.Add(item);
        }

        // Після оновлення списку виділення лишається на тому самому записі
        SelectedItem = Items.FirstOrDefault(item => item.Id == selectedId);
        OnPropertyChanged(nameof(CountText));
    }
}
