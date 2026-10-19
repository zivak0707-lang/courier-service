using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using CourierService.App.ViewModels.Reports;
using CourierService.Core;
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

        // Сьогоднішній день ще триває: суцільна лінія — до вчора, останній відрізок — пунктиром,
        // щоб «провал» неповного дня не виглядав як падіння виручки (огляд C4)
        int complete = daily.Count > 0 && daily[^1].IsIncomplete ? daily.Count - 1 : daily.Count;
        DrawSeries(plot, daily.Take(complete).ToList(), point => point.Revenue, RevenueColor, "Виручка", LinePattern.Solid);
        DrawSeries(plot, daily.Take(complete).ToList(), point => point.GrossProfit, GrossColor, "Валовий дохід", LinePattern.Solid);
        if (complete < daily.Count)
        {
            List<DailyPoint> tail = daily.Skip(Math.Max(0, complete - 1)).ToList();
            DrawSeries(plot, tail, point => point.Revenue, RevenueColor, null, LinePattern.Dashed);
            DrawSeries(plot, tail, point => point.GrossProfit, GrossColor, null, LinePattern.Dashed);
        }

        plot.Axes.DateTimeTicksBottom();
        plot.Axes.Left.Label.Text = "грн";
        plot.ShowLegend(Alignment.UpperLeft);
    }

    private static void DrawSeries(
        Plot plot, IReadOnlyList<DailyPoint> points, Func<DailyPoint, Money> value, Color color, string? legend, LinePattern pattern)
    {
        if (points.Count == 0)
        {
            return;
        }

        var series = plot.Add.Scatter(
            points.Select(point => point.Day.ToOADate()).ToArray(),
            points.Select(point => (double)value(point).Hryvnias).ToArray());
        series.Color = color;
        series.LineWidth = 2;
        series.LinePattern = pattern;
        if (legend is not null)
        {
            series.LegendText = legend;
        }
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
