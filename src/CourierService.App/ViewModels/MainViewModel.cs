using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CourierService.App.Help;
using CourierService.App.Navigation;
using CourierService.App.Services;
using CourierService.App.ViewModels.Administration;
using CourierService.App.ViewModels.Directories;
using CourierService.App.ViewModels.Orders;
using CourierService.App.ViewModels.Reports;
using CourierService.Core.Enums;
using CourierService.Core.Security;

namespace CourierService.App.ViewModels;

/// <summary>
/// ViewModel головного вікна: команди меню, поточна сторінка, меню за роллю, вихід із програми.
/// </summary>
public sealed partial class MainViewModel : ViewModelBase
{
    /// <summary>Текст запиту підтвердження виходу.</summary>
    public const string ExitConfirmationMessage = "Завершити роботу з програмою?";

    /// <summary>Текст запиту підтвердження зміни користувача.</summary>
    public const string ChangeUserConfirmationMessage = "Завершити сеанс і увійти під іншим користувачем?";

    private readonly INavigationService _navigation;
    private readonly IDialogService _dialogs;
    private readonly IAppLifetime _lifetime;
    private readonly ICurrentUser _currentUser;
    private readonly ILoginDialog _loginDialog;
    private readonly IOrderFormLauncher _orderForm;
    private bool _exitConfirmed;

    /// <summary>
    /// Створює ViewModel головного вікна й відкриває стартову сторінку.
    /// </summary>
    /// <param name="navigation">Сервіс навігації між сторінками.</param>
    /// <param name="dialogs">Сервіс повідомлень і діалогів.</param>
    /// <param name="lifetime">Сервіс завершення роботи застосунку.</param>
    /// <param name="currentUser">Поточний користувач.</param>
    /// <param name="loginDialog">Вікно входу для зміни користувача.</param>
    /// <param name="orderForm">Форма нового замовлення (модальне вікно, рішення D-21).</param>
    public MainViewModel(
        INavigationService navigation,
        IDialogService dialogs,
        IAppLifetime lifetime,
        ICurrentUser currentUser,
        ILoginDialog loginDialog,
        IOrderFormLauncher orderForm)
    {
        _navigation = navigation;
        _dialogs = dialogs;
        _lifetime = lifetime;
        _currentUser = currentUser;
        _loginDialog = loginDialog;
        _orderForm = orderForm;

        _navigation.CurrentViewModelChanged += (_, _) => CurrentPage = _navigation.CurrentViewModel;
        _currentUser.Changed += (_, _) => OnCurrentUserChanged();
        _currentUser.SessionUpdated += (_, _) => OnPropertyChanged(nameof(CurrentUserText));
        _navigation.NavigateTo<HomeViewModel>();
    }

    /// <summary>
    /// Реалізовані розділи та їхні сторінки. Розділи, яких тут немає, поки показують заглушку.
    /// </summary>
    public static IReadOnlyDictionary<AppSection, Type> SectionPages { get; } = new Dictionary<AppSection, Type>
    {
        [AppSection.OrderList] = typeof(OrderListViewModel),
        [AppSection.TrackingSearch] = typeof(TrackingSearchViewModel),
        [AppSection.MyActiveDeliveries] = typeof(MyActiveDeliveriesViewModel),
        [AppSection.MyCompletedDeliveries] = typeof(MyCompletedDeliveriesViewModel),
        [AppSection.Dashboard] = typeof(DashboardViewModel),
        [AppSection.RevenueReport] = typeof(RevenueReportViewModel),
        [AppSection.UnpaidReport] = typeof(UnpaidReportViewModel),
        [AppSection.CourierPerformanceReport] = typeof(CourierPerformanceReportViewModel),
        [AppSection.CourierPayoutReport] = typeof(CourierPayoutReportViewModel),
        [AppSection.Zones] = typeof(ZonesViewModel),
        [AppSection.Tariffs] = typeof(TariffsViewModel),
        [AppSection.Clients] = typeof(ClientsViewModel),
        [AppSection.Couriers] = typeof(CouriersViewModel),
        [AppSection.Users] = typeof(UsersViewModel),
        [AppSection.Backups] = typeof(BackupsViewModel),
    };

    /// <inheritdoc />
    public override string Title => "Кур'єрська служба";

    /// <summary>Сторінка, що показується в робочій області вікна.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WindowTitle))]
    public partial ViewModelBase? CurrentPage { get; private set; }

    /// <summary>Заголовок вікна: назва програми й поточної сторінки.</summary>
    public string WindowTitle => CurrentPage is null ? Title : $"{Title} — {CurrentPage.Title}";

    /// <summary>Роль поточного користувача (визначає видимі пункти меню).</summary>
    public UserRole? CurrentRole => _currentUser.Session?.Role;

    /// <summary>Відомості про поточного користувача для рядка стану.</summary>
    public string CurrentUserText => _currentUser.Session is { } session
        ? $"{session.FullName} ({session.Role.ToDisplayName()})"
        : "Вхід не виконано";

