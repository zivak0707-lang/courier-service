using CourierService.App.ViewModels.Directories;
using CourierService.Core.Directories;
using CourierService.Core.Entities;
using CourierService.Core.Enums;
using CourierService.Core.Security;

namespace CourierService.Tests.App;

internal sealed class FakeClientService : FakeDirectoryService<Client, ClientInput>, IClientService;

internal sealed class FakeCourierService : FakeDirectoryService<CourierListItem, CourierInput>, ICourierService;

public class ClientsViewModelTests
{
    private readonly FakeClientService _service = new()
    {
        Items =
        [
            new Client { Id = 1, Name = "ТОВ «Квітковий двір»", Phone = "+380670000201", Address = "вул. Каштанова, 12", ClientType = ClientType.Company },
            new Client { Id = 2, Name = "Наталія Руденко", Phone = "+380930000204", Address = "вул. Яблунева, 3", ClientType = ClientType.Individual },
        ],
    };

    private static readonly IReadOnlyList<Option<int>> Zones = [new(1, "Шевченківський"), new(2, "Печерський")];

    private ClientsViewModel CreateList(UserRole role)
    {
        var currentUser = new CurrentUser();
        currentUser.SignIn(new UserSession(1, "user", "Користувач", role, null));
        return new ClientsViewModel(_service, new ClientValidator(), new FakeZoneService(), new FakeDialogService(), new FakeEditDialogService(), currentUser);
    }

    [Theory]
    [InlineData(UserRole.Operator, true)]
    [InlineData(UserRole.Admin, false)]
    public void CanEdit_OnlyForOperator(UserRole role, bool expected)
    {
        Assert.Equal(expected, CreateList(role).CanEdit);
    }

    [Theory]
    [InlineData("093 000 02 04")]    // так, як номер показано в таблиці
    [InlineData("0930000204")]
    [InlineData("руденко")]
    public async Task Search_FindsClientByPhoneInAnyFormatOrName(string search)
    {
        var list = CreateList(UserRole.Operator);
        await list.LoadCommand.ExecuteAsync(null);

        list.SearchText = search;

        Assert.Equal(2, Assert.Single(list.Items).Id);
    }

    [Fact]
    public void Editor_ShowsPhoneInReadableFormat()
    {
        var editor = new ClientEditorViewModel(_service, new ClientValidator(), Zones, _service.Items[0]);

        Assert.Equal("+380 (67) 000-02-01", editor.Phone);
        Assert.Equal(ClientType.Company, editor.ClientType);
    }

    [Fact]
    public async Task Editor_InvalidPhoneAndEmail_ShowErrorsNextToFields()
    {
        var editor = new ClientEditorViewModel(_service, new ClientValidator(), Zones, null)
        {
            Name = "Олег Петренко", Phone = "12345", Email = "oleg@", Address = "вул. Вишнева, 7",
        };

        await editor.SaveCommand.ExecuteAsync(null);

        Assert.NotEmpty(editor.GetErrors(nameof(ClientEditorViewModel.Phone)).Cast<string>());
        Assert.NotEmpty(editor.GetErrors(nameof(ClientEditorViewModel.Email)).Cast<string>());
        Assert.Empty(_service.Saved);
    }

    [Fact]
    public async Task Editor_ValidClient_IsSaved()
    {
        var editor = new ClientEditorViewModel(_service, new ClientValidator(), Zones, null)
        {
            Name = "Олег Петренко", Phone = "063 111 22 33", Address = "вул. Вишнева, 7", DefaultZoneId = 1,
        };

        await editor.SaveCommand.ExecuteAsync(null);

        Assert.Equal("063 111 22 33", Assert.Single(_service.Saved).Phone);
        Assert.Equal(1, _service.Saved[0].DefaultZoneId);
    }

    [Fact]
    public async Task Editor_WithoutZone_ShowsErrorNextToZone()
    {
        var editor = new ClientEditorViewModel(_service, new ClientValidator(), Zones, null)
        {
            Name = "Олег Петренко", Phone = "063 111 22 33", Address = "вул. Вишнева, 7",
        };

        await editor.SaveCommand.ExecuteAsync(null);

        Assert.NotEmpty(editor.GetErrors(nameof(ClientEditorViewModel.DefaultZoneId)).Cast<string>());
        Assert.Empty(_service.Saved);
    }
}

