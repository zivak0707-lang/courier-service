using CourierService.Core.Entities;
using CourierService.Core.Enums;
using CourierService.Core.Security;
using CourierService.Data.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace CourierService.Tests.Data;

public class AuthServiceTests : IAsyncLifetime
{
    private readonly Pbkdf2PasswordHasher _hasher = new();
    private readonly CurrentUser _currentUser = new();
    private SqliteTestDatabase _db = null!;
    private AuthService _auth = null!;

    public async Task InitializeAsync()
    {
        _db = await SqliteTestDatabase.CreateAsync();
        _auth = new AuthService(_db, _hasher, _currentUser, NullLogger<AuthService>.Instance);

        await using var context = _db.CreateDbContext();
        var zone = new Zone { Name = "Печерський", Bank = RiverBank.Right, CoefficientPercent = 100 };
        context.Users.AddRange(
            NewUser("operator", "operator123", UserRole.Operator),
            NewUser("blocked", "blocked123", UserRole.Operator, isActive: false),
            NewUser("noprofile", "courier123", UserRole.Courier));
        context.Couriers.Add(new Courier
        {
            User = NewUser("courier1", "courier123", UserRole.Courier),
            Phone = "+380500000010",
            TransportType = TransportType.Car,
            MaxWeightGrams = 30_000,
            HomeZone = zone,
        });
        context.Couriers.Add(new Courier
        {
            User = NewUser("courier2", "courier123", UserRole.Courier),
            Phone = "+380500000011",
            TransportType = TransportType.Foot,
            MaxWeightGrams = 5_000,
            HomeZone = zone,
            IsActive = false,
        });
        await context.SaveChangesAsync();
    }

    public async Task DisposeAsync() => await _db.DisposeAsync();

    [Fact]
    public async Task SignIn_ValidCredentials_StartsSession()
    {
        var result = await _auth.SignInAsync("operator", "operator123");

        Assert.True(result.Succeeded);
        Assert.Equal("operator", _currentUser.Session?.Login);
        Assert.Equal(UserRole.Operator, _currentUser.Session?.Role);
        Assert.Null(_currentUser.Session?.CourierId);
    }

    [Theory]
    [InlineData("OPERATOR")]
    [InlineData("  Operator  ")]
    public async Task SignIn_LoginIgnoresCaseAndSpaces(string login)
    {
        var result = await _auth.SignInAsync(login, "operator123");

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task SignIn_Courier_SessionContainsCourierId()
    {
        var result = await _auth.SignInAsync("courier1", "courier123");

        Assert.True(result.Succeeded);
        Assert.NotNull(_currentUser.Session?.CourierId);
    }

    [Theory]
    [InlineData("operator", "wrong")]
    [InlineData("unknown", "operator123")]
    public async Task SignIn_WrongLoginOrPassword_GivesSameMessage(string login, string password)
    {
        var result = await _auth.SignInAsync(login, password);

        Assert.False(result.Succeeded);
        Assert.Equal(AuthService.InvalidCredentialsMessage, result.ErrorMessage);
        Assert.False(_currentUser.IsAuthenticated);
    }

    [Theory]
    [InlineData("", "operator123")]
    [InlineData("   ", "operator123")]
    [InlineData("operator", "")]
    public async Task SignIn_EmptyFields_AsksToFillThem(string login, string password)
    {
        var result = await _auth.SignInAsync(login, password);

        Assert.False(result.Succeeded);
        Assert.Equal("Введіть логін і пароль.", result.ErrorMessage);
    }

    [Fact]
    public async Task SignIn_InactiveUser_IsRejected()
    {
        var result = await _auth.SignInAsync("blocked", "blocked123");

        Assert.False(result.Succeeded);
        Assert.Contains("вимкнено", result.ErrorMessage);
        Assert.False(_currentUser.IsAuthenticated);
    }

    [Theory]
    [InlineData("noprofile")]   // роль «Кур'єр» без профілю кур'єра
    [InlineData("courier2")]    // профіль кур'єра вимкнено
    public async Task SignIn_CourierWithoutActiveProfile_IsRejected(string login)
    {
        var result = await _auth.SignInAsync(login, "courier123");

        Assert.False(result.Succeeded);
        Assert.Contains("Профіль кур'єра", result.ErrorMessage);
    }

    private User NewUser(string login, string password, UserRole role, bool isActive = true)
    {
        var hash = _hasher.Hash(password);
        return new User
        {
            Login = login,
            PasswordHash = hash.Hash,
            PasswordSalt = hash.Salt,
            FullName = "Тестовий Користувач",
            Role = role,
            IsActive = isActive,
            CreatedAt = new DateTime(2026, 10, 19, 10, 0, 0),
        };
    }
}
