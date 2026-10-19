using CourierService.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace CourierService.App.Services;

/// <summary>
/// Реалізація <see cref="INavigationService"/>: зберігає поточну ViewModel і повідомляє про її зміну.
/// </summary>
public sealed class NavigationService : INavigationService
{
    private readonly IServiceProvider _services;

    /// <summary>
    /// Створює сервіс навігації.
    /// </summary>
    /// <param name="services">DI-контейнер, з якого створюються ViewModel-и сторінок.</param>
    public NavigationService(IServiceProvider services)
    {
        _services = services;
    }

    /// <inheritdoc />
    public ViewModelBase? CurrentViewModel { get; private set; }

    /// <inheritdoc />
    public event EventHandler? CurrentViewModelChanged;

    /// <inheritdoc />
    public void NavigateTo<TViewModel>() where TViewModel : ViewModelBase =>
        NavigateTo(_services.GetRequiredService<TViewModel>());

    /// <inheritdoc />
    public void NavigateTo(ViewModelBase viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        CurrentViewModel = viewModel;
        CurrentViewModelChanged?.Invoke(this, EventArgs.Empty);
    }
}
