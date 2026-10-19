using CourierService.Core;
using CourierService.Core.Directories;
using CourierService.Core.Enums;
using CourierService.Core.Exceptions;
using CourierService.Core.Security;
using CourierService.Data.Seed;
using CourierService.Data.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace CourierService.Tests.Data;

/// <summary>Швидка заглушка хешування для тестів сервісів (PBKDF2 перевіряється окремо).</summary>
internal sealed class FakePasswordHasher : IPasswordHasher
{
    public PasswordHashResult Hash(string password) => new("hash:" + password, "salt");

    public bool Verify(string password, string hash, string salt) => hash == "hash:" + password;
}

/// <summary>
/// Спільна підготовка: БД у пам'яті з початковими даними й поточний користувач.
/// </summary>
public abstract class DirectoryServiceTestBase : IAsyncLifetime
{
    protected static readonly DateTime Now = new(2026, 10, 19, 12, 0, 0);

    private protected SqliteTestDatabase Db { get; private set; } = null!;
    protected CurrentUser CurrentUser { get; } = new();
    internal FakePasswordHasher Hasher { get; } = new();
    internal FixedClock Clock { get; } = new(Now);

    public async Task InitializeAsync()
    {
        Db = await SqliteTestDatabase.CreateAsync();
        await using var context = Db.CreateDbContext();
        await new DatabaseSeeder(Hasher, Clock).SeedAsync(context);
    }

    public async Task DisposeAsync() => await Db.DisposeAsync();

    /// <summary>Входить як демо-користувач указаної ролі.</summary>
    protected async Task SignInAsync(UserRole role)
    {
        await using var context = Db.CreateDbContext();
        var user = await context.Users.Include(u => u.Courier).FirstAsync(u => u.Role == role);
        CurrentUser.SignIn(new UserSession(user.Id, user.Login, user.FullName, role, user.Courier?.Id));
    }

    protected async Task<int> ZoneIdAsync(string name)
    {
        await using var context = Db.CreateDbContext();
        return await context.Zones.Where(z => z.Name == name).Select(z => z.Id).SingleAsync();
    }
}

public class ZoneAndTariffServiceTests : DirectoryServiceTestBase
{
    private ZoneService Zones => new(Db, CurrentUser, new ZoneValidator());
    private TariffService Tariffs => new(Db, CurrentUser, new TariffValidator());

    [Fact]
    public async Task GetAll_SortsZonesByUkrainianAlphabet()
    {
        await SignInAsync(UserRole.Operator);

        var names = (await Zones.GetAllAsync()).Select(z => z.Name).ToList();

        Assert.Equal(names.Order(SearchText.UkrainianComparer), names);
        Assert.Equal("Голосіївський", names[0]);
    }

    [Fact]
    public async Task Admin_CanEditZoneCoefficient()
    {
        await SignInAsync(UserRole.Admin);
        int id = await ZoneIdAsync("Деснянський");

        await Zones.SaveAsync(new ZoneInput(id, "Деснянський", RiverBank.Left, 130));

        Assert.Equal(130, (await Zones.GetAllAsync()).Single(z => z.Id == id).CoefficientPercent);
    }

    [Fact]
    public async Task Operator_CannotChangeZonesOrTariffs()
    {
        await SignInAsync(UserRole.Operator);

        await Assert.ThrowsAsync<AccessDeniedException>(() =>
            Zones.SaveAsync(new ZoneInput(null, "Бориспіль", RiverBank.Left, 150)));
        await Assert.ThrowsAsync<AccessDeniedException>(() => Tariffs.SetActiveAsync(1, false));
    }

    [Fact]
    public async Task Courier_CannotReadDirectories()
    {
        await SignInAsync(UserRole.Courier);

        await Assert.ThrowsAsync<AccessDeniedException>(() => Zones.GetAllAsync());
    }

    [Theory]
    [InlineData("Печерський")]
    [InlineData("  печерський ")]
    public async Task DuplicateZoneName_IsReportedOnNameField(string name)
    {
        await SignInAsync(UserRole.Admin);

        var exception = await Assert.ThrowsAsync<DomainValidationException>(() =>
            Zones.SaveAsync(new ZoneInput(null, name, RiverBank.Right, 100)));

        Assert.NotEmpty(exception.Errors[nameof(ZoneInput.Name)]);
    }

