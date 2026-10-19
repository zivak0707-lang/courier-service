using System.Windows;

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

    private static MessageBoxResult Show(string message, MessageBoxButton buttons, MessageBoxImage icon)
    {
        // Власник — активне вікно, щоб повідомлення не сховалося за ним
        Window? owner = Application.Current?.Windows
            .OfType<Window>()
            .FirstOrDefault(window => window.IsActive);

        return owner is null
            ? MessageBox.Show(message, Caption, buttons, icon)
            : MessageBox.Show(owner, message, Caption, buttons, icon);
    }
}