public class CouriersViewModelTests
{
    private readonly FakeCourierService _service = new()
    {
        Items =
        [
            new CourierListItem(1, 10, "Андрій Ткаченко", "courier1", "+380500000101", TransportType.Car, 30_000, 1, "Шевченківський", 50, true),
            new CourierListItem(2, 11, "Марія Лисенко", "courier2", "+380500000102", TransportType.Bicycle, 10_000, 2, "Оболонський", 50, true),
        ],
    };

    private readonly FakeZoneService _zones = new()
    {
        Items =
        [
            new Zone { Id = 1, Name = "Шевченківський", IsActive = true, CoefficientPercent = 100 },
            new Zone { Id = 2, Name = "Оболонський", IsActive = false, CoefficientPercent = 115 },
            new Zone { Id = 3, Name = "Дарницький", IsActive = true, CoefficientPercent = 120 },
        ],
    };

    private readonly FakeEditDialogService _editDialogs = new();

    private async Task<CouriersViewModel> CreateLoadedListAsync()
    {
        var currentUser = new CurrentUser();
        currentUser.SignIn(new UserSession(2, "operator", "Ігор Бондар", UserRole.Operator, null));
        var list = new CouriersViewModel(_service, _zones, new CourierValidator(), new FakeDialogService(), _editDialogs, currentUser);
        await list.LoadCommand.ExecuteAsync(null);
        return list;
    }

    [Fact]
    public async Task NewCourierForm_OffersOnlyActiveZones()
    {
        var list = await CreateLoadedListAsync();

        await list.AddCommand.ExecuteAsync(null);

        var editor = Assert.IsType<CourierEditorViewModel>(_editDialogs.Editors.Single());
        Assert.Equal(["Шевченківський", "Дарницький"], editor.Zones.Select(z => z.Display));
    }

    [Fact]
    public async Task EditForm_KeepsCurrentInactiveZoneVisible()
    {
        var list = await CreateLoadedListAsync();
        list.SelectedItem = list.Items.Single(c => c.Login == "courier2");

        await list.EditCommand.ExecuteAsync(null);

        var editor = Assert.IsType<CourierEditorViewModel>(_editDialogs.Editors.Single());
        Assert.Contains(editor.Zones, z => z.Value == 2 && z.Display == "Оболонський (неактивна)");
        Assert.Equal(2, editor.HomeZoneId);
    }

    [Fact]
    public async Task Search_FindsCourierByLogin()
    {
        var list = await CreateLoadedListAsync();

        list.SearchText = "courier2";

        Assert.Equal("Марія Лисенко", Assert.Single(list.Items).FullName);
    }

    [Fact]
    public async Task NewCourier_WithoutPassword_ShowsPasswordError()
    {
        var editor = new CourierEditorViewModel(_service, new CourierValidator(), [new Option<int>(1, "Шевченківський")], null)
        {
            FullName = "Ірина Гончар", Login = "courier5", Phone = "066 000 01 05", HomeZoneId = 1,
        };

        await editor.SaveCommand.ExecuteAsync(null);

        Assert.Equal("Введіть пароль.", editor.PasswordError);
        Assert.Empty(_service.Saved);
    }

    [Fact]
    public async Task ExistingCourier_SavesWithoutPasswordChange()
    {
        var editor = new CourierEditorViewModel(_service, new CourierValidator(), [new Option<int>(1, "Шевченківський")], _service.Items[0])
        {
            PayoutPercent = "55",
        };

        await editor.SaveCommand.ExecuteAsync(null);

        CourierInput saved = Assert.Single(_service.Saved);
        Assert.Equal(1, saved.Id);
        Assert.Equal("", saved.Password);
        Assert.Equal(55, saved.PayoutPercent);
        Assert.Contains("залиште порожнім", editor.PasswordLabel);
    }

    [Fact]
    public async Task NotSelectedZone_ShowsErrorNextToZoneField()
    {
        var editor = new CourierEditorViewModel(_service, new CourierValidator(), [], null)
        {
            FullName = "Ірина Гончар", Login = "courier5", Password = "courier555", Phone = "066 000 01 05",
        };

        await editor.SaveCommand.ExecuteAsync(null);

        Assert.Equal(["Оберіть домашню зону."], editor.GetErrors(nameof(CourierEditorViewModel.HomeZoneId)).Cast<string>());
    }
}
