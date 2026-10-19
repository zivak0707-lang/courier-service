using System.Windows.Documents;
using CourierService.App.Printing;
using CourierService.Core.Orders;
using CourierService.Tests.Core;

namespace CourierService.Tests.App;

public class WaybillPrintingTests
{
    [Fact]
    public void QrCode_IsSquareImage_DifferentForDifferentNumbers()
    {
        var first = QrCodeImage.Create("KV2610190001-2");
        var second = QrCodeImage.Create("KV2610190002-0");

        Assert.True(first.IsFrozen);
        Assert.True(first.PixelWidth > 0);
        Assert.Equal(first.PixelWidth, first.PixelHeight);
        Assert.NotEqual(Pixels(first), Pixels(second));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void QrCode_EmptyText_Throws(string text)
    {
        Assert.ThrowsAny<ArgumentException>(() => QrCodeImage.Create(text));
    }

    [Fact]
    public void Document_IsOneA5Page()
    {
        // Елементи WPF створюються лише в STA-потоці, як у справжньому вікні
        Exception? error = null;
        int pages = 0;
        double width = 0, height = 0;
        var thread = new Thread(() =>
        {
            try
            {
                FixedDocument document = WaybillPage.CreateDocument(Waybill.From(WaybillTestData.FullOrder()));
                pages = document.Pages.Count;
                width = document.DocumentPaginator.PageSize.Width;
                height = document.DocumentPaginator.PageSize.Height;
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        Assert.Null(error);
        Assert.Equal(1, pages);
        Assert.Equal(148 / 25.4 * 96, width, 3);
        Assert.Equal(210 / 25.4 * 96, height, 3);
    }

    private static byte[] Pixels(System.Windows.Media.Imaging.BitmapSource image)
    {
        int stride = (image.PixelWidth * image.Format.BitsPerPixel + 7) / 8;
        var pixels = new byte[stride * image.PixelHeight];
        image.CopyPixels(pixels, stride, 0);
        return pixels;
    }
}
