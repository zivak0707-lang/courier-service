using System.Windows.Controls;
using CourierService.App.Behaviors;

namespace CourierService.Tests.App;

public class InputMaskTests
{
    [Theory]
    [InlineData(InputMaskKind.Integer, "120", true)]
    [InlineData(InputMaskKind.Integer, "1,5", false)]
    [InlineData(InputMaskKind.Integer, "-5", false)]
    [InlineData(InputMaskKind.Integer, "абв", false)]
    [InlineData(InputMaskKind.Integer, " ", false)]
    [InlineData(InputMaskKind.Decimal, "1,5", true)]
    [InlineData(InputMaskKind.Decimal, "110.50", true)]
    [InlineData(InputMaskKind.Decimal, "1 234,56", true)]
    [InlineData(InputMaskKind.Decimal, "1 234,56", true)]   // нерозривний пробіл зі скопійованої суми
    [InlineData(InputMaskKind.Decimal, "1,5 кг", false)]
    [InlineData(InputMaskKind.Decimal, "-5", false)]
    [InlineData(InputMaskKind.Phone, "+38 (050) 123-45-67", true)]
    [InlineData(InputMaskKind.Phone, "050 123 45 67", true)]
    [InlineData(InputMaskKind.Phone, "050-ABC-45-67", false)]
    [InlineData(InputMaskKind.Phone, "тел. 0501234567", false)]
    [InlineData(InputMaskKind.Login, "courier_5.kyiv-1", true)]
    [InlineData(InputMaskKind.Login, "оператор", false)]       // кирилиця — типова помилка розкладки
    [InlineData(InputMaskKind.Login, "ivan petrenko", false)]
    [InlineData(InputMaskKind.None, "будь-що 123 !?", true)]
    public void IsAllowed_LetsThroughOnlyFittingCharacters(InputMaskKind kind, string text, bool expected)
    {
        Assert.Equal(expected, InputMask.IsAllowed(kind, text));
    }

    [Theory]
    [InlineData("0501234567", "+380 (50) 123-45-67")]
    [InlineData("050 123 45 67", "+380 (50) 123-45-67")]
    [InlineData("+38 (050) 123-45-67", "+380 (50) 123-45-67")]
    [InlineData("+380 (50) 123-45-67", "+380 (50) 123-45-67")]   // повторний вихід з поля нічого не змінює
    public void FormatPhone_RecognizedNumber_IsShownInOneFormat(string input, string expected)
    {
        Assert.Equal(expected, InputMask.FormatPhone(input));
    }

    [Theory]
    [InlineData("050123")]
    [InlineData("")]
    [InlineData("+48 501 234 567")]
    public void FormatPhone_UnrecognizedNumber_IsLeftAsTyped(string input)
    {
        // Користувач бачить, що саме ввів, а валідатор пояснить помилку
        Assert.Equal(input, InputMask.FormatPhone(input));
    }

    [Fact]
    public void Hint_ShownOnlyForEmptyVisibleField()
    {
        // Елементи WPF створюються лише в STA-потоці
        var results = new List<bool>();
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                var box = new TextBox();
                results.Add(Hint.ShouldShow(box));          // не показане на екрані — IsVisible = false
                var combo = new ComboBox { ItemsSource = new[] { "Дарницький" } };
                combo.SelectedIndex = 0;
                results.Add(Hint.ShouldShow(combo));
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
        Assert.Equal([false, false], results);
    }
}
