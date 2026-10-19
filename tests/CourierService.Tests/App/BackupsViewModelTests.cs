using CourierService.App.ViewModels.Administration;
using CourierService.Core.Backups;
using CourierService.Core.Exceptions;

namespace CourierService.Tests.App;

/// <summary>Заглушка резервних копій: зберігає список у пам'яті й запам'ятовує відновлення.</summary>
internal sealed class FakeBackupService : IBackupService
{
    private static readonly DateTime Start = new(2026, 10, 19, 9, 0, 0);

    public List<BackupEntry> Entries { get; } = [];
    public List<string> Restored { get; } = [];
    public DomainException? ThrowOnRestore { get; set; }
    public DomainException? ThrowOnCreate { get; set; }
    public string BackupsDirectory => @"C:\Дані\backups";

    public Task<IReadOnlyList<BackupEntry>> GetBackupsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<BackupEntry>>(Entries.OrderByDescending(e => e.File.CreatedAt).ToList());

    public Task<BackupEntry> CreateBackupAsync(CancellationToken cancellationToken = default)
    {
        if (ThrowOnCreate is not null)
        {
            return Task.FromException<BackupEntry>(ThrowOnCreate);
        }

        return Task.FromResult(Add(BackupKind.Manual));
    }

    public Task<BackupEntry?> CreateDailyBackupIfNeededAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<BackupEntry?>(null);

    public Task<BackupEntry> RestoreAsync(string backupPath, CancellationToken cancellationToken = default)
    {
        if (ThrowOnRestore is not null)
        {
            return Task.FromException<BackupEntry>(ThrowOnRestore);
        }

        Restored.Add(backupPath);
        return Task.FromResult(Add(BackupKind.BeforeRestore));
    }

    public BackupEntry Add(BackupKind kind, long size = 700 * 1024)
    {
        DateTime createdAt = Start.AddHours(Entries.Count);
        string fileName = BackupCatalog.FileNameFor(createdAt, kind);
        var entry = new BackupEntry(new BackupFile(fileName, createdAt, kind), BackupsDirectory + @"\" + fileName, size);
        Entries.Add(entry);
        return entry;
    }
}

public class BackupsViewModelTests
{
    private readonly FakeBackupService _backups = new();
    private readonly FakeDialogService _dialogs = new();
    private readonly FakeFileDialogService _files = new();
    private readonly FakeAppLifetime _lifetime = new();
    private readonly BackupsViewModel _viewModel;

    public BackupsViewModelTests()
    {
        _viewModel = new BackupsViewModel(_backups, _dialogs, _files, _lifetime);
    }

    [Fact]
    public async Task Load_ShowsBackupsNewestFirst_WithReadableKindAndSize()
    {
        _backups.Add(BackupKind.Daily, 640 * 1024);
        _backups.Add(BackupKind.Manual, 1536 * 1024);

        await _viewModel.LoadCommand.ExecuteAsync(null);

        Assert.Equal(["Ручна", "Автоматична"], _viewModel.Backups.Select(b => b.KindText));
        Assert.Equal(["1,5 МБ", "640 КБ"], _viewModel.Backups.Select(b => b.SizeText));
        Assert.Equal("Копій: 2", _viewModel.CountText);
    }

    [Fact]
    public async Task Load_NoBackups_SaysSo()
    {
        await _viewModel.LoadCommand.ExecuteAsync(null);

        Assert.Equal("Копій ще немає.", _viewModel.CountText);
        Assert.False(_viewModel.RestoreSelectedCommand.CanExecute(null));
    }

    [Fact]
    public async Task CreateBackup_AddsAndSelectsNewCopy()
    {
        _backups.Add(BackupKind.Daily);
        await _viewModel.LoadCommand.ExecuteAsync(null);

        await _viewModel.CreateBackupCommand.ExecuteAsync(null);

        Assert.Equal(2, _viewModel.Backups.Count);
        Assert.Equal(BackupKind.Manual, _viewModel.SelectedBackup?.Entry.File.Kind);
        Assert.Contains(_viewModel.SelectedBackup!.Entry.FullPath, Assert.Single(_dialogs.Infos));
    }

