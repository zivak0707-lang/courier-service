using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Markup;
using CourierService.Core.Orders;

namespace CourierService.App.Printing;

/// <summary>
/// Друкована сторінка накладної формату A5.
/// </summary>
public partial class WaybillPage : UserControl
{
    /// <summary>Ширина A5 (148 мм) в одиницях WPF (1/96 дюйма).</summary>
    public const double A5Width = 148 / 25.4 * 96;

    /// <summary>Висота A5 (210 мм) в одиницях WPF.</summary>
    public const double A5Height = 210 / 25.4 * 96;

    /// <summary>
    /// Створює сторінку для накладної.
    /// </summary>
    /// <param name="waybill">Дані накладної.</param>
    public WaybillPage(Waybill waybill)
    {
        InitializeComponent();
        DataContext = waybill;
        QrImage.Source = QrCodeImage.Create(waybill.TrackingNumber);
        Width = A5Width;
        Height = A5Height;
    }

    /// <summary>
    /// Створює документ з однієї сторінки A5: його показує вікно перегляду й друкує стандартний діалог друку.
    /// </summary>
    /// <param name="waybill">Дані накладної.</param>
    /// <returns>Документ для перегляду й друку.</returns>
    public static FixedDocument CreateDocument(Waybill waybill)
    {
        var page = new FixedPage { Width = A5Width, Height = A5Height };
        page.Children.Add(new WaybillPage(waybill));

        // Розмітку рахуємо одразу: принтер отримує вже розкладену сторінку
        var size = new Size(A5Width, A5Height);
        page.Measure(size);
        page.Arrange(new Rect(size));
        page.UpdateLayout();

        var content = new PageContent();
        ((IAddChild)content).AddChild(page);
        var document = new FixedDocument();
        document.DocumentPaginator.PageSize = size;
        document.Pages.Add(content);
        return document;
    }
}
