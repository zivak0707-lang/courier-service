using CommunityToolkit.Mvvm.ComponentModel;

namespace CourierService.App.ViewModels;

/// <summary>
/// Базовий клас для всіх ViewModel-ів застосунку.
/// </summary>
/// <remarks>
/// <see cref="ObservableObject"/> з CommunityToolkit.Mvvm реалізує INotifyPropertyChanged,
/// тому інтерфейс оновлюється, щойно змінюється властивість ViewModel.
/// </remarks>
public abstract class ViewModelBase : ObservableObject
{
    /// <summary>
    /// Заголовок сторінки, що показується в заголовку головного вікна.
    /// Кожен нащадок визначає власний заголовок (поліморфізм).
    /// </summary>
    public abstract string Title { get; }
}
