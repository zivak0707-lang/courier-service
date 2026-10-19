using CourierService.App.ViewModels;

namespace CourierService.App.Services;

/// <summary>
/// Перемикання сторінок у головному вікні.
/// </summary>
/// <remarks>
/// Навігація відбувається між ViewModel-ами; відповідну View WPF обирає сам
/// за <c>DataTemplate</c> у ресурсах застосунку.
/// </remarks>
public interface INavigationService
{
    /// <summary>ViewModel поточної сторінки.</summary>
    ViewModelBase? CurrentViewModel { get; }

    /// <summary>Виникає після переходу на іншу сторінку.</summary>
    event EventHandler? CurrentViewModelChanged;

    /// <summary>
    /// Переходить на сторінку, ViewModel якої створює DI-контейнер.
    /// </summary>
    /// <typeparam name="TViewModel">Тип ViewModel сторінки.</typeparam>
    void NavigateTo<TViewModel>() where TViewModel : ViewModelBase;

    /// <summary>
    /// Переходить на сторінку з уже створеною ViewModel.
    /// </summary>
    /// <param name="viewModel">ViewModel сторінки.</param>
    void NavigateTo(ViewModelBase viewModel);
}
