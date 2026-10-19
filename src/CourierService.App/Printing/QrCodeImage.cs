using System.IO;
using System.Windows.Media.Imaging;
using QRCoder;

namespace CourierService.App.Printing;

/// <summary>
/// QR-код як зображення WPF (бібліотека QRCoder, рішення D-36).
/// </summary>
public static class QrCodeImage
{
    /// <summary>
    /// Створює QR-код із текстом.
    /// </summary>
    /// <param name="text">Закодований текст (трек-номер).</param>
    /// <param name="pixelsPerModule">Розмір одного квадратика коду в пікселях.</param>
    /// <returns>Заморожене зображення: його можна використовувати в будь-якому потоці.</returns>
    public static BitmapSource Create(string text, int pixelsPerModule = 10)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        // Рівень корекції M відновлює до ~15 % пошкодженого коду — достатньо для зім'ятої накладної
        using var generator = new QRCodeGenerator();
        using QRCodeData data = generator.CreateQrCode(text, QRCodeGenerator.ECCLevel.M);
        byte[] png = new PngByteQRCode(data).GetGraphic(pixelsPerModule);

        var image = new BitmapImage();
        using var stream = new MemoryStream(png);
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad; // дані читаються одразу, потік можна закрити
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }
}
