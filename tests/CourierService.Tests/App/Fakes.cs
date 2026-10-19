using CourierService.App.Services;

namespace CourierService.Tests.App;

/// <summary>Заглушка діалогів: запам'ятовує виклики й повертає заданий результат підтвердження.</summary>
internal sealed class FakeDialogService : IDialogService
{
    public bool ConfirmResult { get; set; }
    public int ConfirmCalls { get; private set; }
    public int AboutCalls { get; private set; }
    public List<string> Errors { get; } = [];

    public void ShowInfo(string message) { }
    public void ShowWarning(string message) { }
    public void ShowError(string message) => Errors.Add(message);

    public bool Confirm(string message)
    {
        ConfirmCalls++;
        return ConfirmResult;
    }

    public void ShowAbout() => AboutCalls++;
}

/// <summary>Заглушка завершення роботи: лише рахує виклики.</summary>
internal sealed class FakeAppLifetime : IAppLifetime
{
    public int ShutdownCalls { get; private set; }

    public void Shutdown() => ShutdownCalls++;
}
