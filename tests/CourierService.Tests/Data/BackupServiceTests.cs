using System.IO;
using CourierService.Core.Backups;
using CourierService.Core.Enums;
using CourierService.Core.Exceptions;
using CourierService.Core.Security;
using CourierService.Data;
using CourierService.Data.Seed;
using CourierService.Data.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace CourierService.Tests.Data;

/// <summary>Заглушка резервних копій для тестів, яким копії не потрібні.</summary>
internal sealed class NoBackups : IBackupService
{
    public Exception? ThrowOnDaily { get; init; }
    public int DailyCalls { get; private set; }
    public string BackupsDirectory => string.Empty;

    public Task<IReadOnlyList<BackupEntry>> GetBackupsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<BackupEntry>>([]);

    public Task<BackupEntry> CreateBackupAsync(CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<BackupEntry?> CreateDailyBackupIfNeededAsync(CancellationToken cancellationToken = default)
    {
        DailyCalls++;
        return ThrowOnDaily is null ? Task.FromResult<BackupEntry?>(null) : Task.FromException<BackupEntry?>(ThrowOnDaily);
    }

    public Task<BackupEntry> RestoreAsync(string backupPath, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
}

/// <summary>
/// Резервні копії перевіряються на справжніх файлах SQLite у тимчасовому каталозі.
/// </summary>
public sealed class BackupServiceTests : IAsyncLifetime
{
    private static readonly UserSession Admin = new(1, "admin", "Олена Марченко", UserRole.Admin, null);
    private static readonly UserSession Operator = new(2, "operator", "Ігор Бондар", UserRole.Operator, null);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "courier-backup-tests", Guid.NewGuid().ToString("N"));
    private readonly FixedClock _clock = new(new DateTime(2026, 10, 19, 9, 0, 0));
    private readonly CurrentUser _currentUser = new();
    private readonly BackupService _service;

    public BackupServiceTests()
    {
        Directory.CreateDirectory(_root);
        _service = new BackupService(
            new BackupLocation(DatabasePath, BackupsDirectory),
            new FileDbContextFactory(DatabasePath),
            _currentUser,
            _clock,
            NullLogger<BackupService>.Instance);
        _currentUser.SignIn(Admin);
    }

    private string DatabasePath => Path.Combine(_root, "courier.db");

    private string BackupsDirectory => Path.Combine(_root, "backups");

    public async Task InitializeAsync()
    {
        await using var context = new FileDbContextFactory(DatabasePath).CreateDbContext();
        await context.Database.MigrateAsync();
    }

    public Task DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_root, recursive: true);
        return Task.CompletedTask;
    }

    [Fact]
    public async Task CreateBackup_Admin_CopiesCurrentData()
    {
        Execute(DatabasePath, "CREATE TABLE Marker (Value TEXT); INSERT INTO Marker VALUES ('до копії')");

        BackupEntry backup = await _service.CreateBackupAsync();

        Assert.Equal(BackupKind.Manual, backup.File.Kind);
        Assert.Equal(Path.Combine(BackupsDirectory, backup.File.FileName), backup.FullPath);
        Assert.True(backup.SizeBytes > 0);
        Assert.Equal("до копії", Scalar(backup.FullPath, "SELECT Value FROM Marker"));
        Assert.Empty(Directory.GetFiles(BackupsDirectory, "*.tmp"));
    }

    [Fact]
    public async Task ManualOperations_NotAdmin_AreDenied()
    {
        _currentUser.SignIn(Operator);

        await Assert.ThrowsAsync<AccessDeniedException>(() => _service.CreateBackupAsync());
        await Assert.ThrowsAsync<AccessDeniedException>(() => _service.GetBackupsAsync());
        await Assert.ThrowsAsync<AccessDeniedException>(() => _service.RestoreAsync(DatabasePath));
    }

    [Fact]
    public async Task DailyBackup_WorksBeforeSignIn_OncePerDay()
    {
        _currentUser.SignOut();

        BackupEntry? first = await _service.CreateDailyBackupIfNeededAsync();
        _clock.Now = _clock.Now.AddHours(5);
        BackupEntry? sameDay = await _service.CreateDailyBackupIfNeededAsync();
        _clock.Now = _clock.Now.AddDays(1);
        BackupEntry? nextDay = await _service.CreateDailyBackupIfNeededAsync();

        Assert.Equal(BackupKind.Daily, first?.File.Kind);
        Assert.Null(sameDay);
        Assert.NotNull(nextDay);
        Assert.Equal(2, Directory.GetFiles(BackupsDirectory).Length);
    }

    [Fact]
    public async Task DailyBackup_NoDatabaseYet_DoesNothing()
    {
        SqliteConnection.ClearAllPools();
        File.Delete(DatabasePath);

        Assert.Null(await _service.CreateDailyBackupIfNeededAsync());
        Assert.False(Directory.Exists(BackupsDirectory));
    }

    [Fact]
    public async Task DailyBackup_KeepsSevenAutomaticCopies_ManualStay()
    {
        BackupEntry manual = await _service.CreateBackupAsync();
        for (int day = 0; day < 10; day++)
        {
            _clock.Now = _clock.Now.AddDays(1);
            await _service.CreateDailyBackupIfNeededAsync();
        }

        IReadOnlyList<BackupEntry> backups = await _service.GetBackupsAsync();

        Assert.Equal(BackupCatalog.DailyCopiesToKeep, backups.Count(b => b.File.Kind == BackupKind.Daily));
        Assert.Contains(backups, b => b.FullPath == manual.FullPath);
        Assert.Equal(new DateTime(2026, 10, 29, 9, 0, 0), backups[0].File.CreatedAt);
        Assert.Equal(new DateTime(2026, 10, 23, 9, 0, 0), backups.Where(b => b.File.Kind == BackupKind.Daily).Min(b => b.File.CreatedAt));
    }

    [Fact]
    public async Task GetBackups_IgnoresForeignFiles_NewestFirst()
    {
        await _service.CreateBackupAsync();
        _clock.Now = _clock.Now.AddMinutes(1);
        await _service.CreateBackupAsync();
        File.WriteAllText(Path.Combine(BackupsDirectory, "нотатки.txt"), "не копія");
        File.WriteAllText(Path.Combine(BackupsDirectory, "інша.db"), "не копія");

        IReadOnlyList<BackupEntry> backups = await _service.GetBackupsAsync();

        Assert.Equal(2, backups.Count);
        Assert.True(backups[0].File.CreatedAt > backups[1].File.CreatedAt);
    }

    [Fact]
    public async Task GetBackups_NoDirectory_ReturnsEmpty()
    {
        Assert.Empty(await _service.GetBackupsAsync());
    }

    [Fact]
    public async Task Restore_ReplacesData_AndKeepsSafetyCopyOfCurrentData()
    {
        Execute(DatabasePath, "CREATE TABLE Marker (Value TEXT); INSERT INTO Marker VALUES ('до копії')");
        BackupEntry backup = await _service.CreateBackupAsync();
        Execute(DatabasePath, "INSERT INTO Marker VALUES ('після копії')");
        _clock.Now = _clock.Now.AddMinutes(1);

        BackupEntry safetyCopy = await _service.RestoreAsync(backup.FullPath);

        Assert.Equal(1L, Scalar(DatabasePath, "SELECT COUNT(*) FROM Marker"));
        Assert.Equal("до копії", Scalar(DatabasePath, "SELECT Value FROM Marker"));
        Assert.Equal(BackupKind.BeforeRestore, safetyCopy.File.Kind);
        Assert.Equal(2L, Scalar(safetyCopy.FullPath, "SELECT COUNT(*) FROM Marker"));
    }

    [Fact]
    public async Task Backups_AreSingleSelfContainedFiles_LiveDatabaseStaysInWalMode()
    {
        BackupEntry backup = await _service.CreateBackupAsync();
        _clock.Now = _clock.Now.AddMinutes(1);

        await _service.RestoreAsync(backup.FullPath);

        Assert.Equal("delete", Scalar(backup.FullPath, "PRAGMA journal_mode"));
        Assert.Equal("wal", Scalar(DatabasePath, "PRAGMA journal_mode"));
        Assert.Empty(Directory.GetFiles(BackupsDirectory, "*.tmp"));
        Assert.All(Directory.GetFiles(BackupsDirectory), path => Assert.EndsWith(".db", path));
    }

    public static TheoryData<string, string> UnsuitableFiles() => new()
    {
        { "text", "не є резервною копією" },
        { "empty", "не є резервною копією" },
        { "foreign-sqlite", "не є резервною копією" },
        { "newer-version", "новішою версією" },
        { "missing", "не знайдено" },
        { "live-database", "робочої бази" },
    };

    [Theory]
    [MemberData(nameof(UnsuitableFiles))]
    public async Task Restore_UnsuitableFile_RefusesAndLeavesDataUntouched(string kind, string expectedMessage)
    {
        Execute(DatabasePath, "CREATE TABLE Marker (Value TEXT); INSERT INTO Marker VALUES ('робочі дані')");
        string path = Path.Combine(_root, kind + ".db");
        switch (kind)
        {
            case "text":
                File.WriteAllText(path, "Це звичайний текстовий файл, а не база даних. ".PadRight(4096, '.'));
                break;
            case "empty":
                File.WriteAllBytes(path, []);
                break;
            case "foreign-sqlite":
                Execute(path, "CREATE TABLE Notes (Text TEXT)");
                break;
            case "newer-version":
                path = (await _service.CreateBackupAsync()).FullPath;
                Execute(path, "INSERT INTO __EFMigrationsHistory VALUES ('29991231000000_FutureChange', '99.0.0')");
                break;
            case "live-database":
                path = DatabasePath;
                break;
        }

        int backupsBefore = Directory.Exists(BackupsDirectory) ? Directory.GetFiles(BackupsDirectory).Length : 0;

        var error = await Assert.ThrowsAsync<DomainValidationException>(() => _service.RestoreAsync(path));

        Assert.Contains(expectedMessage, error.UserMessage);
        Assert.Equal("робочі дані", Scalar(DatabasePath, "SELECT Value FROM Marker"));
        Assert.Equal(backupsBefore, Directory.Exists(BackupsDirectory) ? Directory.GetFiles(BackupsDirectory).Length : 0);
    }

    [Fact]
    public async Task Initializer_BackupFails_StillPreparesDatabase()
    {
        await using var db = await SqliteTestDatabase.CreateAsync();
        var backups = new NoBackups { ThrowOnDaily = new BackupException("Диск заповнено") };
        var initializer = new DatabaseInitializer(
            db, new DatabaseSeeder(new Pbkdf2PasswordHasher(), _clock), new DemoOrderGenerator(_clock), backups,
            NullLogger<DatabaseInitializer>.Instance);

        await initializer.InitializeAsync();

        Assert.Equal(1, backups.DailyCalls);
        await using var context = db.CreateDbContext();
        Assert.True(await context.Users.AnyAsync());
    }

    private static void Execute(string path, string sql)
    {
        using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static object? Scalar(string path, string sql)
    {
        using var connection = new SqliteConnection($"Data Source={path};Mode=ReadOnly;Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar();
    }

    private sealed class FileDbContextFactory(string path) : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext() =>
            new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite($"Data Source={path};Pooling=False").Options);
    }
}
