using System.Windows;
using CourierService.App.Printing;
using CourierService.Core.Orders;

namespace CourierService.App.Services;

/// <summary>
/// Перегляд і друк накладної (рішення D-36). Винесено в інтерфейс, щоб картку замовлення можна було
/// перевірити тестом без вікон і принтера.
/// </summary>
public interface IWaybillPrinter
{
    /// <summary>
    /// Показує накладну у вікні перегляду з кнопкою друку.
    /// </summary>
    /// <param name="waybill">Дані накладної.</param>
    void ShowPreview(Waybill waybill);
}

/// <summary>
/// Реалізація <see cref="IWaybillPrinter"/>: модальне вікно перегляду над активним вікном.
/// </summary>
public sealed class WaybillPrinter : IWaybillPrinter
{
    private readonly IDialogService _dialogs;

    /// <summary>
    /// Створює сервіс.
    /// </summary>
    /// <param name="dialogs">Повідомлення про помилки друку.</param>
    public WaybillPrinter(IDialogService dialogs)
    {
        _dialogs = dialogs;
    }

    /// <inheritdoc />
    public void ShowPreview(Waybill waybill)
    {
        var window = new WaybillPreviewWindow(waybill, _dialogs);

        // Власник — картка замовлення, з якої натиснули «Друк накладної»
        if (Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive) is { } owner)
        {
            window.Owner = owner;
        }

        window.ShowDialog();
    }
}