    [Fact]
    public async Task InvalidInput_IsRejectedBeforeAccessCheck()
    {
        // Навіть без входу некоректні дані відхиляються валідатором, а не БД
        var exception = await Assert.ThrowsAsync<DomainValidationException>(() =>
            Zones.SaveAsync(new ZoneInput(null, "", RiverBank.Right, 50)));

        Assert.Equal(2, exception.Errors.PropertyNames.Count);
    }

    [Fact]
    public async Task Admin_CanCreateTariff_AndItIsSortedByPrice()
    {
        await SignInAsync(UserRole.Admin);
        var input = new TariffInput(null, "Економ", Money.FromHryvnias(90m), 2_000, Money.FromHryvnias(10m), 20_000, 24,
            Money.FromHryvnias(25m), Money.FromHryvnias(500m), 5, Money.FromHryvnias(30m));

        int id = await Tariffs.SaveAsync(input);

        var tariffs = await Tariffs.GetAllAsync();
        Assert.Equal(id, tariffs[0].Id);
        Assert.Equal(4, tariffs.Count);
    }

    [Fact]
    public async Task SetActive_DeactivatesAndReactivatesTariff()
    {
        await SignInAsync(UserRole.Admin);
        int id = (await Tariffs.GetAllAsync()).First().Id;

        await Tariffs.SetActiveAsync(id, false);
        Assert.False((await Tariffs.GetAllAsync()).Single(t => t.Id == id).IsActive);

        await Tariffs.SetActiveAsync(id, true);
        Assert.True((await Tariffs.GetAllAsync()).Single(t => t.Id == id).IsActive);
    }

    [Fact]
    public async Task SetActive_UnknownId_ThrowsNotFound()
    {
        await SignInAsync(UserRole.Admin);

        await Assert.ThrowsAsync<EntityNotFoundException>(() => Zones.SetActiveAsync(9999, false));
    }
}

public class ClientServiceTests : DirectoryServiceTestBase
{
    private ClientService Clients => new(Db, CurrentUser, new ClientValidator(), Clock);

    private static ClientInput NewClient(string phone = "063 111 22 33") =>
        new(null, ClientType.Individual, "Олег Петренко", phone, "  ", " вул. Вишнева, 7 ", "");

    [Fact]
    public async Task Operator_CreatesClient_WithNormalizedPhoneAndCleanedFields()
    {
        await SignInAsync(UserRole.Operator);

        int id = await Clients.SaveAsync(NewClient("(063) 111-22-33"));

        var client = (await Clients.GetAllAsync()).Single(c => c.Id == id);
        Assert.Equal("+380631112233", client.Phone);
        Assert.Equal("вул. Вишнева, 7", client.Address);
        Assert.Null(client.Email);
        Assert.Null(client.Notes);
        Assert.Equal(Now, client.CreatedAt);
    }

    [Fact]
    public async Task DuplicatePhoneInAnotherFormat_IsReportedWithOwnerName()
    {
        await SignInAsync(UserRole.Operator);

        // +380930000204 належить демо-клієнтці Наталії Руденко
        var exception = await Assert.ThrowsAsync<DomainValidationException>(() =>
            Clients.SaveAsync(NewClient("093 000 02 04")));

        Assert.Contains("Наталія Руденко", exception.Errors[nameof(ClientInput.Phone)].Single());
    }

    [Fact]
    public async Task EditingClient_KeepsOwnPhoneWithoutDuplicateError()
    {
        await SignInAsync(UserRole.Operator);
        int id = await Clients.SaveAsync(NewClient());

        await Clients.SaveAsync(NewClient() with { Id = id, Name = "Олег Петренко-Іваненко" });

        Assert.Equal("Олег Петренко-Іваненко", (await Clients.GetAllAsync()).Single(c => c.Id == id).Name);
    }

    [Fact]
    public async Task Admin_CanViewButNotChangeClients()
    {
        await SignInAsync(UserRole.Admin);

        Assert.Equal(6, (await Clients.GetAllAsync()).Count);
        await Assert.ThrowsAsync<AccessDeniedException>(() => Clients.SaveAsync(NewClient()));
    }
}

public class CourierServiceTests : DirectoryServiceTestBase
{
    private CourierDirectoryService Couriers => new(Db, CurrentUser, new CourierValidator(), Hasher, Clock);

    private async Task<CourierInput> NewCourierAsync(string login = "courier5") =>
        new(null, "Ірина Гончар", login, "courier555", "066 000 01 05", TransportType.Bicycle, 10_000,
            await ZoneIdAsync("Солом'янський"), 45);

