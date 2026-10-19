using System.ComponentModel;
using CourierService.App.Navigation;
using CourierService.App.Services;
using CourierService.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace CourierService.Tests.App;

public class MainViewModelTests
{
    private readonly FakeDialogService _dialogs = new();
    private readonly FakeAppLifetime _lifetime = new();
    private readonly MainViewModel _viewModel;

    public MainViewModelTests()
    {
        var provider = new ServiceCollection()
            .AddTransient<HomeViewModel>()
            .AddTransient<HelpViewModel>()
            .BuildServiceProvider();

        _viewModel = new MainViewModel(new NavigationService(provider), _dialogs, _lifetime);
    }

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
    public void OpenSection_ShowsPlaceholderForSection(AppSection section)
    {
        _viewModel.OpenSectionCommand.Execute(section);

        var page = Assert.IsType<PlaceholderViewModel>(_viewModel.CurrentPage);
        Assert.Equal(AppSectionCatalog.Get(section).Title, page.Title);
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

        Assert.Throws<ArgumentNullException>(() => navigation.NavigateTo(null!));
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
