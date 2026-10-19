using System.Windows;
using CourierService.App.ViewModels;
using CourierService.App.Views;
using Microsoft.Extensions.DependencyInjection;

namespace CourierService.App.Services;

/// <summary>
/// Показ вікна входу.
/// </summary>
public interface ILoginDialog
{
    /// <summary>
    /// Показує модальне вікно входу.
    /// </summary>
    /// <returns><c>true</c>, якщо користувач увійшов; <c>false</c>, якщо закрив вікно.</returns>
    bool ShowLogin();
}

/// <summary>
/// Реалізація <see cref="ILoginDialog"/>: щоразу створює нове вікно з новою ViewModel.
/// </summary>
public sealed class LoginDialog : ILoginDialog
{
    private readonly IServiceProvider _services;

    /// <summary>
    /// Створює сервіс.
    /// </summary>
    /// <param name="services">DI-контейнер для створення ViewModel вікна входу.</param>
    public LoginDialog(IServiceProvider services)
    {
        _services = services;
    }

    /// <inheritdoc />
    public bool ShowLogin()
    {
        var window = new LoginWindow(_services.GetRequiredService<LoginViewModel>());

        // Під час зміни користувача вікно входу відкривається над головним вікном
        Window? owner = Application.Current?.MainWindow;
        if (owner is { IsVisible: true })
        {
            window.Owner = owner;
        }
        else
        {
            window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        return window.ShowDialog() == true;
    }
}
