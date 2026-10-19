using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CourierService.Core.Abstractions;
using CourierService.Core.Enums;
using CourierService.Data;
using CourierService.Data.Seed;
using Microsoft.Extensions.Logging;

namespace CourierService.App.ViewModels;

/// <summary>
/// Демонстраційний обліковий запис для підказки у вікні входу.
/// </summary>
/// <param name="Login">Логін.</param>
/// <param name="Password">Пароль.</param>
/// <param name="Role">Назва ролі.</param>
public sealed record DemoAccountHint(string Login, string Password, string Role);

/// <summary>
/// Вікно входу: підготовка БД і перевірка логіна та пароля.
/// </summary>
public sealed partial class LoginViewModel : ViewModelBase
{
    private readonly IAuthService _auth;
    private readonly IDatabaseInitializer _database;
    private readonly ILogger<LoginViewModel> _logger;
    private string _password = string.Empty;

    /// <summary>
    /// Створює ViewModel вікна входу.
    /// </summary>
    /// <param name="auth">Сервіс входу.</param>
    /// <param name="database">Підготовка бази даних.</param>
    /// <param name="logger">Журнал.</param>
    public LoginViewModel(IAuthService auth, IDatabaseInitializer database, ILogger<LoginViewModel> logger)
    {
        _auth = auth;
        _database = database;
        _logger = logger;
    }

    /// <summary>Виникає після успішного входу (вікно закривається).</summary>
    public event EventHandler? SignedIn;

    /// <inheritdoc />
    public override string Title => "Вхід — Кур'єрська служба";

    /// <summary>Введений логін.</summary>
    [ObservableProperty]
    public partial string Login { get; set; } = string.Empty;

    /// <summary>
    /// Введений пароль. PasswordBox навмисно не підтримує прив'язку даних,
    /// тому вікно передає пароль сюди з обробника події.
    /// </summary>
    public string Password
    {
        set => _password = value ?? string.Empty;
    }

    /// <summary>Повідомлення про помилку для користувача.</summary>
    [ObservableProperty]
    public partial string? ErrorMessage { get; private set; }

    /// <summary>Стан підготовки (наприклад, «Підготовка бази даних…»).</summary>
    [ObservableProperty]
    public partial string? StatusMessage { get; private set; }

    /// <summary>Чи готова база даних до роботи.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SignInCommand))]
    public partial bool IsDatabaseReady { get; private set; }

    /// <summary>Демонстраційні облікові записи для перевірки програми.</summary>
    public IReadOnlyList<DemoAccountHint> DemoAccountHints { get; } =
        DemoAccounts.All
            .DistinctBy(account => account.Role)
            .Select(account => new DemoAccountHint(
                account.Role == UserRole.Courier ? "courier1…courier4" : account.Login,
                account.Password,
                account.Role.ToDisplayName()))
            .ToList();

    /// <summary>
    /// Готує базу даних (створює під час першого запуску). Викликається, коли вікно відкрилося.
    /// </summary>
    /// <returns>Задача.</returns>
    [RelayCommand]
    private async Task InitializeAsync()
    {
        StatusMessage = "Підготовка бази даних…";
        try
        {
            await _database.InitializeAsync();
            IsDatabaseReady = true;
            StatusMessage = null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Не вдалося підготувати базу даних");
            StatusMessage = null;
            ErrorMessage = "Не вдалося підготувати базу даних. Закрийте інші копії програми й спробуйте ще раз. "
                         + "Деталі записано в журнал.";
        }
    }

    [RelayCommand(CanExecute = nameof(IsDatabaseReady))]
    private async Task SignInAsync()
    {
        ErrorMessage = null;
        if (string.IsNullOrWhiteSpace(Login) || _password.Length == 0)
        {
            ErrorMessage = "Введіть логін і пароль.";
            return;
        }

        try
        {
            SignInResult result = await _auth.SignInAsync(Login, _password);
            if (result.Succeeded)
            {
                SignedIn?.Invoke(this, EventArgs.Empty);
            }
            else
            {
                ErrorMessage = result.ErrorMessage;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Помилка під час входу");
            ErrorMessage = "Не вдалося виконати вхід через помилку бази даних. Деталі записано в журнал.";
        }
    }
}
