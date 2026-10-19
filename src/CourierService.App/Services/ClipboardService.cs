using System.Runtime.InteropServices;
using System.Windows;
using Microsoft.Extensions.Logging;

namespace CourierService.App.Services;

/// <summary>
/// Копіювання тексту в буфер обміну. Винесено в інтерфейс, щоб команди «Копіювати» можна було перевірити тестом.
/// </summary>
public interface IClipboardService
{
    /// <summary>
    /// Кладе текст у буфер обміну.
    /// </summary>
    /// <param name="text">Текст.</param>
    /// <returns><c>false</c>, якщо буфер тимчасово зайнятий іншою програмою.</returns>
    bool TrySetText(string text);
}

/// <summary>
/// Реалізація <see cref="IClipboardService"/> через буфер обміну Windows.
/// </summary>
public sealed class ClipboardService : IClipboardService
{
    private readonly ILogger<ClipboardService> _logger;

    /// <summary>
    /// Створює сервіс.
    /// </summary>
    /// <param name="logger">Журнал.</param>
    public ClipboardService(ILogger<ClipboardService> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public bool TrySetText(string text)
    {
        try
        {
            Clipboard.SetText(text);
            return true;
        }
        catch (ExternalException ex)
        {
            // Буфер обміну може тримати інша програма — це не збій нашої програми
            _logger.LogWarning(ex, "Не вдалося скопіювати текст у буфер обміну");
            return false;
        }
    }
}

/// <summary>
/// Спільна дія «копіювати трек-номер» для сторінок і картки замовлення.
/// </summary>
public static class TrackingNumberCopy
{
    /// <summary>Повідомлення, якщо буфер обміну зайнятий.</summary>
    public const string ClipboardBusyMessage = "Не вдалося скопіювати: буфер обміну зайнятий іншою програмою. Спробуйте ще раз.";

    /// <summary>
    /// Копіює трек-номер; якщо не вдалося — пояснює чому.
    /// </summary>
    /// <param name="clipboard">Буфер обміну.</param>
    /// <param name="dialogs">Повідомлення.</param>
    /// <param name="trackingNumber">Трек-номер.</param>
    /// <returns><c>true</c>, якщо номер скопійовано.</returns>
    public static bool Copy(IClipboardService clipboard, IDialogService dialogs, string? trackingNumber)
    {
        if (string.IsNullOrEmpty(trackingNumber))
        {
            return false;
        }

        if (clipboard.TrySetText(trackingNumber))
        {
            return true;
        }

        dialogs.ShowWarning(ClipboardBusyMessage);
        return false;
    }
}