    [Fact]
    public async Task GetAll_ReturnsNameLoginAndZoneFromRelatedTables()
    {
        await SignInAsync(UserRole.Operator);

        var courier = (await Couriers.GetAllAsync()).Single(c => c.Login == "courier1");

        Assert.Equal("Андрій Ткаченко", courier.FullName);
        Assert.Equal("Шевченківський", courier.HomeZoneName);
    }

    [Fact]
    public async Task NewCourier_GetsAccountAndCanSignIn()
    {
        await SignInAsync(UserRole.Operator);

        int id = await Couriers.SaveAsync(await NewCourierAsync());

        var auth = new AuthService(Db, Hasher, new CurrentUser(), NullLogger<AuthService>.Instance);
        var result = await auth.SignInAsync("courier5", "courier555");
        Assert.True(result.Succeeded);

        await using var context = Db.CreateDbContext();
        var user = await context.Users.SingleAsync(u => u.Login == "courier5");
        Assert.Equal(UserRole.Courier, user.Role);
        Assert.Equal(id, (await context.Couriers.SingleAsync(c => c.UserId == user.Id)).Id);
    }

    [Fact]
    public async Task EditWithoutPassword_KeepsOldPassword()
    {
        await SignInAsync(UserRole.Operator);
        int id = await Couriers.SaveAsync(await NewCourierAsync());

        await Couriers.SaveAsync(await NewCourierAsync() with { Id = id, Password = null, PayoutPercent = 55 });

        var auth = new AuthService(Db, Hasher, new CurrentUser(), NullLogger<AuthService>.Instance);
        Assert.True((await auth.SignInAsync("courier5", "courier555")).Succeeded);
        Assert.Equal(55, (await Couriers.GetAllAsync()).Single(c => c.Id == id).PayoutPercent);
    }

    [Theory]
    [InlineData("courier1")]
    [InlineData("ADMIN")]
    public async Task TakenLogin_IsReportedOnLoginField(string login)
    {
        await SignInAsync(UserRole.Operator);

        var exception = await Assert.ThrowsAsync<DomainValidationException>(async () =>
            await Couriers.SaveAsync(await NewCourierAsync(login)));

        Assert.NotEmpty(exception.Errors[nameof(CourierInput.Login)]);
    }

    [Fact]
    public async Task InactiveZone_CannotBeHomeZone()
    {
        await SignInAsync(UserRole.Admin);
        int zoneId = await ZoneIdAsync("Солом'янський");
        await new ZoneService(Db, CurrentUser, new ZoneValidator()).SetActiveAsync(zoneId, false);
        await SignInAsync(UserRole.Operator);

        var exception = await Assert.ThrowsAsync<DomainValidationException>(async () =>
            await Couriers.SaveAsync(await NewCourierAsync()));

        Assert.NotEmpty(exception.Errors[nameof(CourierInput.HomeZoneId)]);
    }

    [Fact]
    public async Task DeactivatedCourier_CannotSignIn()
    {
        await SignInAsync(UserRole.Operator);
        int id = (await Couriers.GetAllAsync()).Single(c => c.Login == "courier2").Id;

        await Couriers.SetActiveAsync(id, false);

        var auth = new AuthService(Db, Hasher, new CurrentUser(), NullLogger<AuthService>.Instance);
        Assert.False((await auth.SignInAsync("courier2", "courier123")).Succeeded);
    }

    [Fact]
    public async Task RealPasswordHasher_NewCourierCanSignIn()
    {
        await SignInAsync(UserRole.Operator);
        var hasher = new Pbkdf2PasswordHasher();
        var service = new CourierDirectoryService(Db, CurrentUser, new CourierValidator(), hasher, Clock);

        await service.SaveAsync(await NewCourierAsync("courier6"));

        var auth = new AuthService(Db, hasher, new CurrentUser(), NullLogger<AuthService>.Instance);
        Assert.True((await auth.SignInAsync("courier6", "courier555")).Succeeded);
    }
}

public class UserServiceTests : DirectoryServiceTestBase
{
    private UserService Users => new(Db, CurrentUser, new UserValidator(), Hasher, Clock);

    private async Task<int> UserIdAsync(string login)
    {
        await using var context = Db.CreateDbContext();
        return await context.Users.Where(u => u.Login == login).Select(u => u.Id).SingleAsync();
    }