    /// <summary>Чи показувати меню «Замовлення».</summary>
    public bool IsOrdersMenuVisible => SectionAccess.IsAnyAllowed(SectionAccess.OrderSections, CurrentRole);

    /// <summary>Чи показувати меню «Мої доставки».</summary>
    public bool IsMyDeliveriesMenuVisible => SectionAccess.IsAnyAllowed(SectionAccess.MyDeliverySections, CurrentRole);

    /// <summary>Чи показувати меню «Довідники».</summary>
    public bool IsDirectoriesMenuVisible => SectionAccess.IsAnyAllowed(SectionAccess.DirectorySections, CurrentRole);

    /// <summary>Чи показувати меню «Звіти».</summary>
    public bool IsReportsMenuVisible => SectionAccess.IsAnyAllowed(SectionAccess.ReportSections, CurrentRole);

    /// <summary>Чи показувати меню «Адміністрування».</summary>
    public bool IsAdministrationMenuVisible => SectionAccess.IsAnyAllowed(SectionAccess.AdministrationSections, CurrentRole);

    /// <summary>
    /// Перевіряє, чи можна закрити програму: питає користувача один раз.
    /// Викликається і командою «Вихід», і під час закриття вікна (хрестик, Alt+F4).
    /// </summary>
    /// <returns><c>true</c>, якщо користувач підтвердив вихід.</returns>
    public bool CanClose()
    {
        // Перезапуск після відновлення бази вже підтверджено — зайве питання не потрібне
        if (!_exitConfirmed && !_lifetime.IsRestarting)
        {
            _exitConfirmed = _dialogs.Confirm(ExitConfirmationMessage);
        }

        return _exitConfirmed || _lifetime.IsRestarting;
    }

    private void OnCurrentUserChanged()
    {
        // Після зміни користувача оновлюються рядок стану й меню, а робоча область повертається на головну
        OnPropertyChanged(nameof(CurrentRole));
        OnPropertyChanged(nameof(CurrentUserText));
        OnPropertyChanged(nameof(IsOrdersMenuVisible));
        OnPropertyChanged(nameof(IsMyDeliveriesMenuVisible));
        OnPropertyChanged(nameof(IsDirectoriesMenuVisible));
        OnPropertyChanged(nameof(IsReportsMenuVisible));
        OnPropertyChanged(nameof(IsAdministrationMenuVisible));
        _navigation.NavigateTo<HomeViewModel>();
    }

    [RelayCommand]
    private void ShowHome() => _navigation.NavigateTo<HomeViewModel>();

    [RelayCommand]
    private async Task OpenSectionAsync(AppSection section)
    {
        // Пункт меню прихований, але команду можна викликати й гарячою клавішею
        if (!SectionAccess.IsAllowed(section, CurrentRole))
        {
            _dialogs.ShowWarning($"Розділ «{AppSectionCatalog.Get(section).Title}» недоступний для вашої ролі.");
            return;
        }

        if (section == AppSection.NewOrder)
        {
            // Якщо відкрито список замовлень, нове замовлення одразу з'являється в ньому
            if (await _orderForm.OpenNewOrderAsync() is not null && CurrentPage is OrderListViewModel list)
            {
                await list.SearchCommand.ExecuteAsync(null);
            }

            return;
        }

        if (SectionPages.TryGetValue(section, out Type? pageType))
        {
            _navigation.NavigateTo(pageType);
        }
        else
        {
            _navigation.NavigateTo(new PlaceholderViewModel(AppSectionCatalog.Get(section)));
        }
    }

    /// <summary>
    /// Визначає розділ меню за типом сторінки (для контекстної довідки).
    /// </summary>
    /// <param name="pageType">Тип ViewModel сторінки або <c>null</c>.</param>
    /// <returns>Розділ меню або <c>null</c>, якщо сторінка не належить жодному розділу (головна, довідка).</returns>
    public static AppSection? SectionOf(Type? pageType) =>
        SectionPages.Where(page => page.Value == pageType).Select(page => (AppSection?)page.Key).FirstOrDefault();

    [RelayCommand]
    private void ShowHelp()
    {
        // F1 на сторінці довідки нічого не змінює, на інших — відкриває розділ про поточну сторінку
        if (CurrentPage is HelpViewModel)
        {
            return;
        }

        _navigation.NavigateTo(new HelpViewModel(HelpContent.ForSection(SectionOf(CurrentPage?.GetType()))));
    }

    [RelayCommand]
    private void ShowAbout() => _dialogs.ShowAbout();

    [RelayCommand]
    private void ChangeUser()
    {
        if (!_dialogs.Confirm(ChangeUserConfirmationMessage))
        {
            return;
        }

        _currentUser.SignOut();
        if (!_loginDialog.ShowLogin())
        {
            // Сеанс уже завершено, а новий вхід скасовано — програма закривається без зайвого питання
            _exitConfirmed = true;
            _lifetime.Shutdown();
        }
    }

    [RelayCommand]
    private void Exit()
    {
        if (CanClose())
        {
            _lifetime.Shutdown();
        }
    }
}
