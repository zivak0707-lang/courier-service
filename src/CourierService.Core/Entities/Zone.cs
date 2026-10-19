using CourierService.Core.Enums;

namespace CourierService.Core.Entities;

/// <summary>
/// Зона доставки — район Києва (рішення D-07).
/// </summary>
public class Zone : ReferenceEntity
{
    /// <summary>Назва району (унікальна).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Берег Дніпра: від нього залежить доплата за переїзд.</summary>
    public RiverBank Bank { get; set; }

    /// <summary>Коефіцієнт віддаленості від центру у відсотках (115 = ×1,15).</summary>
    public int CoefficientPercent { get; set; } = 100;
}
