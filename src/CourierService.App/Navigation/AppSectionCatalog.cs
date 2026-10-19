namespace CourierService.App.Navigation;

/// <summary>
/// Опис розділу застосунку.
/// </summary>
/// <param name="Title">Назва розділу для заголовка й меню.</param>
/// <param name="Description">Короткий опис призначення розділу.</param>
/// <param name="Iteration">Номер ітерації плану, у якій розділ буде реалізовано.</param>
public sealed record AppSectionInfo(string Title, string Description, int Iteration);

/// <summary>
/// Довідник розділів застосунку: назва, опис і ітерація реалізації (docs/plan.md).
/// </summary>
public static class AppSectionCatalog
{
    private static readonly Dictionary<AppSection, AppSectionInfo> Sections = new()
    {
        [AppSection.ChangeUser] = new("Змінити користувача", "Вихід із поточного облікового запису та вхід під іншим.", 2),
        [AppSection.NewOrder] = new("Нове замовлення", "Оформлення замовлення з розрахунком вартості під час введення.", 4),
        [AppSection.OrderList] = new("Список замовлень", "Пошук, фільтри та сортування замовлень.", 4),
        [AppSection.TrackingSearch] = new("Пошук за трек-номером", "Швидкий пошук замовлення за трек-номером.", 4),
        [AppSection.MyActiveDeliveries] = new("Мої доставки: активні", "Доставки кур'єра: «забрав», «доставив», «невдала спроба», підтвердження готівки.", 5),
        [AppSection.MyCompletedDeliveries] = new("Мої доставки: виконані", "Історія виконаних доставок кур'єра.", 5),
        [AppSection.Clients] = new("Клієнти", "Довідник відправників.", 3),
        [AppSection.Couriers] = new("Кур'єри", "Довідник кур'єрів з обліковими записами.", 3),
        [AppSection.Zones] = new("Зони доставки", "Райони Києва та їхні коефіцієнти.", 3),
        [AppSection.Tariffs] = new("Тарифи", "Стандарт, Експрес, Термінова: ціни, терміни, обмеження.", 3),
        [AppSection.Dashboard] = new("Дашборд", "Графіки виручки, статусів і показники роботи служби.", 6),
        [AppSection.RevenueReport] = new("Виручка", "Виручка за оплаченими замовленнями за період.", 6),
        [AppSection.UnpaidReport] = new("Неоплачені замовлення", "Замовлення, оплату яких ще не отримано.", 6),
        [AppSection.CourierPerformanceReport] = new("Ефективність кур'єрів", "Кількість доставок і частка вчасних за кур'єрами.", 6),
        [AppSection.CourierPayoutReport] = new("Нарахування кур'єрам", "Виплати кур'єрам і валовий дохід служби.", 6),
        [AppSection.Users] = new("Користувачі", "Облікові записи та ролі.", 3),
    };

    /// <summary>
    /// Повертає опис розділу.
    /// </summary>
    /// <param name="section">Розділ застосунку.</param>
    /// <returns>Опис розділу.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Для розділу немає опису.</exception>
    public static AppSectionInfo Get(AppSection section) =>
        Sections.TryGetValue(section, out var info)
            ? info
            : throw new ArgumentOutOfRangeException(nameof(section), section, "Невідомий розділ застосунку.");
}
