using System.Text.RegularExpressions;

namespace CourierService.Core.Validation;

/// <summary>
/// Типові правила перевірки полів. Методи розширення дозволяють записувати валідатори
/// коротко: <c>errors.RequiredText(nameof(input.Name), input.Name, "Назва", 50)</c>.
/// </summary>
public static partial class ValidationRules
{
    /// <summary>Мінімальна довжина пароля.</summary>
    public const int PasswordMinLength = 8;

    /// <summary>Максимальна довжина пароля.</summary>
    public const int PasswordMaxLength = 100;

    /// <summary>Обов'язковий текст з обмеженням довжини.</summary>
    /// <param name="errors">Результат перевірки.</param>
    /// <param name="property">Назва поля.</param>
    /// <param name="value">Значення.</param>
    /// <param name="label">Назва поля для повідомлення.</param>
    /// <param name="maxLength">Максимальна довжина.</param>
    public static void RequiredText(this ValidationErrors errors, string property, string? value, string label, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add(property, $"Заповніть поле «{label}».");
        }
        else
        {
            errors.OptionalText(property, value, label, maxLength);
        }
    }

    /// <summary>Необов'язковий текст з обмеженням довжини.</summary>
    /// <param name="errors">Результат перевірки.</param>
    /// <param name="property">Назва поля.</param>
    /// <param name="value">Значення.</param>
    /// <param name="label">Назва поля для повідомлення.</param>
    /// <param name="maxLength">Максимальна довжина.</param>
    public static void OptionalText(this ValidationErrors errors, string property, string? value, string label, int maxLength)
    {
        if (value is not null && value.Trim().Length > maxLength)
        {
            errors.Add(property, $"Поле «{label}» — не більше {maxLength} символів.");
        }
    }

    /// <summary>Ціле число в межах.</summary>
    /// <param name="errors">Результат перевірки.</param>
    /// <param name="property">Назва поля.</param>
    /// <param name="value">Значення.</param>
    /// <param name="min">Мінімум (включно).</param>
    /// <param name="max">Максимум (включно).</param>
    /// <param name="message">Повідомлення, якщо значення поза межами.</param>
    public static void InRange(this ValidationErrors errors, string property, int value, int min, int max, string message)
    {
        if (value < min || value > max)
        {
            errors.Add(property, message);
        }
    }

    /// <summary>Грошова сума в межах.</summary>
    /// <param name="errors">Результат перевірки.</param>
    /// <param name="property">Назва поля.</param>
    /// <param name="value">Сума.</param>
    /// <param name="min">Мінімум (включно).</param>
    /// <param name="max">Максимум (включно).</param>
    /// <param name="label">Назва поля для повідомлення.</param>
    public static void MoneyInRange(this ValidationErrors errors, string property, Money value, Money min, Money max, string label)
    {
        if (value < min || value > max)
        {
            errors.Add(property, $"Поле «{label}» має бути від {min} до {max}.");
        }
    }

    /// <summary>Значення перелічення визначене (захист від пошкоджених даних).</summary>
    /// <typeparam name="TEnum">Тип перелічення.</typeparam>
    /// <param name="errors">Результат перевірки.</param>
    /// <param name="property">Назва поля.</param>
    /// <param name="value">Значення.</param>
    /// <param name="label">Назва поля для повідомлення.</param>
    public static void Defined<TEnum>(this ValidationErrors errors, string property, TEnum value, string label)
        where TEnum : struct, Enum
    {
        if (!Enum.IsDefined(value))
        {
            errors.Add(property, $"Оберіть значення поля «{label}».");
        }
    }

    /// <summary>Обов'язковий український номер телефону.</summary>
    /// <param name="errors">Результат перевірки.</param>
    /// <param name="property">Назва поля.</param>
    /// <param name="value">Номер.</param>
    public static void Phone(this ValidationErrors errors, string property, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add(property, "Заповніть поле «Телефон».");
        }
        else if (!PhoneNumber.TryNormalize(value, out _))
        {
            errors.Add(property, "Телефон має бути українським номером, наприклад +380 50 123 45 67 або 050 123 45 67.");
        }
    }

    /// <summary>Необов'язкова адреса електронної пошти.</summary>
    /// <param name="errors">Результат перевірки.</param>
    /// <param name="property">Назва поля.</param>
    /// <param name="value">Адреса.</param>
    /// <param name="maxLength">Максимальна довжина.</param>
    public static void OptionalEmail(this ValidationErrors errors, string property, string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        string email = value.Trim();
        if (email.Length > maxLength)
        {
            errors.Add(property, $"Електронна пошта — не більше {maxLength} символів.");
        }
        else if (!EmailPattern().IsMatch(email))
        {
            errors.Add(property, "Електронна пошта має бути у форматі name@example.com.");
        }
    }

    /// <summary>Логін: 3–50 символів, латинські літери, цифри, «.», «_», «-», починається з літери.</summary>
    /// <param name="errors">Результат перевірки.</param>
    /// <param name="property">Назва поля.</param>
    /// <param name="value">Логін.</param>
    public static void Login(this ValidationErrors errors, string property, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add(property, "Заповніть поле «Логін».");
        }
        else if (!LoginPattern().IsMatch(value.Trim()))
        {
            errors.Add(property, "Логін: 3–50 латинських літер, цифр або символів «.», «_», «-»; починається з літери.");
        }
    }

    /// <summary>Пароль: щонайменше 8 символів, хоча б одна літера й одна цифра.</summary>
    /// <param name="errors">Результат перевірки.</param>
    /// <param name="property">Назва поля.</param>
    /// <param name="value">Пароль.</param>
    /// <param name="required">Чи обов'язковий пароль (для нового облікового запису).</param>
    public static void Password(this ValidationErrors errors, string property, string? value, bool required)
    {
        if (string.IsNullOrEmpty(value))
        {
            if (required)
            {
                errors.Add(property, "Введіть пароль.");
            }

            return;
        }

        if (value.Length < PasswordMinLength || value.Length > PasswordMaxLength
            || !value.Any(char.IsLetter) || !value.Any(char.IsDigit))
        {
            errors.Add(property, $"Пароль: від {PasswordMinLength} до {PasswordMaxLength} символів, щонайменше одна літера й одна цифра.");
        }
    }

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    private static partial Regex EmailPattern();

    [GeneratedRegex(@"^[A-Za-z][A-Za-z0-9._-]{2,49}$")]
    private static partial Regex LoginPattern();
}