    [Fact]
    public async Task CreateBackup_Fails_ShowsError()
    {
        _backups.ThrowOnCreate = new BackupException("Не вдалося створити резервну копію: диск заповнено");

        await _viewModel.CreateBackupCommand.ExecuteAsync(null);

        Assert.Contains("диск заповнено", Assert.Single(_dialogs.Errors));
        Assert.False(_viewModel.IsBusy);
    }

    [Fact]
    public async Task RestoreSelected_BothConfirmed_RestoresAndRestarts()
    {
        BackupEntry backup = _backups.Add(BackupKind.Daily);
        await _viewModel.LoadCommand.ExecuteAsync(null);
        _viewModel.SelectedBackup = _viewModel.Backups.Single();
        _dialogs.ConfirmResult = true;

        await _viewModel.RestoreSelectedCommand.ExecuteAsync(null);

        Assert.Equal(2, _dialogs.ConfirmCalls);
        Assert.Contains("19.10.2026 09:00", _dialogs.Questions[0]);
        Assert.Equal(BackupsViewModel.SecondConfirmationMessage, _dialogs.Questions[1]);
        Assert.Equal([backup.FullPath], _backups.Restored);
        Assert.Contains("перезапуститься", Assert.Single(_dialogs.Infos));
        Assert.Equal(1, _lifetime.RestartCalls);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task RestoreSelected_AnyConfirmationDeclined_DoesNothing(bool first, bool second)
    {
        _backups.Add(BackupKind.Daily);
        await _viewModel.LoadCommand.ExecuteAsync(null);
        _viewModel.SelectedBackup = _viewModel.Backups.Single();
        _dialogs.ConfirmAnswers.Enqueue(first);
        _dialogs.ConfirmAnswers.Enqueue(second);

        await _viewModel.RestoreSelectedCommand.ExecuteAsync(null);

        Assert.Empty(_backups.Restored);
        Assert.Equal(0, _lifetime.RestartCalls);
    }

    [Fact]
    public async Task RestoreSelected_InvalidFile_ShowsReasonAndDoesNotRestart()
    {
        _backups.Add(BackupKind.Daily);
        await _viewModel.LoadCommand.ExecuteAsync(null);
        _viewModel.SelectedBackup = _viewModel.Backups.Single();
        _dialogs.ConfirmResult = true;
        _backups.ThrowOnRestore = new DomainValidationException("Файл резервної копії пошкоджено.");

        await _viewModel.RestoreSelectedCommand.ExecuteAsync(null);

        Assert.Equal("Файл резервної копії пошкоджено.", Assert.Single(_dialogs.Errors));
        Assert.Equal(0, _lifetime.RestartCalls);
        Assert.True(_viewModel.RestoreSelectedCommand.CanExecute(null));
    }

    [Fact]
    public async Task RestoreFromFile_UsesChosenFile()
    {
        _files.BackupPathToReturn = @"E:\флешка\courier-20261001-120000-000-manual.db";
        _dialogs.ConfirmResult = true;

        await _viewModel.RestoreFromFileCommand.ExecuteAsync(null);

        Assert.Contains("courier-20261001-120000-000-manual.db", _dialogs.Questions[0]);
        Assert.Equal([_files.BackupPathToReturn], _backups.Restored);
        Assert.Equal(1, _lifetime.RestartCalls);
    }

    [Fact]
    public async Task RestoreFromFile_Cancelled_DoesNothing()
    {
        _files.BackupPathToReturn = null;

        await _viewModel.RestoreFromFileCommand.ExecuteAsync(null);

        Assert.Equal(0, _dialogs.ConfirmCalls);
        Assert.Empty(_backups.Restored);
    }

    [Fact]
    public void OpenFolder_OpensBackupsDirectory()
    {
        _viewModel.OpenFolderCommand.Execute(null);

        Assert.Equal([_backups.BackupsDirectory], _files.Opened);
    }
}
