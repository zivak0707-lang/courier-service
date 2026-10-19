using CourierService.App.Services;
using Microsoft.Extensions.DependencyInjection;

namespace CourierService.App.Infrastructure;

/// <summary>
/// Єдине місце, де визначено, яка реалізація відповідає кожному інтерфейсу (рішення D-03).
/// </summary>
public static class CompositionRoot
{
    /// <summary>
    /// Реєструє сервіси, ViewModel-и та вікна застосунку.
    /// </summary>
    /// <param name="services">Колекція сервісів DI-контейнера.</param>
    /// <returns>Ту саму колекцію для ланцюжкових викликів.</returns>
    public static IServiceCollection AddCourierApp(this IServiceCollection services)
    {
        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<GlobalExceptionHandler>();
        services.AddSingleton<MainWindow>();
        return services;
    }
}
