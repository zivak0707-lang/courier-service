using System.ComponentModel;
using CourierService.App.Infrastructure;
using CourierService.App.Navigation;
using CourierService.App.Services;
using CourierService.App.ViewModels;
using CourierService.Core.Enums;
using CourierService.Core.Security;
using CourierService.Data;
using Microsoft.Extensions.DependencyInjection;

namespace CourierService.Tests.App;

public class MainViewModelTests
{
    private static readonly UserSession Admin = new(1, "admin", "Олена Марченко", UserRole.Admin, null);
    private static readonly UserSession Operator = new(2, "operator", "Ігор Бондар", UserRole.Operator, null);
    private static readonly UserSession Courier = new(3, "courier1", "Андрій Ткаченко", UserRole.Courier, 1);

    private readonly FakeDialogService _dialogs = new();
    private readonly FakeAppLifetime _lifetime = new();
    private readonly CurrentUser _currentUser = new();
    private readonly FakeLoginDialog _loginDialog;
    private readonly MainViewModel _viewModel;

    public MainViewModelTests()
    {
        var provider = new ServiceCollection()
            .AddTransient<HomeViewModel>()
            .AddTransient<HelpViewModel>()
            .BuildServiceProvider();

        _loginDialog = new FakeLoginDialog(_currentUser);
        _currentUser.SignIn(Operator);
        _viewModel = new MainViewModel(new NavigationService(provider), _dialogs, _lifetime, _currentUser, _loginDialog, new FakeOrderFormLauncher());
    }

    private static UserSession SessionFor(UserRole role) => role switch
    {
        UserRole.Admin => Admin,
        UserRole.Operator => Operator,
        _ => Courier,
    };

    public static TheoryData<AppSection> AllSections()
    {
        var data = new TheoryData<AppSection>();
        foreach (AppSection section in Enum.GetValues<AppSection>())
        {
            data.Add(section);
        }

        return data;
    }

    [Fact]
    public void Constructor_OpensHomePage()
    {
        Assert.IsType<HomeViewModel>(_viewModel.CurrentPage);
        Assert.Equal("Кур'єрська служба — Головна", _viewModel.WindowTitle);
    }

    [Theory]
    [MemberData(nameof(AllSections))]
    public void OpenSection_AllowedRole_OpensPageOrPlaceholder(AppSection section)
    {
        UserRole allowedRole = Enum.GetValues<UserRole>().First(role => SectionAccess.IsAllowed(section, role));
        var navigation = new RecordingNavigationService();
        _currentUser.SignIn(SessionFor(allowedRole));
        var launcher = new FakeOrderFormLauncher();
        var viewModel = new MainViewModel(navigation, _dialogs, _lifetime, _currentUser, _loginDialog, launcher);

        viewModel.OpenSectionCommand.Execute(section);

        if (section == AppSection.NewOrder)
        {
            // Нове замовлення відкривається модальним вікном, а не сторінкою (D-21)
            Assert.Equal(1, launcher.Calls);
            Assert.Equal(typeof(HomeViewModel), Assert.Single(navigation.RequestedTypes));
        }
        else if (MainViewModel.SectionPages.TryGetValue(section, out Type? pageType))
        {
            Assert.Equal(pageType, navigation.RequestedTypes.Last());
        }
        else
        {
            var page = Assert.IsType<PlaceholderViewModel>(navigation.CurrentViewModel);
            Assert.Equal(AppSectionCatalog.Get(section).Title, page.Title);
        }

        Assert.Empty(_dialogs.Warnings);
    }

    [Fact]
    public void SectionPages_AreRegisteredInContainer()
    {
        var services = new ServiceCollection()
            .AddLogging()
            .AddCourierData(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "courier-di-test.db"))
            .AddCourierApp();
        using var provider = services.BuildServiceProvider();

