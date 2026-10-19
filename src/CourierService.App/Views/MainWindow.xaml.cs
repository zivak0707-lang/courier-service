using System.ComponentModel;
using System.Windows;
using CourierService.App.ViewModels;

namespace CourierService.App.Views;

/// <summary>
/// Головне вікно застосунку. Уся логіка — у <see cref="MainViewModel"/>.
/// </summary>
public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    /// <summary>
    /// Створює головне вікно.
    /// </summary>
    /// <param name="viewModel">ViewModel головного вікна.</param>
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
    }

    /// <summary>
    /// Закриття вікна (хрестик, Alt+F4) теж потребує підтвердження користувача.
    /// </summary>
    /// <param name="e">Аргументи події; <c>Cancel = true</c> скасовує закриття.</param>
    protected override void OnClosing(CancelEventArgs e)
    {
        e.Cancel = !_viewModel.CanClose();
        base.OnClosing(e);
    }
}
