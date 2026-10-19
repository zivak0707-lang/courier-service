using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CourierService.App.Services;
using CourierService.Core.Abstractions;
using CourierService.Core.Exceptions;
using CourierService.Core.Reports;

namespace CourierService.App.ViewModels.Reports;

/// <summary>
/// Дашборд (рішення D-33): плитки показників і графіки за 7, 30 або 90 днів.
/// Числа рахує <see cref="DashboardCalculator"/> (Core), вигляд лише малює їх.
/// </summary>
public sealed partial class DashboardViewModel : ViewModelBase, ILoadable
{
    private readonly IReportService _reports;
    private readonly IDialogService _dialogs;
    private readonly IClock _clock;

    /// <summary>
    /// Створює сторінку.
    /// </summary>
    /// <param name="reports">Сервіс звітів (дані за період).</param>
    /// <param name="dialogs">Повідомлення.</param>
    /// <param name="clock">Поточний час.</param>
    public DashboardViewModel(IReportService reports, IDialogService dialogs, IClock clock)
    {
        _reports = reports;
        _dialogs = dialogs;
        _clock = clock;
    }

    /// <inheritdoc />
    public override string Title => "Дашборд";

    /// <summary>Варіанти періоду, днів.</summary>
    public IReadOnlyList<int> PeriodOptions { get; } = [7, 30, 90];

    /// <summary>Обраний період, днів.</summary>
    [ObservableProperty]
    public partial int Days { get; set; } = 30;

    /// <summary>Дані дашборда.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasIncompleteDay))]
    public partial DashboardSnapshot? Snapshot { get; private set; }

    /// <summary>Чи є на графіку сьогоднішній неповний день (тоді під графіком показується пояснення пунктиру).</summary>
    public bool HasIncompleteDay => Snapshot?.Daily.Any(point => point.IsIncomplete) == true;

    /// <summary>Чи триває завантаження.</summary>
    [ObservableProperty]
    public partial bool IsBusy { get; private set; }

    partial void OnDaysChanged(int value) => LoadCommand.Execute(null);

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsBusy = true;
        try
        {
            ReportData data = await _reports.GetDataAsync(ReportPeriod.LastDays(_clock.Now, Days));
            Snapshot = DashboardCalculator.Calculate(data);
        }
        catch (DomainException ex)
        {
            _dialogs.ShowError(ex.UserMessage);
        }
        finally
        {
            IsBusy = false;
        }
    }
}
