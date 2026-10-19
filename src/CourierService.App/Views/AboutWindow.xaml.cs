using System.Windows;
using CourierService.App.ViewModels;

namespace CourierService.App.Views;

/// <summary>
/// Модальне вікно «Про програму».
/// </summary>
public partial class AboutWindow : Window
{
    /// <summary>
    /// Створює вікно з відомостями про програму.
    /// </summary>
    /// <param name="viewModel">Відомості про програму.</param>
    public AboutWindow(AboutViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
