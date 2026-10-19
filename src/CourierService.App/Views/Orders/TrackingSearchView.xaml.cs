using System.Windows;
using System.Windows.Controls;

namespace CourierService.App.Views.Orders;

/// <summary>
/// Пошук замовлення за трек-номером.
/// </summary>
public partial class TrackingSearchView : UserControl
{
    /// <summary>
    /// Створює сторінку.
    /// </summary>
    public TrackingSearchView()
    {
        InitializeComponent();
    }

    // Курсор одразу в полі номера: оператор може вводити, щойно відкрив сторінку
    private void OnSearchBoxLoaded(object sender, RoutedEventArgs e) => ((TextBox)sender).Focus();
}
