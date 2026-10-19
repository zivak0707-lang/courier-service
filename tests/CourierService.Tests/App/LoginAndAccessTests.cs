using System.IO;
using CourierService.App.Navigation;
using CourierService.App.ViewModels;
using CourierService.Core.Abstractions;
using CourierService.Core.Enums;
using Microsoft.Extensions.Logging.Abstractions;

namespace CourierService.Tests.App;

public class LoginViewModelTests
{
    private readonly FakeAuthService _auth = new();
    private readonly FakeDatabaseInitializer _database = new();
    private readonly LoginViewModel _viewModel;

    public LoginViewModelTests()
    {
        _viewModel = new LoginViewModel(_auth, _database, NullLogger<LoginViewModel>.Instance);
    }

    [Fact]
    public void SignIn_BeforeDatabaseIsReady_IsDisabled()
    {
        Assert.False(_viewModel.SignInCommand.CanExecute(null));
    }

    [Fact]
    public async Task Initialize_Success_EnablesSignIn()
    {
        await _viewModel.InitializeCommand.ExecuteAsync(null);

        Assert.True(_viewModel.IsDatabaseReady);
        Assert.True(_viewModel.SignInCommand.CanExecute(null));
        Assert.Null(_viewModel.StatusMessage);
    }

    [Fact]
    public async Task Initialize_Failure_ShowsErrorAndKeepsSignInDisabled()
    {
        _database.ThrowOnInitialize = new IOException("database is locked");

        await _viewModel.InitializeCommand.ExecuteAsync(null);

        Assert.False(_viewModel.SignInCommand.CanExecute(null));
        Assert.Contains("Не вдалося підготувати базу даних", _viewModel.ErrorMessage);
        Assert.DoesNotContain("database is locked", _viewModel.ErrorMessage);
    }

    [Theory]
    [InlineData("", "admin123")]
    [InlineData("   ", "admin123")]
    [InlineData("admin", "")]
    public async Task SignIn_EmptyFields_ShowsHintWithoutCallingService(string login, string password)
    {
        await _viewModel.InitializeCommand.ExecuteAsync(null);
        _viewModel.Login = login;
        _viewModel.Password = password;

        await _viewModel.SignInCommand.ExecuteAsync(null);

        Assert.Equal("Введіть логін і пароль.", _viewModel.ErrorMessage);
        Assert.Empty(_auth.Calls);
    }

    [Fact]
    public async Task SignIn_Success_RaisesSignedIn()
    {
        await _viewModel.InitializeCommand.ExecuteAsync(null);
        bool signedIn = false;
        _viewModel.SignedIn += (_, _) => signedIn = true;
        _viewModel.Login = "admin";
        _viewModel.Password = "admin123";

        await _viewModel.SignInCommand.ExecuteAsync(null);

        Assert.True(signedIn);
        Assert.Null(_viewModel.ErrorMessage);
        Assert.Equal(("admin", "admin123"), Assert.Single(_auth.Calls));
    }

    [Fact]
    public async Task SignIn_Failure_ShowsServiceMessage()
    {
        await _viewModel.InitializeCommand.ExecuteAsync(null);
        _auth.Result = SignInResult.Failure("Неправильний логін або пароль.");
        bool signedIn = false;
        _viewModel.SignedIn += (_, _) => signedIn = true;
        _viewModel.Login = "admin";
        _viewModel.Password = "wrong";

        await _viewModel.SignInCommand.ExecuteAsync(null);

        Assert.False(signedIn);
        Assert.Equal("Неправильний логін або пароль.", _viewModel.ErrorMessage);
    }

    [Fact]
    public async Task SignIn_ServiceThrows_ShowsFriendlyErrorInsteadOfCrashing()
    {
        await _viewModel.InitializeCommand.ExecuteAsync(null);
        _auth.ThrowOnSignIn = new InvalidOperationException("SQLite Error 5: 'database is locked'");
        _viewModel.Login = "admin";
        _viewModel.Password = "admin123";

        await _viewModel.SignInCommand.ExecuteAsync(null);

        Assert.Contains("Не вдалося виконати вхід", _viewModel.ErrorMessage);
        Assert.DoesNotContain("SQLite", _viewModel.ErrorMessage);
    }

    [Fact]
    public void DemoAccountHints_ContainOneLinePerRole()
    {
        Assert.Equal(3, _viewModel.DemoAccountHints.Count);
        Assert.Contains(_viewModel.DemoAccountHints, hint => hint.Login == "admin" && hint.Password == "admin123");
    }
}

public class SectionAccessTests
{
    [Theory]
    [InlineData(AppSection.NewOrder, false, true, false)]
    [InlineData(AppSection.OrderList, true, true, false)]
    [InlineData(AppSection.TrackingSearch, true, true, false)]
    [InlineData(AppSection.MyActiveDeliveries, false, false, true)]
    [InlineData(AppSection.MyCompletedDeliveries, false, false, true)]
    [InlineData(AppSection.Clients, true, true, false)]
    [InlineData(AppSection.Couriers, true, true, false)]
    [InlineData(AppSection.Zones, true, true, false)]
    [InlineData(AppSection.Tariffs, true, true, false)]
    [InlineData(AppSection.Dashboard, true, true, false)]
    [InlineData(AppSection.RevenueReport, true, true, false)]
    [InlineData(AppSection.UnpaidReport, true, true, false)]
    [InlineData(AppSection.CourierPerformanceReport, true, true, false)]
    [InlineData(AppSection.CourierPayoutReport, true, false, false)]
    [InlineData(AppSection.Users, true, false, false)]
    public void IsAllowed_MatchesAccessTableFromPlan(AppSection section, bool admin, bool @operator, bool courier)
    {
        Assert.Equal(admin, SectionAccess.IsAllowed(section, UserRole.Admin));
        Assert.Equal(@operator, SectionAccess.IsAllowed(section, UserRole.Operator));
        Assert.Equal(courier, SectionAccess.IsAllowed(section, UserRole.Courier));
    }

    [Fact]
    public void IsAllowed_WithoutSignIn_DeniesEverything()
    {
        Assert.All(Enum.GetValues<AppSection>(), section => Assert.False(SectionAccess.IsAllowed(section, null)));
    }

    [Fact]
    public void EverySection_BelongsToExactlyOneMenuGroup()
    {
        AppSection[] grouped =
        [
            .. SectionAccess.OrderSections, .. SectionAccess.MyDeliverySections, .. SectionAccess.DirectorySections,
            .. SectionAccess.ReportSections, .. SectionAccess.AdministrationSections,
        ];

        Assert.Equal(Enum.GetValues<AppSection>().Order(), grouped.Order());
    }
}
