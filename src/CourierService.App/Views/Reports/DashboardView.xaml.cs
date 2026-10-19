using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using CourierService.App.ViewModels.Reports;
using CourierService.Core.Enums;
using CourierService.Core.Reports;
using ScottPlot;
using ScottPlot.WPF;

namespace CourierService.App.Views.Reports;

/// <summary>
/// Дашборд. Графіки ScottPlot не прив'язуються до даних через XAML, тому код вигляду передає в них
/// готові числа з <see cref="DashboardSnapshot"/> — це лише відображення (рішення D-10).
/// </summary>
public partial class DashboardView : UserControl
{
    private const string FontName = "Segoe UI";
    private static readonly Color RevenueColor = Color.FromHex("#2F6FB3");
    private static readonly Color GrossColor = Color.FromHex("#2E9D5B");
    private static readonly Color BarColor = Color.FromHex("#5B8FD0");

    private DashboardViewModel? _viewModel;

    /// <summary>
    /// Створює сторінку.
    /// </summary>
    public DashboardView()
    {
        InitializeComponent();
        foreach (WpfPlot plot in new[] { DailyPlot, StatusPlot, CourierPlot })
        {
            // Шрифт з кирилицею; масштабування мишею вимкнено, щоб графік випадково не «з'їхав»
            plot.Plot.Font.Set(FontName);
            plot.UserInputProcessor.Disable();
        }

        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _viewModel = e.NewValue as DashboardViewModel;
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            Draw(_viewModel.Snapshot);
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DashboardViewModel.Snapshot))
        {
            Draw(_viewModel?.Snapshot);
        }
    }

    private void Draw(DashboardSnapshot? snapshot)
    {
        DailyPlot.Plot.Clear();
        StatusPlot.Plot.Clear();
        CourierPlot.Plot.Clear();

        if (snapshot is not null)
        {
            DrawDaily(snapshot.Daily);
            DrawStatuses(snapshot.Statuses);
            DrawCouriers(snapshot.TopCouriers);
        }

        DailyPlot.Refresh();
        StatusPlot.Refresh();
        CourierPlot.Refresh();
    }

    private void DrawDaily(IReadOnlyList<DailyPoint> daily)
    {
        Plot plot = DailyPlot.Plot;
        double[] days = daily.Select(point => point.Day.ToOADate()).ToArray();

        var revenue = plot.Add.Scatter(days, daily.Select(point => (double)point.Revenue.Hryvnias).ToArray());
        revenue.LegendText = "Виручка";
        revenue.Color = RevenueColor;
        revenue.LineWidth = 2;

        var gross = plot.Add.Scatter(days, daily.Select(point => (double)point.GrossProfit.Hryvnias).ToArray());
        gross.LegendText = "Валовий дохід";
        gross.Color = GrossColor;
        gross.LineWidth = 2;

        plot.Axes.DateTimeTicksBottom();
        plot.Axes.Left.Label.Text = "грн";
        plot.ShowLegend(Alignment.UpperLeft);
    }

    private void DrawStatuses(IReadOnlyList<StatusCount> statuses)
    {
        Plot plot = StatusPlot.Plot;
        var bars = plot.Add.Bars(statuses.Select(status => (double)status.Count).ToArray());
        int index = 0;
        foreach (Bar bar in bars.Bars)
        {
            bar.FillColor = BarColor;
            bar.Label = statuses[index++].Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        bars.ValueLabelStyle.FontName = FontName;
        plot.Axes.Bottom.SetTicks(
            Enumerable.Range(0, statuses.Count).Select(i => (double)i).ToArray(),
            statuses.Select(status => status.Status.ToDisplayName()).ToArray());
        plot.Axes.Margins(bottom: 0, top: 0.15);
    }

    private void DrawCouriers(IReadOnlyList<CourierScore> couriers)
    {
        Plot plot = CourierPlot.Plot;

        // Найкращий — угорі: горизонтальні стовпці малюються знизу вгору
        List<CourierScore> ordered = couriers.Reverse().ToList();
        var bars = plot.Add.Bars(ordered.Select(score => (double)score.Delivered).ToArray());
        bars.Horizontal = true;
        int index = 0;
        foreach (Bar bar in bars.Bars)
        {
            CourierScore score = ordered[index++];
            bar.FillColor = GrossColor;
            bar.Label = score.OnTimePercent is { } percent ? $"{score.Delivered} ({percent:0.0} % вчасно)" : $"{score.Delivered}";
        }

        bars.ValueLabelStyle.FontName = FontName;
        plot.Axes.Left.SetTicks(
            Enumerable.Range(0, ordered.Count).Select(i => (double)i).ToArray(),
            ordered.Select(score => score.FullName).ToArray());
        plot.Axes.Margins(left: 0, right: 0.6);
    }
}
