namespace CourierService.Core.Entities;

/// <summary>
/// Тариф доставки: параметри формули вартості (рішення D-06, D-13).
/// </summary>
public class Tariff : ReferenceEntity
{
    /// <summary>Назва тарифу (унікальна).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Базова ціна, що включає <see cref="IncludedWeightGrams"/>.</summary>
    public Money BasePrice { get; set; }

    /// <summary>Вага, включена в базову ціну, у грамах.</summary>
    public int IncludedWeightGrams { get; set; }

    /// <summary>Ціна кожного розпочатого кілограма понад включену вагу.</summary>
    public Money PricePerExtraKg { get; set; }

    /// <summary>Максимальна розрахункова вага посилки для тарифу, у грамах.</summary>
    public int MaxWeightGrams { get; set; }

    /// <summary>Термін доставки в робочих годинах (рішення D-14).</summary>
    public int DeliveryHours { get; set; }

    /// <summary>Доплата, якщо зони «звідки» й «куди» на різних берегах Дніпра.</summary>
    public Money CrossRiverSurcharge { get; set; }

    /// <summary>Оголошена цінність, що не тарифікується страхуванням.</summary>
    public Money InsuranceFreeLimit { get; set; }

    /// <summary>Ставка страхування в проміле від цінності понад ліміт (5 = 0,5 %).</summary>
    public int InsurancePermille { get; set; }

    /// <summary>Доплата за крихку посилку.</summary>
    public Money FragileSurcharge { get; set; }
}
