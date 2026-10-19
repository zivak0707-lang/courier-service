using System.Security.Cryptography;

namespace CourierService.Core.Security;

/// <summary>
/// Результат хешування пароля: хеш і сіль у Base64.
/// </summary>
/// <param name="Hash">Хеш пароля.</param>
/// <param name="Salt">Випадкова сіль.</param>
public readonly record struct PasswordHashResult(string Hash, string Salt);

/// <summary>
/// Хешування й перевірка паролів.
/// </summary>
public interface IPasswordHasher
{
    /// <summary>
    /// Обчислює хеш пароля з новою випадковою сіллю.
    /// </summary>
    /// <param name="password">Пароль.</param>
    /// <returns>Хеш і сіль для збереження в БД.</returns>
    PasswordHashResult Hash(string password);

    /// <summary>
    /// Перевіряє, чи відповідає пароль збереженому хешу.
    /// </summary>
    /// <param name="password">Введений пароль.</param>
    /// <param name="hash">Збережений хеш.</param>
    /// <param name="salt">Збережена сіль.</param>
    /// <returns><c>true</c>, якщо пароль правильний.</returns>
    bool Verify(string password, string hash, string salt);
}

/// <summary>
/// Хешування паролів алгоритмом PBKDF2-HMAC-SHA512 вбудованими засобами .NET (рішення D-08).
/// </summary>
/// <remarks>
/// Сам пароль ніде не зберігається. Сіль робить однакові паролі різними в БД,
/// а велика кількість ітерацій уповільнює підбір пароля.
/// </remarks>
public sealed class Pbkdf2PasswordHasher : IPasswordHasher
{
    /// <summary>Кількість ітерацій (рекомендація OWASP для PBKDF2-HMAC-SHA512).</summary>
    public const int Iterations = 210_000;

    private const int SaltSizeBytes = 16;
    private const int HashSizeBytes = 32;
    private static readonly HashAlgorithmName Algorithm = HashAlgorithmName.SHA512;

    /// <inheritdoc />
    /// <exception cref="ArgumentException">Пароль порожній.</exception>
    public PasswordHashResult Hash(string password)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);

        byte[] salt = RandomNumberGenerator.GetBytes(SaltSizeBytes);
        byte[] hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, Algorithm, HashSizeBytes);

        return new PasswordHashResult(Convert.ToBase64String(hash), Convert.ToBase64String(salt));
    }

    /// <inheritdoc />
    public bool Verify(string password, string hash, string salt)
    {
        if (string.IsNullOrEmpty(password))
        {
            return false;
        }

        byte[] expectedHash;
        byte[] saltBytes;
        try
        {
            expectedHash = Convert.FromBase64String(hash);
            saltBytes = Convert.FromBase64String(salt);
        }
        catch (FormatException)
        {
            return false; // пошкоджений запис у БД — вхід неможливий
        }

        byte[] actualHash = Rfc2898DeriveBytes.Pbkdf2(password, saltBytes, Iterations, Algorithm, expectedHash.Length);

        // Порівняння за сталий час: тривалість не підказує, скільки байтів збіглося
        return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
    }
}
