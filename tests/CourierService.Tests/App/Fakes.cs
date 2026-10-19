using CourierService.App.Services;
using CourierService.App.ViewModels;
using CourierService.Core.Abstractions;
using CourierService.Core.Security;
using CourierService.Data;

namespace CourierService.Tests.App;

/// <summary>Заглушка діалогів: запам'ятовує виклики й повертає заданий результат підтвердження.</summary>
internal sealed class FakeDialogService : IDialogService
{
    public bool ConfirmResult { get; set; }
    public Queue<bool> ConfirmAnswers { get; } = new();
    public int ConfirmCalls { get; private set; }
    public int AboutCalls { get; private set; }
    public List<string> Errors { get; } = [];
    public List<string> Warnings { get; } = [];
    public List<string> Infos { get; } = [];
    public List<string> Questions { get; } = [];

    public void ShowInfo(string message) => Infos.Add(message);
    public void ShowWarning(string message) => Warnings.Add(message);
    public void ShowError(string message) => Errors.Add(message);

    public bool Confirm(string message)
    {
        ConfirmCalls++;
        Questions.Add(message);
        return ConfirmAnswers.Count > 0 ? ConfirmAnswers.Dequeue() : ConfirmResult;
    }

    public void ShowAbout() => AboutCalls++;
}

/// <summary>Заглушка завершення роботи: лише рахує виклики.</summary>
internal sealed class FakeAppLifetime : IAppLifetime
{
    public int ShutdownCalls { get; private set; }
    public int RestartCalls { get; private set; }
    public bool IsRestarting => RestartCalls > 0;

    public void Shutdown() => ShutdownCalls++;
    public void Restart() => RestartCalls++;
}

/// <summary>Заглушка вікна входу: імітує вхід заданого користувача або скасування.</summary>
internal sealed class FakeLoginDialog(ICurrentUser currentUser) : ILoginDialog
{
    public UserSession? NextUser { get; set; }
    public int Calls { get; private set; }

    public bool ShowLogin()
    {
        Calls++;
        if (NextUser is null)
        {
            return false;
        }

        currentUser.SignIn(NextUser);
        return true;
    }
}

/// <summary>Заглушка сервісу входу: повертає заданий результат або кидає виняток.</summary>
internal sealed class FakeAuthService : IAuthService
{
    public SignInResult Result { get; set; } = SignInResult.Success;
    public Exception? ThrowOnSignIn { get; set; }
    public List<(string Login, string Password)> Calls { get; } = [];

    public Task<SignInResult> SignInAsync(string login, string password, CancellationToken cancellationToken = default)
    {
        Calls.Add((login, password));
        return ThrowOnSignIn is null ? Task.FromResult(Result) : Task.FromException<SignInResult>(ThrowOnSignIn);
    }
}

/// <summary>Навігація, що лише запам'ятовує запити (сторінки не створюються).</summary>
internal sealed class RecordingNavigationService : INavigationService
{
    public ViewModelBase? CurrentViewModel { get; private set; }
    public List<Type> RequestedTypes { get; } = [];

    public event EventHandler? CurrentViewModelChanged;

    public void NavigateTo<TViewModel>() where TViewModel : ViewModelBase => RequestedTypes.Add(typeof(TViewModel));

    public void NavigateTo(Type viewModelType) => RequestedTypes.Add(viewModelType);

    public void NavigateTo(ViewModelBase viewModel)
    {
        CurrentViewModel = viewModel;
        CurrentViewModelChanged?.Invoke(this, EventArgs.Empty);
    }
}

/// <summary>Заглушка підготовки БД.</summary>
internal sealed class FakeDatabaseInitializer : IDatabaseInitializer
{
    public Exception? ThrowOnInitialize { get; set; }

    public Task InitializeAsync(CancellationToken cancellationToken = default) =>
        ThrowOnInitialize is null ? Task.CompletedTask : Task.FromException(ThrowOnInitialize);
}

/// <summary>Заглушка форми нового замовлення: лише рахує відкриття.</summary>
internal sealed class FakeOrderFormLauncher : CourierService.App.ViewModels.Orders.IOrderFormLauncher
{
    public int Calls { get; private set; }

    public Task<CourierService.Core.Orders.OrderCreated?> OpenNewOrderAsync()
    {
        Calls++;
        return Task.FromResult<CourierService.Core.Orders.OrderCreated?>(null);
    }
}

/// <summary>Заглушка картки замовлення: запам'ятовує відкриті замовлення й повертає заданий результат.</summary>
internal sealed class FakeOrderDetailsDialog : IOrderDetailsDialog
{
    public bool Result { get; set; }
    public List<int> Opened { get; } = [];

    public bool ShowOrder(int orderId)
    {
        Opened.Add(orderId);
        return Result;
    }
}
