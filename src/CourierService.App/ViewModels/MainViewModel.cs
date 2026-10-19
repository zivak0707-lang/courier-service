using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CourierService.App.Navigation;
using CourierService.App.Services;

namespace CourierService.App.ViewModels;

/// <summary>
/// ViewModel головного вікна: команди меню, поточна сторінка, вихід із програми.
/// </summary>
public sealed partial class MainViewModel : ViewModelBase
{
    /// <summary>Текст запиту підтвердження виходу.</summary>
    public const string ExitConfirmationMessage = "Завершити роботу з програмою?";

    private readonly INavigationService _navigation;
    private readonly IDialogService _dialogs;
    private readonly IAppLifetime _lifetime;
    private bool _exitConfirmed;

    /// <summary>
    /// Створює ViewModel головного вікна й відкриває стартову сторінку.
    /// </summary>
    /// <param name="navigation">Сервіс навігації між сторінками.</param>
    /// <param name="dialogs">Сервіс повідомлень і діалогів.</param>
    /// <param name="lifetime">Сервіс завершення роботи застосунку.</param>
    public MainViewModel(INavigationService navigation, IDialogService dialogs, IAppLifetime lifetime)
    {
        _navigation = navigation;
        _dialogs = dialogs;
        _lifetime = lifetime;

        _navigation.CurrentViewModelChanged += (_, _) => CurrentPage = _navigation.CurrentViewModel;
        _navigation.NavigateTo<HomeViewModel>();
    }

    /// <inheritdoc />
    public override string Title => "Кур'єрська служба";

    /// <summary>Сторінка, що показується в робочій області вікна.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WindowTitle))]
    public partial ViewModelBase? CurrentPage { get; private set; }

    /// <summary>Заголовок вікна: назва програми й поточної сторінки.</summary>
    public string WindowTitle => CurrentPage is null ? Title : $"{Title} — {CurrentPage.Title}";

    /// <summary>Відомості про поточного користувача для рядка стану.</summary>
    public string CurrentUserText => "Вхід у систему з'явиться в ітерації 2";

    /// <summary>
    /// Перевіряє, чи можна закрити програму: питає користувача один раз.
    /// Викликається і командою «Вихід», і під час закриття вікна (хрестик, Alt+F4).
    /// </summary>
    /// <returns><c>true</c>, якщо користувач підтвердив вихід.</returns>
    public bool CanClose()
    {
        if (!_exitConfirmed)
        {
            _exitConfirmed = _dialogs.Confirm(ExitConfirmationMessage);
        }

        return _exitConfirmed;
    }

    [RelayCommand]
    private void ShowHome() => _navigation.NavigateTo<HomeViewModel>();

    [RelayCommand]
    private void OpenSection(AppSection section) =>
        _navigation.NavigateTo(new PlaceholderViewModel(AppSectionCatalog.Get(section)));

    [RelayCommand]
    private void ShowHelp() => _navigation.NavigateTo<HelpViewModel>();

    [RelayCommand]
    private void ShowAbout() => _dialogs.ShowAbout();

    [RelayCommand]
    private void Exit()
    {
        if (CanClose())
        {
            _lifetime.Shutdown();
        }
    }
}
