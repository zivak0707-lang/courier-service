using CourierService.Core.Enums;
using CourierService.Core.Exceptions;
using CourierService.Core.Security;

namespace CourierService.Tests.Core;

public class Pbkdf2PasswordHasherTests
{
    private readonly Pbkdf2PasswordHasher _hasher = new();

    [Fact]
    public void Verify_CorrectPassword_ReturnsTrue()
    {
        var stored = _hasher.Hash("Kurier#2026");

        Assert.True(_hasher.Verify("Kurier#2026", stored.Hash, stored.Salt));
    }

    [Theory]
    [InlineData("kurier#2026")]   // інший регістр
    [InlineData("Kurier#2025")]
    [InlineData("")]
    public void Verify_WrongPassword_ReturnsFalse(string attempt)
    {
        var stored = _hasher.Hash("Kurier#2026");

        Assert.False(_hasher.Verify(attempt, stored.Hash, stored.Salt));
    }

    [Fact]
    public void Hash_SamePasswordTwice_GivesDifferentHashesBecauseOfSalt()
    {
        var first = _hasher.Hash("operator123");
        var second = _hasher.Hash("operator123");

        Assert.NotEqual(first.Salt, second.Salt);
        Assert.NotEqual(first.Hash, second.Hash);
    }

    [Fact]
    public void Hash_DoesNotContainPasswordItself()
    {
        var stored = _hasher.Hash("operator123");

        Assert.DoesNotContain("operator123", stored.Hash);
    }

    [Fact]
    public void Verify_CorruptedStoredValues_ReturnsFalseInsteadOfCrashing()
    {
        Assert.False(_hasher.Verify("operator123", "не base64!", "також ні"));
    }

    [Fact]
    public void Hash_EmptyPassword_Throws()
    {
        Assert.Throws<ArgumentException>(() => _hasher.Hash(""));
    }
}

public class CurrentUserTests
{
    private static readonly UserSession Operator = new(2, "operator", "Ігор Бондар", UserRole.Operator, null);

    [Fact]
    public void SignInAndSignOut_ChangeStateAndRaiseEvent()
    {
        var currentUser = new CurrentUser();
        int changes = 0;
        currentUser.Changed += (_, _) => changes++;

        currentUser.SignIn(Operator);
        Assert.True(currentUser.IsAuthenticated);
        Assert.Equal(Operator, currentUser.Session);

        currentUser.SignOut();
        Assert.False(currentUser.IsAuthenticated);
        Assert.Null(currentUser.Session);

        Assert.Equal(2, changes);
    }

    [Fact]
    public void RequireRole_AllowedRole_ReturnsSession()
    {
        var currentUser = new CurrentUser();
        currentUser.SignIn(Operator);

        Assert.Equal(Operator, currentUser.RequireRole(UserRole.Admin, UserRole.Operator));
    }

    [Fact]
    public void RequireRole_ForbiddenRole_ThrowsAccessDenied()
    {
        var currentUser = new CurrentUser();
        currentUser.SignIn(Operator);

        var exception = Assert.Throws<AccessDeniedException>(() => currentUser.RequireRole(UserRole.Admin));
        Assert.Contains("Оператор", exception.Message);
    }

    [Fact]
    public void RequireRole_NotSignedIn_ThrowsAccessDenied()
    {
        Assert.Throws<AccessDeniedException>(() => new CurrentUser().RequireRole(UserRole.Admin));
    }
}

public class CurrentUserSessionUpdateTests
{
    [Fact]
    public void UpdateSession_ReplacesDataOfSameUser()
    {
        var currentUser = new CurrentUser();
        currentUser.SignIn(new UserSession(1, "admin", "Сергій Зівак", UserRole.Admin, null));

        currentUser.UpdateSession(new UserSession(1, "admin", "Олена Шевчук", UserRole.Admin, null));

        Assert.Equal("Олена Шевчук", currentUser.Session!.FullName);
    }

    [Fact]
    public void UpdateSession_RejectsOtherUserOrNoSession()
    {
        var currentUser = new CurrentUser();
        var other = new UserSession(2, "operator", "Ігор Бондар", UserRole.Operator, null);

        Assert.Throws<InvalidOperationException>(() => currentUser.UpdateSession(other));
        currentUser.SignIn(new UserSession(1, "admin", "Сергій Зівак", UserRole.Admin, null));
        Assert.Throws<InvalidOperationException>(() => currentUser.UpdateSession(other));
    }
}
