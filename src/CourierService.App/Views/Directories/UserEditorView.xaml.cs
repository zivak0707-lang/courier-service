using System.Windows;
using System.Windows.Controls;
using CourierService.App.ViewModels.Directories;

namespace CourierService.App.Views.Directories;

/// <summary>
/// Поля форми облікового запису.
/// </summary>
public partial class UserEditorView : UserControl
{
    /// <summary>
    /// Створює сторінку.
    /// </summary>
    public UserEditorView()
    {
        InitializeComponent();
    }

    // PasswordBox не підтримує прив'язку даних — передаємо пароль у ViewModel вручну
    private void OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is UserEditorViewModel viewModel)
        {
            viewModel.Password = PasswordBox.Password;
        }
    }
}
