using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CourierService.Core.Validation;

namespace CourierService.App.Behaviors;

/// <summary>
/// Які символи можна вводити в поле.
/// </summary>
public enum InputMaskKind
{
    /// <summary>Без обмежень.</summary>
    None,

    /// <summary>Ціле число: лише цифри (габарити, години, відсотки).</summary>
    Integer,

    /// <summary>Дробове число: цифри, кома або крапка, пробіл між розрядами (вага, суми, коефіцієнт).</summary>
    Decimal,

    /// <summary>Телефон: цифри, «+», дужки, дефіс і пробіл; після виходу з поля номер форматується.</summary>
    Phone,

    /// <summary>Логін: латинські літери, цифри, «.», «_», «-».</summary>
    Login,
}

/// <summary>
/// М'яка маска введення: не дає набрати символи, яких у полі бути не може (літеру у вазі, кирилицю в логіні),
/// але не нав'язує жорсткого шаблону — номер, скопійований у будь-якому вигляді, вставляється без проблем.
/// Межі значень і формат загалом перевіряє валідатор, маска лише відсікає явно зайве.
/// </summary>
public static class InputMask
{
    /// <summary>Вид маски поля.</summary>
    public static readonly DependencyProperty KindProperty = DependencyProperty.RegisterAttached(
        "Kind", typeof(InputMaskKind), typeof(InputMask), new PropertyMetadata(InputMaskKind.None, OnKindChanged));

    /// <summary>Повертає вид маски поля.</summary>
    /// <param name="element">Поле.</param>
    /// <returns>Вид маски.</returns>
    public static InputMaskKind GetKind(DependencyObject element) => (InputMaskKind)element.GetValue(KindProperty);

    /// <summary>Задає вид маски поля.</summary>
    /// <param name="element">Поле.</param>
    /// <param name="value">Вид маски.</param>
    public static void SetKind(DependencyObject element, InputMaskKind value) => element.SetValue(KindProperty, value);

    /// <summary>
    /// Чи всі символи тексту допустимі для маски (перевіряється і набраний символ, і вставлений текст).
    /// </summary>
    /// <param name="kind">Вид маски.</param>
    /// <param name="text">Текст.</param>
    /// <returns><c>true</c>, якщо текст можна ввести.</returns>
    public static bool IsAllowed(InputMaskKind kind, string text) => text.All(ch => kind switch
    {
        InputMaskKind.Integer => char.IsAsciiDigit(ch),
        InputMaskKind.Decimal => char.IsAsciiDigit(ch) || ch is ',' or '.' or ' ' or ' ',
        InputMaskKind.Phone => char.IsAsciiDigit(ch) || ch is '+' or '(' or ')' or '-' or ' ',
        InputMaskKind.Login => char.IsAsciiLetter(ch) || char.IsAsciiDigit(ch) || ch is '.' or '_' or '-',
        _ => true,
    });

    /// <summary>
    /// Вигляд номера після виходу з поля: розпізнаний номер — «+380 (50) 123-45-67», інакше — як є
    /// (щоб користувач бачив, що саме ввів, а валідатор пояснив помилку).
    /// </summary>
    /// <param name="text">Введений номер.</param>
    /// <returns>Відформатований або незмінний текст.</returns>
    public static string FormatPhone(string text) =>
        PhoneNumber.TryNormalize(text, out _) ? PhoneNumber.Format(text) : text;

    private static void OnKindChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBox box)
        {
            return;
        }

        box.PreviewTextInput -= OnPreviewTextInput;
        box.PreviewKeyDown -= OnPreviewKeyDown;
        box.LostKeyboardFocus -= OnLostKeyboardFocus;
        DataObject.RemovePastingHandler(box, OnPasting);
        if ((InputMaskKind)e.NewValue == InputMaskKind.None)
        {
            return;
        }

        box.PreviewTextInput += OnPreviewTextInput;
        box.PreviewKeyDown += OnPreviewKeyDown;
        box.LostKeyboardFocus += OnLostKeyboardFocus;
        DataObject.AddPastingHandler(box, OnPasting);
    }

    private static void OnPreviewTextInput(object sender, TextCompositionEventArgs e) =>
        e.Handled = !IsAllowed(GetKind((TextBox)sender), e.Text);

    // Пробіл не проходить через PreviewTextInput, тому перевіряється окремо
    private static void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space)
        {
            e.Handled = !IsAllowed(GetKind((TextBox)sender), " ");
        }
    }

    private static void OnPasting(object sender, DataObjectPastingEventArgs e)
    {
        if (e.DataObject.GetData(DataFormats.UnicodeText) is not string text
            || !IsAllowed(GetKind((TextBox)sender), text.Trim()))
        {
            e.CancelCommand();
        }
    }

    private static void OnLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        var box = (TextBox)sender;
        if (GetKind(box) == InputMaskKind.Phone)
        {
            string formatted = FormatPhone(box.Text);
            if (formatted != box.Text)
            {
                box.Text = formatted;
            }
        }
    }
}