        Assert.All(MainViewModel.SectionPages.Values, pageType =>
            Assert.Contains(services, descriptor => descriptor.ServiceType == pageType));
    }

    [Fact]
    public void OpenSection_ForbiddenForRole_ShowsWarningAndStaysOnPage()
    {
        _currentUser.SignIn(Courier);

        _viewModel.OpenSectionCommand.Execute(AppSection.Users);

        Assert.IsType<HomeViewModel>(_viewModel.CurrentPage);
        Assert.Contains("недоступний для вашої ролі", Assert.Single(_dialogs.Warnings));
    }

    [Theory]
    [InlineData(UserRole.Admin, true, false, true, true, true)]
    [InlineData(UserRole.Operator, true, false, true, true, false)]
    [InlineData(UserRole.Courier, false, true, false, false, false)]
    public void MenuGroups_DependOnRole(
        UserRole role, bool orders, bool myDeliveries, bool directories, bool reports, bool administration)
    {
        _currentUser.SignIn(SessionFor(role));

        Assert.Equal(orders, _viewModel.IsOrdersMenuVisible);
        Assert.Equal(myDeliveries, _viewModel.IsMyDeliveriesMenuVisible);
        Assert.Equal(directories, _viewModel.IsDirectoriesMenuVisible);
        Assert.Equal(reports, _viewModel.IsReportsMenuVisible);
        Assert.Equal(administration, _viewModel.IsAdministrationMenuVisible);
    }

    [Fact]
    public void StatusBar_ShowsUserNameAndRole()
    {
        Assert.Equal("Ігор Бондар (Оператор)", _viewModel.CurrentUserText);
    }

    [Fact]
    public void StatusBar_RefreshesAfterOwnProfileUpdate_WithoutLeavingPage()
    {
        var changed = new List<string?>();
        _viewModel.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        ViewModelBase? page = _viewModel.CurrentPage;

        _currentUser.UpdateSession(_currentUser.Session! with { FullName = "Ігор Бондаренко" });

        Assert.Contains(nameof(MainViewModel.CurrentUserText), changed);
        Assert.Equal("Ігор Бондаренко (Оператор)", _viewModel.CurrentUserText);
        Assert.Same(page, _viewModel.CurrentPage);
    }

    [Fact]
    public void UserChange_RefreshesMenuAndReturnsHome()
    {
        _viewModel.ShowHelpCommand.Execute(null);
        var changed = new List<string?>();
        ((INotifyPropertyChanged)_viewModel).PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        _currentUser.SignIn(Admin);

        Assert.Contains(nameof(MainViewModel.CurrentRole), changed);
        Assert.Contains(nameof(MainViewModel.IsAdministrationMenuVisible), changed);
        Assert.IsType<HomeViewModel>(_viewModel.CurrentPage);
        Assert.Equal("Олена Марченко (Адміністратор)", _viewModel.CurrentUserText);
    }

    [Fact]
    public void ChangeUser_UserDeclines_KeepsSession()
    {
        _dialogs.ConfirmResult = false;

        _viewModel.ChangeUserCommand.Execute(null);

        Assert.Equal(Operator, _currentUser.Session);
        Assert.Equal(0, _loginDialog.Calls);
    }

    [Fact]
    public void ChangeUser_NewUserSignsIn_ContinuesWithNewSession()
    {
        _dialogs.ConfirmResult = true;
        _loginDialog.NextUser = Courier;

        _viewModel.ChangeUserCommand.Execute(null);

        Assert.Equal(Courier, _currentUser.Session);
        Assert.Equal(0, _lifetime.ShutdownCalls);
        Assert.True(_viewModel.IsMyDeliveriesMenuVisible);
    }

    [Fact]
    public void ChangeUser_LoginCancelled_ClosesProgramWithoutSecondQuestion()
    {
        _dialogs.ConfirmResult = true;
        _loginDialog.NextUser = null;

        _viewModel.ChangeUserCommand.Execute(null);

        Assert.False(_currentUser.IsAuthenticated);
        Assert.Equal(1, _lifetime.ShutdownCalls);
        // Під час закриття вікна питання про вихід уже не ставиться
        Assert.True(_viewModel.CanClose());
        Assert.Equal([MainViewModel.ChangeUserConfirmationMessage], _dialogs.Questions);
    }

    [Theory]
    [MemberData(nameof(AllSections))]
    public void Catalog_DescribesEverySectionWithPlannedIteration(AppSection section)
    {
        AppSectionInfo info = AppSectionCatalog.Get(section);

        Assert.False(string.IsNullOrWhiteSpace(info.Title));
        Assert.False(string.IsNullOrWhiteSpace(info.Description));
        Assert.InRange(info.Iteration, 2, 7);
    }

    [Fact]
    public void Catalog_UnknownSection_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => AppSectionCatalog.Get((AppSection)999));
    }

    [Fact]
    public void ChangingPage_RaisesWindowTitleChanged()
    {
        var changed = new List<string?>();
        ((INotifyPropertyChanged)_viewModel).PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        _viewModel.ShowHelpCommand.Execute(null);

        Assert.Contains(nameof(MainViewModel.WindowTitle), changed);
        Assert.Equal("Кур'єрська служба — Допомога", _viewModel.WindowTitle);
    }

    [Fact]
    public void ShowHelp_OpensHelpPageWithTopics()
    {
        _viewModel.ShowHelpCommand.Execute(null);

        var help = Assert.IsType<HelpViewModel>(_viewModel.CurrentPage);
        Assert.NotEmpty(help.Topics);
    }

    [Fact]
    public void ShowHome_ReturnsToHomePage()
    {
        _viewModel.ShowHelpCommand.Execute(null);

        _viewModel.ShowHomeCommand.Execute(null);

        Assert.IsType<HomeViewModel>(_viewModel.CurrentPage);
    }

    [Fact]
    public void ShowAbout_OpensAboutDialog()
    {
        _viewModel.ShowAboutCommand.Execute(null);

        Assert.Equal(1, _dialogs.AboutCalls);
    }

    [Fact]
    public void Exit_UserDeclines_DoesNotShutDown()
    {
        _dialogs.ConfirmResult = false;

        _viewModel.ExitCommand.Execute(null);

        Assert.Equal(1, _dialogs.ConfirmCalls);
        Assert.Equal(0, _lifetime.ShutdownCalls);
    }

    [Fact]
    public void Exit_UserConfirms_ShutsDown()
    {
        _dialogs.ConfirmResult = true;

        _viewModel.ExitCommand.Execute(null);

        Assert.Equal(1, _lifetime.ShutdownCalls);
    }

    [Fact]
    public void CanClose_AfterConfirmedExit_DoesNotAskAgain()
    {
        // Команда «Вихід» підтверджена → під час закриття вікна питання не повторюється
        _dialogs.ConfirmResult = true;
        _viewModel.ExitCommand.Execute(null);

        bool canClose = _viewModel.CanClose();

        Assert.True(canClose);
        Assert.Equal(1, _dialogs.ConfirmCalls);
    }

    [Fact]
    public void CanClose_AfterDecline_AsksAgainNextTime()
    {
        _dialogs.ConfirmResult = false;
        Assert.False(_viewModel.CanClose());

        _dialogs.ConfirmResult = true;
        Assert.True(_viewModel.CanClose());

        Assert.Equal(2, _dialogs.ConfirmCalls);
    }
}

