namespace CourierService.App.Services;

/// <summary>
/// Показ повідомлень і запитів підтвердження.
/// </summary>
/// <remarks>
/// ViewModel-и не викликають MessageBox напряму, а працюють через цей інтерфейс,
/// тому в тестах його можна замінити заглушкою (рішення D-03).
/// </remarks>
public interface IDialogService
{
    /// <summary>Показує інформаційне повідомлення.</summary>
    /// <param name="message">Текст повідомлення.</param>
    void ShowInfo(string message);

    /// <summary>Показує попередження.</summary>
    /// <param name="message">Текст попередження.</param>
    void ShowWarning(string message);

    /// <summary>Показує повідомлення про помилку.</summary>
    /// <param name="message">Зрозумілий користувачу опис помилки.</param>
    void ShowError(string message);

    /// <summary>Запитує в користувача підтвердження дії.</summary>
    /// <param name="message">Питання до користувача.</param>
    /// <returns><c>true</c>, якщо користувач погодився.</returns>
    bool Confirm(string message);

    /// <summary>Показує модальне вікно «Про програму».</summary>
    void ShowAbout();
}
