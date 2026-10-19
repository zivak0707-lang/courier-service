using System.Globalization;
using CourierService.Core.Entities;
using CourierService.Core.Pricing;
using CourierService.Core.Sla;
using CourierService.Core.Validation;

namespace CourierService.App.ViewModels.Orders;

/// <summary>Рядок розбивки вартості: що саме й скільки коштує.</summary>
/// <param name="Label">Опис складової.</param>
/// <param name="Amount">Сума, «132,00 грн».</param>
public sealed record PriceLine(string Label, string Amount);

/// <summary>
/// Вартість для показу у формі замовлення: розбивка й підсумок або підказка, чого бракує для розрахунку.
/// </summary>
/// <param name="IsReady">Чи вдалося порахувати вартість.</param>
/// <param name="Hint">Підказка, якщо вартість ще не порахована.</param>
/// <param name="Lines">Складові вартості.</param>
/// <param name="Total">Підсумок.</param>
/// <param name="Details">Розрахункова вага й коефіцієнт зони.</param>
/// <param name="DeadlineText">Крайній термін доставки, якщо оформити замовлення зараз.</param>
public sealed record PriceQuote(
    bool IsReady,
    string? Hint,
    IReadOnlyList<PriceLine> Lines,
    string Total,
    string Details,
    string DeadlineText)
{
    private static readonly CultureInfo Ukrainian = CultureInfo.GetCultureInfo("uk-UA");

    /// <summary>
    /// Вартість ще не порахована.
    /// </summary>
    /// <param name="hint">Чого бракує або що виправити.</param>
    /// <returns>Стан без розбивки.</returns>
    public static PriceQuote NotReady(string hint) => new(false, hint, [], string.Empty, string.Empty, string.Empty);

    /// <summary>
    /// Готує розбивку для показу: базова ціна завжди, інші складові — лише ненульові.
    /// </summary>
    /// <param name="price">Результат калькулятора.</param>
    /// <param name="tariff">Тариф.</param>
    /// <param name="parcel">Параметри посилки.</param>
    /// <param name="deadline">Дедлайн доставки.</param>
    /// <returns>Розбивка для показу.</returns>
    public static PriceQuote From(PriceBreakdown price, Tariff tariff, ParcelParameters parcel, DateTime deadline)
    {
        ArgumentNullException.ThrowIfNull(price);
        ArgumentNullException.ThrowIfNull(tariff);
        ArgumentNullException.ThrowIfNull(parcel);

        string coefficient = price.ZoneCoefficientPercent == 100
            ? string.Empty
            : " × " + InputParsing.FormatCoefficient(price.ZoneCoefficientPercent);

        var lines = new List<PriceLine>
        {
            new($"Базова ціна (до {Kilograms(tariff.IncludedWeightGrams)} кг){coefficient}", price.BaseCost.ToString()),
        };
        if (price.ExtraKilograms > 0)
        {
            lines.Add(new($"Додаткова вага: {price.ExtraKilograms} кг × {tariff.PricePerExtraKg}{coefficient}",
                price.WeightCost.ToString()));
        }

        if (price.CrossesRiver)
        {
            lines.Add(new("Переїзд через Дніпро", price.CrossRiverCost.ToString()));
        }

        if (price.InsuranceCost.Kopecks > 0)
        {
            lines.Add(new($"Страхування: {tariff.InsurancePermille} ‰ від цінності понад {tariff.InsuranceFreeLimit}",
                price.InsuranceCost.ToString()));
        }

        if (price.FragileCost.Kopecks > 0)
        {
            lines.Add(new("Крихка посилка", price.FragileCost.ToString()));
        }

        bool volumetric = price.VolumetricWeightGrams > parcel.WeightGrams;
        string details = $"Розрахункова вага {Kilograms(price.ChargeableWeightGrams)} кг"
            + (volumetric ? " (об'ємна: Д × Ш × В / 4000)" : string.Empty)
            + $", коефіцієнт зони ×{InputParsing.FormatCoefficient(price.ZoneCoefficientPercent)}";
        string deadlineText = $"Доставити до {deadline.ToString("dd.MM.yyyy HH:mm", Ukrainian)} "
            + $"({tariff.DeliveryHours} роб. год; служба працює {DeadlineCalculator.WorkdayStart:hh\\:mm}–{DeadlineCalculator.WorkdayEnd:hh\\:mm})";

        return new(true, null, lines, price.Total.ToString(), details, deadlineText);
    }

    private static string Kilograms(int grams) => InputParsing.FormatKilograms(grams);
}
