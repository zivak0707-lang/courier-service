using System.Windows;
using System.Windows.Media;

namespace CourierService.App.Views;

/// <summary>Вид повідомлення: визначає значок і кнопки.</summary>
public enum MessageKind
{
    /// <summary>Інформація (кнопка «Гаразд»).</summary>
    Info,

    /// <summary>Попередження (кнопка «Гаразд»).</summary>
    Warning,

    /// <summary>Помилка (кнопка «Гаразд»).</summary>
    Error,

    /// <summary>Запитання (кнопки «Так» і «Ні»).</summary>
    Question,
}

/// <summary>
/// Модальне вікно повідомлення або запитання з кнопками українською.
/// </summary>
public partial class MessageWindow : Window
{
    /// <summary>
    /// Створює вікно.
    /// </summary>
    /// <param name="message">Текст повідомлення.</param>
    /// <param name="kind">Вид повідомлення.</param>
    public MessageWindow(string message, MessageKind kind)
    {
        InitializeComponent();
        MessageText.Text = message;

        (string glyph, Color color) = kind switch
        {
            MessageKind.Warning => ("", Color.FromRgb(0x9D, 0x5D, 0x00)),
            MessageKind.Error => ("", Color.FromRgb(0xC4, 0x2B, 0x1C)),
            MessageKind.Question => ("", Color.FromRgb(0x00, 0x5F, 0xB8)),
            _ => ("", Color.FromRgb(0x00, 0x5F, 0xB8)),
        };
        IconText.Text = glyph;
        IconText.Foreground = new SolidColorBrush(color);

        if (kind == MessageKind.Question)
        {
            PrimaryButton.Content = "_Так";
        }
        else
        {
            // Для повідомлень одна кнопка, яка закриває вікно і Enter, і Esc
            PrimaryButton.Content = "_Гаразд";
            PrimaryButton.IsCancel = true;
            SecondaryButton.Visibility = Visibility.Collapsed;
        }
    }

    private void OnPrimaryClick(object sender, RoutedEventArgs e) => DialogResult = true;
}