public class NavigationServiceTests
{
    [Fact]
    public void NavigateTo_SetsCurrentViewModelAndRaisesEvent()
    {
        var provider = new ServiceCollection().AddTransient<HomeViewModel>().BuildServiceProvider();
        var navigation = new NavigationService(provider);
        int raised = 0;
        navigation.CurrentViewModelChanged += (_, _) => raised++;

        navigation.NavigateTo<HomeViewModel>();

        Assert.IsType<HomeViewModel>(navigation.CurrentViewModel);
        Assert.Equal(1, raised);
    }

    [Fact]
    public void NavigateTo_Null_Throws()
    {
        var navigation = new NavigationService(new ServiceCollection().BuildServiceProvider());

        Assert.Throws<ArgumentNullException>(() => navigation.NavigateTo((ViewModelBase)null!));
    }
}

public class AboutViewModelTests
{
    [Fact]
    public void ReadsProductVersionAndAuthorFromAssembly()
    {
        var about = new AboutViewModel();

        Assert.Equal("Кур'єрська служба", about.ProductName);
        Assert.Matches(@"^\d+\.\d+\.\d+$", about.Version);
        Assert.False(string.IsNullOrWhiteSpace(about.Author));
        Assert.Contains("Інструментальні засоби візуального програмування", about.Purpose);
    }
}
