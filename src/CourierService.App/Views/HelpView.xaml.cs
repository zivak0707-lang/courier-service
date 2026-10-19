using System.Windows;
using System.Windows.Controls;

namespace CourierService.App.Views;

/// <summary>
/// Сторінка довідки.
/// </summary>
public partial class HelpView : UserControl
{
    /// <summary>
    /// Створює сторінку.
    /// </summary>
    public HelpView()
    {
        InitializeComponent();
    }

    // Розділ, відкритий з F1, може бути внизу змісту — прокручуємо до нього
    private void OnTopicListLoaded(object sender, RoutedEventArgs e) => ScrollToSelected();

    private void OnTopicSelectionChanged(object sender, SelectionChangedEventArgs e) => ScrollToSelected();

    private void ScrollToSelected()
    {
        if (TopicList.SelectedItem is { } topic)
        {
            TopicList.ScrollIntoView(topic);
        }
    }
}
