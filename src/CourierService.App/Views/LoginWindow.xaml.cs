using System.Windows;
using CourierService.App.ViewModels;

namespace CourierService.App.Views;

/// <summary>
/// Модальне вікно входу. Логіка — у <see cref="LoginViewModel"/>.
/// </summary>
public partial class LoginWindow : Window
{
    private readonly LoginViewModel _viewModel;

    /// <summary>
    /// Створює вікно входу.
    /// </summary>
    /// <param name="viewModel">ViewModel вікна входу.</param>
    public LoginWindow(LoginViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;

        viewModel.SignedIn += (_, _) => DialogResult = true;
        Loaded += (_, _) =>
        {
            LoginBox.Focus();
            viewModel.InitializeCommand.Execute(null);
        };
    }

    // PasswordBox не підтримує прив'язку (щоб пароль не зберігався у властивостях),
    // тому передаємо його у ViewModel вручну
    private void OnPasswordChanged(object sender, RoutedEventArgs e) =>
        _viewModel.Password = PasswordBox.Password;
}
