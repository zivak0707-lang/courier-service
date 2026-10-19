using System.Printing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using CourierService.App.Services;
using CourierService.Core.Orders;

namespace CourierService.App.Printing;

/// <summary>
/// Перегляд накладної перед друком.
/// </summary>
public partial class WaybillPreviewWindow : Window
{
    private readonly FixedDocument _document;
    private readonly string _jobName;
    private readonly IDialogService _dialogs;

    /// <summary>
    /// Створює вікно перегляду.
    /// </summary>
    /// <param name="waybill">Дані накладної.</param>
    /// <param name="dialogs">Повідомлення про помилки друку.</param>
    public WaybillPreviewWindow(Waybill waybill, IDialogService dialogs)
    {
        InitializeComponent();
        _dialogs = dialogs;
        _jobName = $"Накладна {waybill.TrackingNumber}";
        Title = _jobName;
        _document = WaybillPage.CreateDocument(waybill);
        Viewer.Document = _document;
        Loaded += (_, _) =>
        {
            // Пошук у тексті для однієї сторінки зайвий, до того ж стандартна панель пошуку не перекладена
            if (Viewer.Template.FindName("PART_FindToolBarHost", Viewer) is UIElement findBar)
            {
                findBar.Visibility = Visibility.Collapsed;
            }

            Viewer.FitToHeight();
        };
    }

    private void OnPrintClick(object sender, RoutedEventArgs e)
    {
        var dialog = new PrintDialog { UserPageRangeEnabled = false };
        dialog.PrintTicket.PageMediaSize = new PageMediaSize(PageMediaSizeName.ISOA5);
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            dialog.PrintDocument(_document.DocumentPaginator, _jobName);
        }
        catch (Exception ex) when (ex is PrintingCanceledException or PrintSystemException or InvalidOperationException)
        {
            // Принтер вимкнено, черга недоступна чи збереження PDF скасовано — програма працює далі
            _dialogs.ShowError($"Не вдалося надрукувати накладну.\n\n{ex.Message}");
        }
    }
}
