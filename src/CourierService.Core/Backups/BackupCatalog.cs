using System.Globalization;
using System.Text.RegularExpressions;

namespace CourierService.Core.Backups;

/// <summary>
/// Звідки взялася резервна копія.
/// </summary>
public enum BackupKind
{
    /// <summary>Автоматична копія під час першого запуску за день.</summary>
    Daily,

    /// <summary>Копія, зроблена адміністратором вручну.</summary>
    Manual,

    /// <summary>Страхувальна копія поточної бази перед відновленням з іншої копії.</summary>
    BeforeRestore,
}

/// <summary>
/// Резервна копія в каталозі копій.
/// </summary>
/// <param name="FileName">Назва файлу.</param>
/// <param name="CreatedAt">Коли створено (з назви файлу).</param>
/// <param name="Kind">Вид копії.</param>
public sealed record BackupFile(string FileName, DateTime CreatedAt, BackupKind Kind);

/// <summary>
/// Правила іменування й зберігання резервних копій (рішення D-35).
/// Назва файлу містить дату й вид копії, тому список копій не потребує окремої таблиці в БД,
/// яку довелося б відновлювати разом із даними.
/// </summary>
public static partial class BackupCatalog
{
    /// <summary>Скільки щоденних автоматичних копій зберігати.</summary>
    public const int DailyCopiesToKeep = 7;

    /// <summary>Розширення файлів копій.</summary>
    public const string Extension = ".db";

    private const string TimestampFormat = "yyyyMMdd-HHmmss-fff";

    private static readonly Dictionary<BackupKind, string> Suffixes = new()
    {
        [BackupKind.Daily] = "auto",
        [BackupKind.Manual] = "manual",
        [BackupKind.BeforeRestore] = "before-restore",
    };

    /// <summary>
    /// Назва файлу нової копії, наприклад <c>courier-20261019-093000-000-auto.db</c>.
    /// </summary>
    /// <param name="createdAt">Час створення.</param>
    /// <param name="kind">Вид копії.</param>
    /// <returns>Назва файлу.</returns>
    public static string FileNameFor(DateTime createdAt, BackupKind kind) =>
        $"courier-{createdAt.ToString(TimestampFormat, CultureInfo.InvariantCulture)}-{Suffixes[kind]}{Extension}";

    /// <summary>
    /// Розпізнає файл копії за назвою.
    /// </summary>
    /// <param name="fileName">Назва файлу (без каталогу).</param>
    /// <returns>Опис копії або <c>null</c>, якщо це не файл копії програми.</returns>
    public static BackupFile? TryParse(string fileName)
    {
        Match match = FileNamePattern().Match(fileName);
        if (!match.Success
            || !DateTime.TryParseExact(match.Groups["time"].Value, TimestampFormat, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out DateTime createdAt))
        {
            return null;
        }

        BackupKind kind = Suffixes.First(pair => pair.Value == match.Groups["kind"].Value).Key;
        return new BackupFile(fileName, createdAt, kind);
    }

    /// <summary>
    /// Чи вже є автоматична копія за цей день.
    /// </summary>
    /// <param name="backups">Наявні копії.</param>
    /// <param name="day">День.</param>
    /// <returns><c>true</c>, якщо сьогоднішню копію вже зроблено.</returns>
    public static bool HasDailyBackup(IEnumerable<BackupFile> backups, DateOnly day) =>
        backups.Any(b => b.Kind == BackupKind.Daily && DateOnly.FromDateTime(b.CreatedAt) == day);

    /// <summary>
    /// Застарілі автоматичні копії: усі, крім <see cref="DailyCopiesToKeep"/> найновіших.
    /// Ручні й страхувальні копії не видаляються автоматично.
    /// </summary>
    /// <param name="backups">Наявні копії.</param>
    /// <returns>Копії, які треба видалити.</returns>
    public static IReadOnlyList<BackupFile> ExpiredDailyBackups(IEnumerable<BackupFile> backups) =>
        backups
            .Where(b => b.Kind == BackupKind.Daily)
            .OrderByDescending(b => b.CreatedAt)
            .Skip(DailyCopiesToKeep)
            .ToList();

    /// <summary>
    /// Назва виду копії для інтерфейсу.
    /// </summary>
    /// <param name="kind">Вид копії.</param>
    /// <returns>Назва українською.</returns>
    public static string ToDisplayName(this BackupKind kind) => kind switch
    {
        BackupKind.Daily => "Автоматична",
        BackupKind.Manual => "Ручна",
        BackupKind.BeforeRestore => "Перед відновленням",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    [GeneratedRegex(@"^courier-(?<time>\d{8}-\d{6}-\d{3})-(?<kind>auto|manual|before-restore)\.db$")]
    private static partial Regex FileNamePattern();
}
