using System.IO;

namespace CourierService.App.Infrastructure;

/// <summary>
/// Шляхи до файлів застосунку: каталог даних користувача, журнали, файл бази даних, резервні копії.
/// </summary>
/// <remarks>
/// Дані зберігаються в %LocalAppData%\CourierService, а не поруч з exe:
/// у Program Files звичайний користувач не має права запису.
/// </remarks>
public static class AppPaths
{
    /// <summary>Назва каталогу застосунку всередині %LocalAppData%.</summary>
    public const string AppFolderName = "CourierService";

    /// <summary>Кореневий каталог даних застосунку.</summary>
    public static string DataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        AppFolderName);

    /// <summary>Каталог файлів журналу.</summary>
    public static string LogsDirectory { get; } = Path.Combine(DataDirectory, "logs");

    /// <summary>Каталог резервних копій бази даних (рішення D-35).</summary>
    public static string BackupsDirectory { get; } = Path.Combine(DataDirectory, "backups");

    /// <summary>Повний шлях до файлу бази даних SQLite.</summary>
    public static string DatabasePath { get; } = Path.Combine(DataDirectory, "courier.db");

    /// <summary>
    /// Створює каталоги даних і журналів, якщо їх ще немає.
    /// </summary>
    public static void EnsureDirectoriesExist()
    {
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(LogsDirectory);
    }
}
