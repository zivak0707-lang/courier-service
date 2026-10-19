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
    private readonly Func<bool>? _copy;

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

    /// <summary>
    /// Створює інформаційне вікно з кнопкою копіювання (наприклад, трек-номера нового замовлення).
    /// </summary>
    /// <param name="message">Текст повідомлення.</param>
    /// <param name="kind">Вид повідомлення.</param>
    /// <param name="copyButtonText">Напис на кнопці копіювання.</param>
    /// <param name="copy">Дія копіювання; повертає <c>false</c>, якщо буфер обміну зайнятий.</param>
    public MessageWindow(string message, MessageKind kind, string copyButtonText, Func<bool> copy)
        : this(message, kind)
    {
        _copy = copy;
        CopyButton.Content = copyButtonText;
        CopyButton.Visibility = Visibility.Visible;
    }

    private void OnPrimaryClick(object sender, RoutedEventArgs e) => DialogResult = true;

    // Напис на кнопці підтверджує, що скопійовано, — окреме повідомлення тут зайве
    private void OnCopyClick(object sender, RoutedEventArgs e) =>
        CopyButton.Content = _copy?.Invoke() == true ? "✓ Скопійовано" : "Буфер обміну зайнятий — спробуйте ще раз";

}
