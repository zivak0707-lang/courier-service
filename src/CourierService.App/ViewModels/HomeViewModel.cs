namespace CourierService.App.ViewModels;

/// <summary>
/// Стартова сторінка з коротким описом можливостей програми.
/// </summary>
public sealed class HomeViewModel : ViewModelBase
{
    /// <inheritdoc />
    public override string Title => "Головна";

    /// <summary>Привітання на стартовій сторінці.</summary>
    public string Greeting => "Вітаємо в програмі «Кур'єрська служба»";

    /// <summary>Пункти з основними можливостями програми.</summary>
    public IReadOnlyList<string> Features { get; } =
    [
        "Оформлення замовлень із розрахунком вартості за тарифом і районами Києва",
        "Призначення кур'єрів і контроль термінів доставки",
        "Облік оплат: готівка, картка, безготівковий розрахунок",
        "Звіти й дашборд: виручка, неоплачені замовлення, робота кур'єрів",
    ];

    /// <summary>Підказка, з чого почати.</summary>
    public string Hint => "Оберіть розділ у головному меню. Довідка — клавіша F1.";
}
