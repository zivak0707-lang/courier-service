using System.Windows;
using CourierService.App.ViewModels;
using CourierService.App.Views;

namespace CourierService.App.Services;

/// <summary>
/// Реалізація <see cref="IDialogService"/> через стандартні вікна повідомлень WPF.
/// </summary>
public sealed class DialogService : IDialogService
{
    /// <summary>Заголовок усіх вікон повідомлень.</summary>
    public const string Caption = "Кур'єрська служба";

    /// <inheritdoc />
    public void ShowInfo(string message) =>
        Show(message, MessageBoxButton.OK, MessageBoxImage.Information);

    /// <inheritdoc />
    public void ShowWarning(string message) =>
        Show(message, MessageBoxButton.OK, MessageBoxImage.Warning);

    /// <inheritdoc />
    public void ShowError(string message) =>
        Show(message, MessageBoxButton.OK, MessageBoxImage.Error);

    /// <inheritdoc />
    public bool Confirm(string message) =>
        Show(message, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

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

    private static MessageBoxResult Show(string message, MessageBoxButton buttons, MessageBoxImage icon)
    {
        Window? owner = FindOwner();

        return owner is null
            ? MessageBox.Show(message, Caption, buttons, icon)
            : MessageBox.Show(owner, message, Caption, buttons, icon);
    }
}
