using CourierService.Core.Backups;

namespace CourierService.Tests.Core;

public class BackupCatalogTests
{
    private static readonly DateTime Moment = new(2026, 10, 19, 9, 30, 15, 250);

    [Theory]
    [InlineData(BackupKind.Daily, "courier-20261019-093015-250-auto.db")]
    [InlineData(BackupKind.Manual, "courier-20261019-093015-250-manual.db")]
    [InlineData(BackupKind.BeforeRestore, "courier-20261019-093015-250-before-restore.db")]
    public void FileNameFor_ContainsTimeAndKind_AndParsesBack(BackupKind kind, string expected)
    {
        string fileName = BackupCatalog.FileNameFor(Moment, kind);

        Assert.Equal(expected, fileName);
        Assert.Equal(new BackupFile(fileName, Moment, kind), BackupCatalog.TryParse(fileName));
    }

    [Theory]
    [InlineData("courier.db")] // сама робоча база
    [InlineData("courier-20261019-093015-250-auto.db.tmp")] // незавершена копія
    [InlineData("courier-20261019-093015-250-weekly.db")]
    [InlineData("courier-20261399-093015-250-auto.db")] // неіснуюча дата
    [InlineData("courier-20261019-093015-auto.db")]
    [InlineData("COURIER-20261019-093015-250-AUTO.DB")]
    [InlineData("")]
    public void TryParse_ForeignFile_ReturnsNull(string fileName)
    {
        Assert.Null(BackupCatalog.TryParse(fileName));
    }

    [Fact]
    public void HasDailyBackup_OnlyAutomaticCopyOfThatDayCounts()
    {
        var backups = new[]
        {
            Copy(new DateTime(2026, 10, 18, 23, 59, 0), BackupKind.Daily),
            Copy(new DateTime(2026, 10, 19, 10, 0, 0), BackupKind.Manual),
        };

        Assert.False(BackupCatalog.HasDailyBackup(backups, new DateOnly(2026, 10, 19)));
        Assert.True(BackupCatalog.HasDailyBackup(backups, new DateOnly(2026, 10, 18)));
        Assert.False(BackupCatalog.HasDailyBackup([], new DateOnly(2026, 10, 19)));
    }

    [Fact]
    public void ExpiredDailyBackups_KeepsSevenNewestAutomatic_AndAllOthers()
    {
        var daily = Enumerable.Range(1, 10)
            .Select(day => Copy(new DateTime(2026, 10, day, 9, 0, 0), BackupKind.Daily))
            .ToList();
        var others = new[]
        {
            Copy(new DateTime(2026, 9, 1, 12, 0, 0), BackupKind.Manual),
            Copy(new DateTime(2026, 9, 2, 12, 0, 0), BackupKind.BeforeRestore),
        };

        var expired = BackupCatalog.ExpiredDailyBackups(daily.Concat(others).Reverse());

        Assert.Equal([daily[2], daily[1], daily[0]], expired);
    }

    [Fact]
    public void ExpiredDailyBackups_SevenOrFewer_NothingExpires()
    {
        var daily = Enumerable.Range(1, BackupCatalog.DailyCopiesToKeep)
            .Select(day => Copy(new DateTime(2026, 10, day, 9, 0, 0), BackupKind.Daily));

        Assert.Empty(BackupCatalog.ExpiredDailyBackups(daily));
    }

    [Theory]
    [InlineData(BackupKind.Daily, "Автоматична")]
    [InlineData(BackupKind.Manual, "Ручна")]
    [InlineData(BackupKind.BeforeRestore, "Перед відновленням")]
    public void ToDisplayName_IsUkrainian(BackupKind kind, string expected)
    {
        Assert.Equal(expected, kind.ToDisplayName());
    }

    private static BackupFile Copy(DateTime createdAt, BackupKind kind) =>
        new(BackupCatalog.FileNameFor(createdAt, kind), createdAt, kind);
}
