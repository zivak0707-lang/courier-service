using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace CourierService.App.Behaviors;

/// <summary>
/// Сірий текст-підказка в порожньому полі («050 123 45 67», «Оберіть зону»). Зникає, щойно в полі з'являється значення.
/// </summary>
/// <remarks>
/// У WPF немає вбудованої властивості на кшталт PlaceholderText, тому підказка малюється поверх поля
/// окремим шаром (adorner) — так вона працює з будь-яким шаблоном поля, зокрема з темою Fluent.
/// </remarks>
public static class Hint
{
    /// <summary>Текст підказки.</summary>
    public static readonly DependencyProperty TextProperty = DependencyProperty.RegisterAttached(
        "Text", typeof(string), typeof(Hint), new PropertyMetadata(null, OnTextChanged));

    /// <summary>Повертає текст підказки.</summary>
    /// <param name="element">Поле.</param>
    /// <returns>Текст підказки.</returns>
    public static string? GetText(DependencyObject element) => (string?)element.GetValue(TextProperty);

    /// <summary>Задає текст підказки.</summary>
    /// <param name="element">Поле.</param>
    /// <param name="value">Текст підказки.</param>
    public static void SetText(DependencyObject element, string? value) => element.SetValue(TextProperty, value);

    /// <summary>
    /// Чи показувати підказку: поле видиме й порожнє (у текстовому полі немає тексту, у списку нічого не вибрано).
    /// </summary>
    /// <param name="control">Поле.</param>
    /// <returns><c>true</c>, якщо підказку треба показати.</returns>
    public static bool ShouldShow(Control control) => control.IsVisible && control switch
    {
        TextBox box => box.Text.Length == 0,
        ComboBox combo => combo.SelectedItem is null,
        _ => false,
    };

    private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Control control)
        {
            return;
        }

        control.Loaded -= OnStateChanged;
        control.IsVisibleChanged -= OnVisibilityChanged;
        control.Loaded += OnStateChanged;
        control.IsVisibleChanged += OnVisibilityChanged;
        switch (control)
        {
            case TextBox box:
                box.TextChanged -= OnStateChanged;
                box.TextChanged += OnStateChanged;
                break;
            case ComboBox combo:
                combo.SelectionChanged -= OnStateChanged;
                combo.SelectionChanged += OnStateChanged;
                break;
        }

        Update(control);
    }

    private static void OnStateChanged(object sender, RoutedEventArgs e) => Update((Control)sender);

    private static void OnVisibilityChanged(object sender, DependencyPropertyChangedEventArgs e) => Update((Control)sender);

    private static void Update(Control control)
    {
        if (AdornerLayer.GetAdornerLayer(control) is not { } layer)
        {
            return;
        }

        HintAdorner? existing = layer.GetAdorners(control)?.OfType<HintAdorner>().FirstOrDefault();
        bool show = ShouldShow(control) && !string.IsNullOrEmpty(GetText(control));
        if (show && existing is null)
        {
            layer.Add(new HintAdorner(control));
        }
        else if (!show && existing is not null)
        {
            layer.Remove(existing);
        }
        else
        {
            existing?.InvalidateVisual();
        }
    }

    /// <summary>
    /// Шар, що малює підказку на місці тексту поля. Не перехоплює мишу: клік потрапляє в саме поле.
    /// </summary>
    private sealed class HintAdorner : Adorner
    {
        private static readonly Brush HintBrush = CreateHintBrush();

        public HintAdorner(Control control)
            : base(control)
        {
            IsHitTestVisible = false;
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            var control = (Control)AdornedElement;
            string? text = GetText(control);
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            var formatted = new FormattedText(
                text,
                CultureInfo.CurrentUICulture,
                control.FlowDirection,
                new Typeface(control.FontFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
                control.FontSize,
                HintBrush,
                VisualTreeHelper.GetDpi(control).PixelsPerDip)
            {
                MaxTextWidth = Math.Max(1, control.ActualWidth - control.Padding.Left - control.Padding.Right - 36),
                MaxLineCount = 1,
                Trimming = TextTrimming.CharacterEllipsis,
            };

            // Текст поля починається після рамки, внутрішнього відступу й 2 пікселів поля редагування
            double left = control.BorderThickness.Left + control.Padding.Left + 2;
            double top = control is TextBox { AcceptsReturn: true }
                ? control.BorderThickness.Top + control.Padding.Top + 1
                : (control.ActualHeight - formatted.Height) / 2;
            drawingContext.DrawText(formatted, new Point(left, top));
        }

        private static Brush CreateHintBrush()
        {
            var brush = new SolidColorBrush(Color.FromRgb(0x8A, 0x8A, 0x8A));
            brush.Freeze();
            return brush;
        }
    }
}
