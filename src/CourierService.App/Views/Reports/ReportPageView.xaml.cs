using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using CourierService.App.ViewModels.Reports;
using CourierService.Core.Reports;

namespace CourierService.App.Views.Reports;

/// <summary>
/// Сторінка звіту. Колонки в кожного звіту різні, тому таблицю налаштовує код вигляду —
/// це лише відображення, дані й обчислення — у ViewModel і Core.
/// </summary>
public partial class ReportPageView : UserControl
{
    private ReportPageViewModel? _viewModel;

    /// <summary>
    /// Створює сторінку.
    /// </summary>
    public ReportPageView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _viewModel = e.NewValue as ReportPageViewModel;
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            BuildColumns(_viewModel.Table);
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ReportPageViewModel.Table))
        {
            BuildColumns(_viewModel?.Table);
        }
    }

    private void BuildColumns(ReportTable? table)
    {
        ReportGrid.Columns.Clear();
        if (table is null)
        {
            return;
        }

        for (int index = 0; index < table.Columns.Count; index++)
        {
            ReportColumn column = table.Columns[index];
            var textColumn = new DataGridTextColumn
            {
                Header = column.Header,
                Binding = new Binding($"Cells[{index}]"),
                Width = column.Kind == ReportValueKind.Text ? new DataGridLength(1, DataGridLengthUnitType.Star) : DataGridLength.Auto,
                MinWidth = 80,
                // Сортування за текстом дало б «10» раніше за «9», тому клік по заголовку вимкнено
                CanUserSort = false,
            };

            // Числа вирівнюються праворуч, як у бухгалтерських таблицях
            if (column.Kind is not (ReportValueKind.Text or ReportValueKind.Date or ReportValueKind.DateTime))
            {
                textColumn.ElementStyle = new Style(typeof(TextBlock))
                {
                    Setters = { new Setter(TextBlock.TextAlignmentProperty, TextAlignment.Right) },
                };
            }

            ReportGrid.Columns.Add(textColumn);
        }
    }
}
