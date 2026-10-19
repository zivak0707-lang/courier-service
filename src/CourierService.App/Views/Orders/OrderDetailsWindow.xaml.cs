using System.Windows;
using CourierService.App.ViewModels.Orders;

namespace CourierService.App.Views.Orders;

/// <summary>
/// Модальне вікно картки замовлення.
/// </summary>
public partial class OrderDetailsWindow : Window
{
    /// <summary>
    /// Створює вікно й завантажує дані замовлення після відкриття.
    /// </summary>
    /// <param name="viewModel">ViewModel картки.</param>
    public OrderDetailsWindow(OrderDetailsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Loaded += (_, _) => viewModel.LoadCommand.Execute(null);
    }
}
