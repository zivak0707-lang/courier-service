using System.Windows;
using System.Windows.Controls;
using CourierService.App.ViewModels.Directories;

namespace CourierService.App.Views.Directories;

/// <summary>
/// Поля форми кур'єра.
/// </summary>
public partial class CourierEditorView : UserControl
{
    /// <summary>
    /// Створює сторінку.
    /// </summary>
    public CourierEditorView()
    {
        InitializeComponent();
    }

    // PasswordBox не підтримує прив'язку даних — передаємо пароль у ViewModel вручну
    private void OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is CourierEditorViewModel viewModel)
        {
            viewModel.Password = PasswordBox.Password;
        }
    }
}