    [Fact]
    public async Task OnlyAdmin_CanManageUsers()
    {
        await SignInAsync(UserRole.Operator);

        await Assert.ThrowsAsync<AccessDeniedException>(() => Users.GetAllAsync());
    }

    [Fact]
    public async Task GetAll_ListsAdminsFirstThenOperatorsThenCouriers()
    {
        await SignInAsync(UserRole.Admin);

        var roles = (await Users.GetAllAsync()).Select(u => u.Role).ToList();

        Assert.Equal(roles.Order(), roles);
        Assert.Equal(6, roles.Count);
    }

    [Fact]
    public async Task Admin_CreatesOperator_WhoCanSignIn()
    {
        await SignInAsync(UserRole.Admin);

        await Users.SaveAsync(new UserInput(null, "Світлана Коваль", "s.koval", "operator456", UserRole.Operator));

        var auth = new AuthService(Db, Hasher, new CurrentUser(), NullLogger<AuthService>.Instance);
        Assert.True((await auth.SignInAsync("s.koval", "operator456")).Succeeded);
    }

    [Fact]
    public async Task CourierRole_CannotBeCreatedHere()
    {
        await SignInAsync(UserRole.Admin);

        var exception = await Assert.ThrowsAsync<DomainValidationException>(() =>
            Users.SaveAsync(new UserInput(null, "Новий Кур'єр", "courier7", "courier777", UserRole.Courier)));

        Assert.Contains("довіднику «Кур'єри»", exception.Errors[nameof(UserInput.Role)].Single());
    }

    [Fact]
    public async Task CourierAccount_RoleCannotBeChanged()
    {
        await SignInAsync(UserRole.Admin);
        int id = await UserIdAsync("courier1");

        await Assert.ThrowsAsync<DomainValidationException>(() =>
            Users.SaveAsync(new UserInput(id, "Андрій Ткаченко", "courier1", null, UserRole.Operator)));
    }

    [Fact]
    public async Task Admin_CannotDeactivateSelfOrChangeOwnRole()
    {
        await SignInAsync(UserRole.Admin);
        int ownId = CurrentUser.Session!.UserId;

        await Assert.ThrowsAsync<DomainValidationException>(() => Users.SetActiveAsync(ownId, false));
        await Assert.ThrowsAsync<DomainValidationException>(() =>
            Users.SaveAsync(new UserInput(ownId, "Олена Марченко", "admin", null, UserRole.Operator)));
    }

    [Fact]
    public async Task LastActiveAdmin_CannotBeDeactivatedOrDemoted()
    {
        // Другий адміністратор знімає з першого (єдиного іншого) роль — це заборонено
        await SignInAsync(UserRole.Admin);
        int firstAdmin = CurrentUser.Session!.UserId;
        int secondAdmin = await Users.SaveAsync(new UserInput(null, "Другий Адмін", "admin2", "admin2pass", UserRole.Admin));
        await Users.SetActiveAsync(secondAdmin, false);
        CurrentUser.SignIn(new UserSession(secondAdmin, "admin2", "Другий Адмін", UserRole.Admin, null));

        await Assert.ThrowsAsync<DomainValidationException>(() => Users.SetActiveAsync(firstAdmin, false));
        await Assert.ThrowsAsync<DomainValidationException>(() =>
            Users.SaveAsync(new UserInput(firstAdmin, "Олена Марченко", "admin", null, UserRole.Operator)));
    }

    [Fact]
    public async Task ResetPassword_ChangesPasswordOnlyWhenProvided()
    {
        await SignInAsync(UserRole.Admin);
        int operatorId = await UserIdAsync("operator");

        await Users.SaveAsync(new UserInput(operatorId, "Ігор Бондар", "operator", "newpass99", UserRole.Operator));

        var auth = new AuthService(Db, Hasher, new CurrentUser(), NullLogger<AuthService>.Instance);
        Assert.False((await auth.SignInAsync("operator", "operator123")).Succeeded);
        Assert.True((await auth.SignInAsync("operator", "newpass99")).Succeeded);
    }

    [Fact]
    public async Task DeactivatedOperator_CannotSignIn()
    {
        await SignInAsync(UserRole.Admin);

        await Users.SetActiveAsync(await UserIdAsync("operator"), false);

        var auth = new AuthService(Db, Hasher, new CurrentUser(), NullLogger<AuthService>.Instance);
        Assert.False((await auth.SignInAsync("operator", "operator123")).Succeeded);
    }
}
