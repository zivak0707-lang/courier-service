using CourierService.App.ViewModels.Directories;
using CourierService.Core.Directories;
using CourierService.Core.Enums;
using CourierService.Core.Security;

namespace CourierService.Tests.App;

internal sealed class FakeUserService : FakeDirectoryService<UserListItem, UserInput>, IUserService;

public class UsersViewModelTests
{
    private static readonly DateTime Created = new(2026, 10, 19, 9, 0, 0);

    private readonly FakeUserService _service = new()
    {
        Items =
        [
            new UserListItem(1, "admin", "Сергій Зівак", UserRole.Admin, true, Created),
            new UserListItem(2, "operator", "Ігор Бондар", UserRole.Operator, true, Created),
            new UserListItem(3, "courier1", "Андрій Ткаченко", UserRole.Courier, true, Created),
        ],
    };

    private readonly FakeEditDialogService _editDialogs = new();

    private async Task<UsersViewModel> CreateLoadedListAsync()
    {
        var currentUser = new CurrentUser();
        currentUser.SignIn(new UserSession(1, "admin", "Сергій Зівак", UserRole.Admin, null));
        var list = new UsersViewModel(_service, new UserValidator(), new FakeDialogService(), _editDialogs, currentUser);
        await list.LoadCommand.ExecuteAsync(null);
        return list;
    }

    private async Task<UserEditorViewModel> OpenEditorAsync(string login)
    {
        var list = await CreateLoadedListAsync();
        list.SelectedItem = list.Items.Single(u => u.Login == login);
        await list.EditCommand.ExecuteAsync(null);
        return Assert.IsType<UserEditorViewModel>(_editDialogs.Editors.Single());
    }

    [Fact]
    public async Task Search_FindsUsersByRoleName()
    {
        var list = await CreateLoadedListAsync();

        list.SearchText = "кур'єр";

        Assert.Equal("courier1", Assert.Single(list.Items).Login);
    }

    [Fact]
    public async Task OwnAccount_RoleIsLocked()
    {
        var editor = await OpenEditorAsync("admin");

        Assert.False(editor.IsRoleEditable);
        Assert.Equal("Власну роль змінити не можна.", editor.RoleHint);
    }

    [Fact]
    public async Task CourierAccount_RoleIsLockedAndOnlyCourierOffered()
    {
        var editor = await OpenEditorAsync("courier1");

        Assert.False(editor.IsRoleEditable);
        Assert.Equal([UserRole.Courier], editor.Roles.Select(r => r.Value));
    }

    [Fact]
    public async Task OtherOperator_RoleCanBeChanged()
    {
        var editor = await OpenEditorAsync("operator");

        Assert.True(editor.IsRoleEditable);
        Assert.Equal([UserRole.Admin, UserRole.Operator], editor.Roles.Select(r => r.Value));
    }

    [Fact]
    public async Task NewUser_RequiresPassword()
    {
        var editor = new UserEditorViewModel(_service, new UserValidator(), null, isOwnAccount: false)
        {
            FullName = "Світлана Коваль", Login = "s.koval",
        };

        await editor.SaveCommand.ExecuteAsync(null);

        Assert.Equal("Введіть пароль.", editor.PasswordError);
        Assert.Empty(_service.Saved);
    }

    [Fact]
    public async Task ExistingUser_PasswordReset_IsSaved()
    {
        var editor = await OpenEditorAsync("operator");
        editor.Password = "newpass99";

        await editor.SaveCommand.ExecuteAsync(null);

        Assert.Equal(new UserInput(2, "Ігор Бондар", "operator", "newpass99", UserRole.Operator), Assert.Single(_service.Saved));
    }

    [Fact]
    public async Task WeakPassword_ShowsPolicy()
    {
        var editor = await OpenEditorAsync("operator");
        editor.Password = "123";

        await editor.SaveCommand.ExecuteAsync(null);

        Assert.Contains("щонайменше одна літера й одна цифра", editor.PasswordError);
    }
}
