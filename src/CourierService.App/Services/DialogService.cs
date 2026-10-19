using System.Windows;
using CourierService.App.ViewModels;
using CourierService.App.Views;

namespace CourierService.App.Services;

/// <summary>
/// Реалізація <see cref="IDialogService"/> через власне вікно повідомлень.
/// </summary>
/// <remarks>
/// Стандартний MessageBox підписує кнопки мовою Windows (на російськомовній системі — «Да»/«Нет»),
/// тому використано власне вікно з кнопками українською.
/// </remarks>
public sealed class DialogService : IDialogService
{
    /// <inheritdoc />
    public void ShowInfo(string message) => Show(message, MessageKind.Info);

    /// <inheritdoc />
    public void ShowWarning(string message) => Show(message, MessageKind.Warning);

    /// <inheritdoc />
    public void ShowError(string message) => Show(message, MessageKind.Error);

    /// <inheritdoc />
    public bool Confirm(string message) => Show(message, MessageKind.Question);

    /// <inheritdoc />
    public void ShowAbout()
    {
        var window = new AboutWindow(new AboutViewModel()) { Owner = FindOwner() };
        window.ShowDialog();
    }

    // Власник — активне вікно, щоб діалог не сховався за ним
    private static Window? FindOwner() =>
        Application.Current?.Windows
            .OfType<Window>()
            .FirstOrDefault(window => window.IsActive);

    private static bool Show(string message, MessageKind kind)
    {
        var window = new MessageWindow(message, kind);
        Window? owner = FindOwner();
        if (owner is not null)
        {
            window.Owner = owner;
        }
        else
        {
            window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        return window.ShowDialog() == true;
    }
}
